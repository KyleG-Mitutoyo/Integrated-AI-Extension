using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using HandyControl.Controls;
using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;

namespace Integrated_AI
{
    /// <summary>
    /// Interaction logic for ChatWindow.xaml
    /// </summary>
    public partial class ChatWindow : UserControl
    {
        private readonly List<UrlOption> _urlOptions = new List<UrlOption>
        {
            new UrlOption { DisplayName = "Grok", Url = "https://www.grok.com" },
            new UrlOption { DisplayName = "Gemini", Url = "https://aistudio.google.com" }
        };

        private string _userDataFolder;

        public ChatWindow()
        {
            InitializeComponent();
            var dummy = typeof(HandyControl.Controls.Window);
            UrlSelector.ItemsSource = _urlOptions;
            InitializeWebView2Async();
        }

        private async void InitializeWebView2Async()
        {
            try
            {
                // Use a user-specific folder in AppData to store WebView2 data (more secure than MyDocuments)
                _userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIChatExtension",
                    Environment.UserName); // Append username to isolate data per user
                Directory.CreateDirectory(_userDataFolder); // Ensure folder exists

                // Set WebView2 environment with custom user data folder
                var env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
                await ChatWebView.EnsureCoreWebView2Async(env);

                // Configure WebView2 settings for better session handling
                ChatWebView.CoreWebView2.Settings.IsWebMessageEnabled = true; // Enable JavaScript communication if needed
                ChatWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true; // Allow context menus for user interaction
                ChatWebView.CoreWebView2.Settings.IsStatusBarEnabled = false; // Disable status bar for cleaner UI
                ChatWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"WebView2 initialization failed: {ex.Message}", "Error");
                return;
            }

            // Handle WebView2 initialization completion
            ChatWebView.CoreWebView2InitializationCompleted += (s, e) =>
            {
                MessageBox.Show(e.IsSuccess ? "WebView2 initialized successfully." : $"WebView2 initialization failed: {e.InitializationException?.Message}", "Initialization");
                if (e.IsSuccess)
                {
                    // Optional: Inspect cookies on startup to verify session persistence
                    // CheckCookiesAsync();
                }
            };

            // Handle navigation completion to monitor login status
            ChatWebView.NavigationCompleted += async (s, e) =>
            {
                if (!e.IsSuccess)
                {
                    MessageBox.Show($"Navigation failed: {e.WebErrorStatus}", "Navigation Error");
                }
                else
                {
                    // Optional: Check if user is logged in by inspecting page content or cookies
                    // await CheckLoginStatusAsync();
                }
            };
        }

        private async void CheckCookiesAsync()
        {
            try
            {
                // Get cookies for the current URL
                var cookies = await ChatWebView.CoreWebView2.CookieManager.GetCookiesAsync(ChatWebView.Source?.ToString());
                if (cookies.Any())
                {
                    // Log or inspect cookies to verify session persistence (for debugging)
                    var cookieNames = string.Join(", ", cookies.Select(c => c.Name));
                    MessageBox.Show($"Cookies found: {cookieNames}", "Cookie Check");
                }
                else
                {
                    MessageBox.Show("No cookies found. User may need to log in.", "Cookie Check");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error checking cookies: {ex.Message}", "Error");
            }
        }

        private async Task CheckLoginStatusAsync()
        {
            try
            {
                // Example: Inject JavaScript to check if a login-specific element exists
                // Adjust the selector based on the target site's HTML
                string script = "document.querySelector('.logged-in-user') !== null ? 'true' : 'false';";
                string result = await ChatWebView.ExecuteScriptAsync(script);
                bool isLoggedIn = result.Trim('"') == "true";
                MessageBox.Show(isLoggedIn ? "User is logged in." : "User is not logged in.", "Login Status");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error checking login status: {ex.Message}", "Error");
            }
        }

        private void UrlSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UrlSelector.SelectedItem is UrlOption selectedOption && !string.IsNullOrEmpty(selectedOption.Url))
            {
                try
                {
                    ChatWebView.Source = new Uri(selectedOption.Url);
                }
                catch (UriFormatException ex)
                {
                    MessageBox.Show($"Invalid URL format: {ex.Message}", "Error");
                    ChatWebView.Source = new Uri("https://www.grok.com");
                }
            }
            else
            {
                ChatWebView.Source = new Uri("https://www.grok.com");
            }
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var dte = ServiceProvider.GlobalProvider.GetService(typeof(DTE)) as DTE2;
            if (dte?.ActiveDocument != null)
            {
                var textSelection = (EnvDTE.TextSelection)dte.ActiveDocument.Selection;
                if (textSelection != null && !string.IsNullOrEmpty(textSelection.Text))
                {
                    Clipboard.SetText(textSelection.Text);
                    MessageBox.Show("Text copied to clipboard.", "Success");
                }
                else
                {
                    MessageBox.Show("No text selected in the active document.", "Warning");
                }
            }
            else
            {
                MessageBox.Show("No active document found.", "Warning");
            }
        }

        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Clipboard.ContainsText())
            {
                string clipboardText = Clipboard.GetText();
                // Escape special characters to prevent JavaScript injection
                clipboardText = clipboardText.Replace("\\", "\\\\").Replace("'", "\\'").Replace("\n", "\\n");
                // Inject clipboard text into the chat input field
                // Adjust the selector based on the target site's input field ID or class
                ChatWebView.ExecuteScriptAsync($"document.querySelector('textarea, [role=\"textbox\"]').value = '{clipboardText}';");
            }
        }

        // Optional: Add a button to clear session data (e.g., for logout or switching users)
        //private void ClearSessionButton_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        // Clear all cookies
        //        ChatWebView.CoreWebView2.CookieManager.DeleteAllCookies();
        //        // Clear cache (optional)
        //        ChatWebView.CoreWebView2.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite | CoreWebView2BrowsingDataKinds.CacheStorage);
        //        MessageBox.Show("Session data cleared. Please log in again.", "Success");

        //        // Navigate to the current URL to force re-authentication
        //        ChatWebView.Reload();
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show($"Error clearing session data: {ex.Message}", "Error");
        //    }
        //}

        private class UrlOption
        {
            public string DisplayName { get; set; }
            public string Url { get; set; }
        }
    }
}