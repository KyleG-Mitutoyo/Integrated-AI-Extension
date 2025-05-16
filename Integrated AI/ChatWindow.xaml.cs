using EnvDTE;
using EnvDTE80;
using HandyControl.Controls;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace Integrated_AI
{
    public partial class ChatWindow : UserControl
    {
        private readonly List<UrlOption> _urlOptions = new List<UrlOption>
        {
            new UrlOption { DisplayName = "Grok", Url = "https://grok.com" },
            new UrlOption { DisplayName = "Google AI Studio", Url = "https://aistudio.google.com" },
            new UrlOption { DisplayName = "ChatGPT", Url = "https://chat.openai.com" }
        };

        private string _userDataFolder;

        public ChatWindow()
        {
            InitializeComponent();
            // Required for HandyControl XAML compilation
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
                    "AIChatExtension",
                    Environment.UserName);
                Directory.CreateDirectory(_userDataFolder);

                var env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
                await ChatWebView.EnsureCoreWebView2Async(env);

                ChatWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                ChatWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                ChatWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            ChatWebView.CoreWebView2InitializationCompleted += (s, e) =>
            {
                if (e.IsSuccess && _urlOptions.Any())
                {
                    UrlSelector.SelectedIndex = 0; // Load first URL
                }
                else
                {
                    MessageBox.Show($"WebView2 initialization failed: {e.InitializationException?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            ChatWebView.NavigationCompleted += (s, e) =>
            {
                if (!e.IsSuccess)
                {
                    MessageBox.Show($"Failed to load {ChatWebView.Source?.ToString() ?? "page"}: {e.WebErrorStatus}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            };
        }

        private void UrlSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (UrlSelector.SelectedItem is UrlOption selectedOption && !string.IsNullOrEmpty(selectedOption.Url))
            {
                try
                {
                    if (ChatWebView?.CoreWebView2 != null)
                    {
                        ChatWebView.Source = new Uri(selectedOption.Url);
                    }
                }
                catch (UriFormatException ex)
                {
                    MessageBox.Show($"Invalid URL '{selectedOption.Url}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    ChatWebView.Source = new Uri("https://grok.com");
                }
            }
        }

        private string _selectedOption = "Code -> AI"; // Default option
        private bool _executeCommandOnClick = true; // Flag to control Click event

        private async void SplitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_executeCommandOnClick)
            {
                // Execute the current command when the button's text/content is clicked
                await ExecuteCommandAsync(_selectedOption);
            }
            // Reset flag for next click
            _executeCommandOnClick = true;
        }

        private void SplitButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            var splitButton = sender as SplitButton;
            if (splitButton == null) return;

            // Get click position relative to the SplitButton
            var clickPosition = e.GetPosition(splitButton);
            // Estimate the dropdown arrow area (rightmost 20% of the button)
            // Adjust the threshold (0.8) based on your SplitButton's appearance
            if (clickPosition.X > splitButton.ActualWidth * 0.7)
            {
                // Click is likely on the arrow; skip command execution
                _executeCommandOnClick = false;
            }
            else
            {
                // Click is on the text/content area; allow command execution
                _executeCommandOnClick = true;
            }
        }

        private async void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string option)
            {
                _selectedOption = option;
                VSToAISplitButton.Content = option; // Update button content
                await ExecuteCommandAsync(option); // Execute the selected command immediately
            }
        }

        private async Task ExecuteCommandAsync(string option)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await ServiceProvider.GetGlobalServiceAsync(typeof(DTE)) as DTE2;
            if (dte?.ActiveDocument == null)
            {
                MessageBox.Show("No active document in Visual Studio.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (option == "Code -> AI")
            {
                var textSelection = (TextSelection)dte.ActiveDocument.Selection;
                if (string.IsNullOrEmpty(textSelection?.Text))
                {
                    MessageBox.Show("No text selected in Visual Studio.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                await InjectTextIntoWebViewAsync(textSelection.Text, "VS Selection");
            }
            else if (option == "File -> AI")
            {
                var textDocument = (TextDocument)dte.ActiveDocument.Object("TextDocument");
                var text = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
                if (string.IsNullOrEmpty(text))
                {
                    MessageBox.Show("The active document is empty.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                await InjectTextIntoWebViewAsync(text, "VS Full File");
            }
        }

        private async Task InjectTextIntoWebViewAsync(string textToInject, string sourceOfText)
        {
            if (ChatWebView?.CoreWebView2 == null)
            {
                MessageBox.Show("Web view is not ready. Please wait for the page to load.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Escape text for JavaScript
            string escapedText = textToInject
                .Replace("\\", "\\\\")
                .Replace("'", "\\'")
                .Replace("\n", "\\n")
                .Replace("\r", "\\r")
                .Replace("\t", "\\t");

            // Map URLs to specific selectors for AI chat sites
            var selectorMap = new Dictionary<string, string>
            {
                { "https://grok.com", "textarea[aria-label=\"Ask Grok anything\"]" },
                { "https://chat.openai.com", "div[contenteditable=\"true\"][data-testid=\"conversation-input\"]" },
                { "https://aistudio.google.com", "textarea[aria-label=\"Type something or tab to choose an example prompt\"]" }
            };

            string currentUrl = ChatWebView.Source?.ToString() ?? "";
            string selector = selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value ?? "textarea"; // Fallback to generic textarea

            // JavaScript to inject text without sending
            string script = $@"
(function() {{
    try {{
        const elem = document.querySelector('{selector}');
        if (!elem) return 'FAILURE: Input field not found for selector: {selector}';
        
        // Verify element is visible and interactable
        const style = window.getComputedStyle(elem);
        if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') {{
            return 'FAILURE: Input field is not visible or interactable for selector: {selector}';
        }}

        elem.focus();
        if (elem.value !== undefined) {{ // <textarea> or <input>
            elem.value = '{escapedText}';
            elem.dispatchEvent(new Event('input', {{ bubbles: true }}));
            elem.dispatchEvent(new Event('change', {{ bubbles: true }}));
            return 'SUCCESS: Text injected into {selector} (value set)';
        }} else if (elem.isContentEditable) {{ // contenteditable Atlantis
            elem.textContent = '{escapedText}';
            elem.dispatchEvent(new Event('input', {{ bubbles: true }}));
            return 'SUCCESS: Text injected into {selector} (contenteditable)';
        }}
        return 'FAILURE: Element is neither a text input nor contenteditable for selector: {selector}';
    }} catch (e) {{
        return 'FAILURE: Error: ' + e.message;
    }}
}})();";

            try
            {
                string result = await ChatWebView.ExecuteScriptAsync(script);
                result = result?.Trim('"') ?? "null"; // Remove JSON quotes and handle null
                if (result == "null" || result.Contains("FAILURE"))
                {
                    MessageBox.Show($"Failed to inject {sourceOfText}: {result.Replace("FAILURE: ", "")}", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    Log($"InjectTextIntoWebViewAsync: {result}"); // Log success to custom pane
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error injecting {sourceOfText}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Log(string message)
        {
            try
            {
                var outputWindow = ServiceProvider.GetGlobalServiceAsync(typeof(SVsOutputWindow)).Result as IVsOutputWindow;
                if (outputWindow == null)
                {
                    Debug.WriteLine($"Log failed: Output window service unavailable - {message}");
                    return;
                }

                // Use a custom pane for the extension
                Guid paneGuid = new Guid("D7E1B3F6-3E3E-4E9E-BE6A-7A8D6B5A3C9F"); // Unique GUID for AI Chat Extension
                IVsOutputWindowPane pane;
                int hr = outputWindow.GetPane(ref paneGuid, out pane);
                if (hr < 0 || pane == null) // Pane doesn't exist, create it
                {
                    hr = outputWindow.CreatePane(ref paneGuid, "AI Chat Extension", 1, 0);
                    if (hr >= 0)
                    {
                        hr = outputWindow.GetPane(ref paneGuid, out pane);
                    }
                }

                if (hr >= 0 && pane != null)
                {
                    pane.OutputStringThreadSafe($"{DateTime.Now}: {message}\n");
                }
                else
                {
                    Debug.WriteLine($"Log failed: Could not create or get custom pane (HRESULT: {hr}) - {message}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Log failed: {ex.Message} - {message}");
            }
        }

        private class UrlOption
        {
            public string DisplayName { get; set; }
            public string Url { get; set; }
        }
    }
}