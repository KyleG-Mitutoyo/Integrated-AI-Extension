using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
// Add this using for System.Text.Json
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using JsonException = System.Text.Json.JsonException;
using JsonSerializer = System.Text.Json.JsonSerializer;

// The following using might be for HandyControl UI elements if you use them in XAML.
// using HandyControl.Controls; 
// Explicitly use System.Windows.MessageBox to avoid ambiguity if HandyControl also defines MessageBox.
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
            // You can add more AI chat service URLs here
        };

        private string _userDataFolder;

        // Helper class for deserializing script execution results from JavaScript
        private class ScriptExecutionResult
        {
            public bool success { get; set; }
            public string message { get; set; }
        }

        public ChatWindow()
        {
            InitializeComponent();
            // The following line for HandyControl is likely for referencing its assemblies 
            // or ensuring its styles are loaded if you're using HandyControl components.
            var dummy = typeof(HandyControl.Controls.Window);
            UrlSelector.ItemsSource = _urlOptions;
            InitializeWebView2Async();
        }

        private async void InitializeWebView2Async()
        {
            try
            {
                _userDataFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "AIChatExtension", // A dedicated folder for your extension's data
                    Environment.UserName); // User-specific subfolder to keep data isolated
                Directory.CreateDirectory(_userDataFolder); // Ensure the folder exists

                var env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
                await ChatWebView.EnsureCoreWebView2Async(env);

                // Configure WebView2 settings
                ChatWebView.CoreWebView2.Settings.IsWebMessageEnabled = true; // Important for some JS interop
                ChatWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true; // Standard browser context menus
                ChatWebView.CoreWebView2.Settings.IsStatusBarEnabled = false; // Cleaner UI
                // Optional: Set a specific UserAgent if a site requires it
                // ChatWebView.CoreWebView2.Settings.UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64)...";
            }
            catch (Exception ex)
            {
                // Critical error if WebView2 cannot be initialized
                MessageBox.Show($"WebView2 initialization critical error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ChatWebView.CoreWebView2InitializationCompleted += (s, e) =>
            {
                // Consider replacing this MessageBox with logging for a smoother user experience
                // System.Diagnostics.Debug.WriteLine(e.IsSuccess ? "WebView2 initialized successfully." : $"WebView2 initialization failed: {e.InitializationException?.Message}");
                if (e.IsSuccess)
                {
                    // Automatically load the first URL from the list, or a saved preference
                    if (UrlSelector.ItemsSource is List<UrlOption> options && options.Any())
                    {
                        UrlSelector.SelectedIndex = 0; // Default to the first item
                        // Or, you could implement logic to remember and load the last used URL
                    }
                }
                else
                {
                    MessageBox.Show($"WebView2 initialization failed: {e.InitializationException?.Message}", "WebView2 Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            ChatWebView.NavigationCompleted += async (s, e) =>
            {
                if (!e.IsSuccess)
                {
                    MessageBox.Show($"Navigation to {ChatWebView.Source?.ToString() ?? "the selected URL"} failed. Error: {e.WebErrorStatus}", "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                // else: Navigation was successful.
                // You could call your CheckLoginStatusAsync here if needed:
                // await CheckLoginStatusAsync();
            };
        }

        // CheckCookiesAsync and CheckLoginStatusAsync are useful for debugging session persistence.
        // They remain unchanged from your original code.
        private async void CheckCookiesAsync()
        {
            try
            {
                if (ChatWebView == null || ChatWebView.CoreWebView2 == null) return;
                var cookies = await ChatWebView.CoreWebView2.CookieManager.GetCookiesAsync(ChatWebView.Source?.ToString());
                if (cookies.Any())
                {
                    var cookieNames = string.Join(", ", cookies.Select(c => c.Name));
                    MessageBox.Show($"Cookies found: {cookieNames}", "Cookie Check");
                }
                else
                {
                    MessageBox.Show("No cookies found for the current site. User may need to log in.", "Cookie Check");
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
                if (ChatWebView == null || ChatWebView.CoreWebView2 == null) return;
                // This script is an example; you'll need to adjust the selector for specific sites.
                string script = "document.querySelector('.user-profile-indicator, .logged-in-status') !== null ? 'true' : 'false';";
                string result = await ChatWebView.ExecuteScriptAsync(script);
                bool isLoggedIn = result != null && result.Trim('"') == "true";
                MessageBox.Show(isLoggedIn ? "User appears to be logged in." : "User does not appear to be logged in, or status element not found.", "Login Status Check");
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
                    if (ChatWebView != null && ChatWebView.CoreWebView2 != null) // Ensure WebView is ready
                    {
                        ChatWebView.Source = new Uri(selectedOption.Url);
                    }
                    // If CoreWebView2 is null, it might still be initializing. 
                    // The CoreWebView2InitializationCompleted event will handle the first navigation if needed.
                }
                catch (UriFormatException ex)
                {
                    MessageBox.Show($"The URL '{selectedOption.Url}' is invalid: {ex.Message}", "Invalid URL", MessageBoxButton.OK, MessageBoxImage.Error);
                    ChatWebView.Source = new Uri("https://www.grok.com");
                }
            }
        }

        // This method now implements "VS -> AI" functionality.
        // In your XAML, this handler should be connected to your "VS -> AI" button.
        private async void VStoAI_Click(object sender, RoutedEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(); // Ensure on UI thread

            var dte = await ServiceProvider.GetGlobalServiceAsync(typeof(DTE)) as DTE2;
            if (dte?.ActiveDocument?.Selection == null)
            {
                MessageBox.Show("Cannot access Visual Studio's active document or selection.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var textSelection = (EnvDTE.TextSelection)dte.ActiveDocument.Selection;
            if (textSelection == null || string.IsNullOrEmpty(textSelection.Text))
            {
                MessageBox.Show("No text selected in Visual Studio's active document.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string selectedText = textSelection.Text;
            await InjectTextIntoWebViewAsync(selectedText, "VS Selection");
        }

        // This method now implements "Clipboard -> AI" functionality.
        private async void ClipboardtoAI_Click(object sender, RoutedEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(); // Ensure on UI thread

            if (!Clipboard.ContainsText())
            {
                MessageBox.Show("Clipboard does not contain any text.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string clipboardText = Clipboard.GetText();
            await InjectTextIntoWebViewAsync(clipboardText, "Clipboard content");
        }

        // Helper method to inject text into the WebView's active chat input

        private async Task InjectTextIntoWebViewAsync(string textToInject, string sourceOfText)
        {
            Debug.WriteLine($"InjectText DOM_MANIPULATION_NO_JSON: Entered for '{sourceOfText}' with text length {textToInject.Length}");

            if (ChatWebView == null || ChatWebView.CoreWebView2 == null)
            {
                Debug.WriteLine("InjectText DOM_MANIPULATION_NO_JSON: WebView not ready.");
                MessageBox.Show("The web view is not ready. Please wait for it to load.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 1. Manual JavaScript string escaping
            string escapedTextToInject = textToInject
                .Replace("\\", "\\\\") // Must be first
                .Replace("'", "\\'")
                .Replace("\"", "\\\"")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t")
                .Replace("`", "\\`"); // Escape backticks as they are JS template literal delimiters

            // Also escape the sourceOfText string if you plan to use it directly in the JS for messages
            string escapedSourceName = sourceOfText
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\"", "\\\"")
                .Replace("`", "\\`");

            Debug.WriteLine($"InjectText DOM_MANIPULATION_NO_JSON: Escaped text preview (first 100 chars): {escapedTextToInject.Substring(0, Math.Min(escapedTextToInject.Length, 100))}");

            try
            {
                // 2. Construct the JavaScript for DOM manipulation
                // Using JavaScript template literal (backticks) for `textToUse` for easier multi-line handling.
                string script = $@"
(function() {{
    const textToUse = `{escapedTextToInject}`;
    const sourceNameStr = '{escapedSourceName}';
    const selectors = [
        'textarea',                                       // Standard textarea
        'input[type=""text""]',                             // Standard text input
        '[role=""textbox""]',                               // ARIA textbox role
        'div[contenteditable=""true""]',                    // General contenteditable div
        // Add more specific selectors for AI chat sites if needed, e.g.:
        // 'div[aria-label*=""message""][contenteditable=""true""]', 
        // 'div[data-lexical-editor][contenteditable=""true""]', // For Gemini
        // '#prompt-textarea' // For ChatGPT (example, selector might change)
    ];
    let injected = false;
    let targetInfo = 'N/A';
    let errorMessage = '';

    for (const selector of selectors) {{
        try {{
            const elem = document.querySelector(selector);
            if (elem) {{
                targetInfo = (elem.tagName || 'UNKNOWN_TAG') +
                             (elem.id ? '#' + elem.id : '') +
                             (elem.className && typeof elem.className === 'string' ? '.' + elem.className.trim().replace(/\s+/g, '.') : '');
                
                elem.focus(); // Bring focus to the element

                if (typeof elem.value !== 'undefined' && elem.value !== null && !elem.isContentEditable) {{ // Handles <textarea>, <input>
                    elem.value = textToUse; // Set the value
                    // Dispatch 'input' and 'change' events for frameworks like React, Vue, Angular
                    elem.dispatchEvent(new Event('input', {{ bubbles: true, cancelable: true }}));
                    elem.dispatchEvent(new Event('change', {{ bubbles: true, cancelable: true }}));
                    injected = true;
                    break;
                }} else if (elem.isContentEditable) {{ // Handles contenteditable elements
                    // For contenteditable, 'insertText' is often preferred.
                    // This sequence attempts to replace existing content.
                    document.execCommand('selectAll', false, null); 
                    document.execCommand('insertText', false, textToUse);
                    // Dispatch 'input' event, as some editors might listen for it
                    elem.dispatchEvent(new Event('input', {{ bubbles: true, cancelable: true }}));
                    injected = true;
                    break;
                }}
            }}
        }} catch (e) {{
            // Catch errors during DOM interaction with a specific selector
            errorMessage = 'Error with selector ""' + selector + '"": ' + e.message;
            // Log to console for more detailed debugging in WebView DevTools if needed
            console.error('Error during text injection attempt with selector: ' + selector, e); 
            // Continue to try next selector
        }}
    }}

    if (injected) {{
        return 'SUCCESS: ' + sourceNameStr + ' injected into ' + targetInfo + '.';
    }} else {{
        let finalMsg = 'FAILURE: Could not find a suitable input field for ' + sourceNameStr + '. Tried ' + selectors.length + ' selectors.';
        if (errorMessage) {{
            finalMsg += ' Last error: ' + errorMessage;
        }}
        return finalMsg;
    }}
}})();";

                Debug.WriteLine("InjectText DOM_MANIPULATION_NO_JSON: About to execute script.");
                string scriptResult = await ChatWebView.ExecuteScriptAsync(script);
                Debug.WriteLine($"InjectText DOM_MANIPULATION_NO_JSON: Script executed. Raw string result: {scriptResult}");

                if (scriptResult != null)
                {
                    bool success = scriptResult.StartsWith("SUCCESS:");
                    //MessageBox.Show(scriptResult,
                    //                success ? "Success" : "Operation Note",
                    //                MessageBoxButton.OK,
                    //                success ? MessageBoxImage.Information : MessageBoxImage.Warning);
                }
                else
                {
                    MessageBox.Show($"Script for {sourceOfText} returned no result (null). Text may not have been injected.", "Script Execution Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex) // Catches C# side exceptions or critical WebView2 errors
            {
                Debug.WriteLine($"InjectText DOM_MANIPULATION_NO_JSON: C# EXCEPTION for '{sourceOfText}' - {ex.ToString()}");
                MessageBox.Show($"An error occurred while trying to inject text from {sourceOfText}: {ex.Message}\n\nEnsure the target web page is fully loaded and the chat input is visible.", "Injection Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            Debug.WriteLine($"InjectText DOM_MANIPULATION_NO_JSON: Exiting for '{sourceOfText}'");
        }

        // The ClearSessionButton_Click method is commented out as in your original code.
        // Uncomment and adapt if you need this functionality.
        //private async void ClearSessionButton_Click(object sender, RoutedEventArgs e)
        //{
        //    try
        //    {
        //        if (ChatWebView != null && ChatWebView.CoreWebView2 != null)
        //        {
        //            await ChatWebView.CoreWebView2.CookieManager.DeleteAllCookiesAsync();
        //            await ChatWebView.CoreWebView2.ClearBrowsingDataAsync(CoreWebView2BrowsingDataKinds.AllSite | CoreWebView2BrowsingDataKinds.CacheStorage);
        //            MessageBox.Show("Session data (cookies and cache) cleared. You may need to log in again.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
        //            ChatWebView.Reload(); // Reload to apply changes and prompt for login if necessary
        //        }
        //    }
        //    catch (Exception ex)
        //    {
        //        MessageBox.Show($"Error clearing session data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        //    }
        //}

        // UrlOption class definition (kept private as in original)
        private class UrlOption
        {
            public string DisplayName { get; set; }
            public string Url { get; set; }
        }
    }
}