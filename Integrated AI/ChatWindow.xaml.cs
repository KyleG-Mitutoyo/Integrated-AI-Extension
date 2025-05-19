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
using System.Net; // Required for WebUtility
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
            new UrlOption { DisplayName = "ChatGPT", Url = "https://chatgpt.com" }
        };

        private string _userDataFolder;
        private string _selectedOption = "Code -> AI";
        private bool _executeCommandOnClick = true;

        public ChatWindow()
        {
            InitializeComponent();
            var dummy = typeof(HandyControl.Controls.Window); // Required for HandyControl XAML compilation
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
                ChatWebView.CoreWebView2.Settings.IsStatusBarEnabled = false; // Usually good to hide for cleaner UI
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
                else if (!e.IsSuccess)
                {
                    MessageBox.Show($"WebView2 initialization failed: {e.InitializationException?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            ChatWebView.NavigationCompleted += (s, e) =>
            {
                if (!e.IsSuccess)
                {
                    // Check for common errors like no internet, DNS issues, etc.
                    string errorMessage = $"Failed to load {ChatWebView.Source?.ToString() ?? "page"}. Error status: {e.WebErrorStatus}.";
                    if (e.WebErrorStatus == CoreWebView2WebErrorStatus.CannotConnect ||
                        e.WebErrorStatus == CoreWebView2WebErrorStatus.HostNameNotResolved)
                    {
                        errorMessage += " Please check your internet connection and firewall settings.";
                    }
                    MessageBox.Show(errorMessage, "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                    else
                    {
                        // WebView not ready, could queue the navigation or inform user.
                        // For now, if it's null, InitializeWebView2Async would have shown an error or is still running.
                    }
                }
                catch (UriFormatException ex)
                {
                    MessageBox.Show($"Invalid URL '{selectedOption.Url}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    // Fallback or clear WebView
                }
            }
        }

        

        private async void SplitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_executeCommandOnClick)
            {
                await ExecuteCommandAsync(_selectedOption);
            }
            _executeCommandOnClick = true;
        }

        private void SplitButton_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is SplitButton splitButton)
            {
                var clickPosition = e.GetPosition(splitButton);
                if (clickPosition.X > splitButton.ActualWidth * 0.7) // Adjust threshold if needed
                {
                    _executeCommandOnClick = false;
                }
                else
                {
                    _executeCommandOnClick = true;
                }
            }
        }

        private async void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string option)
            {
                _selectedOption = option;
                VSToAISplitButton.Content = option;
                await ExecuteCommandAsync(option);
            }
        }

        private async Task ExecuteCommandAsync(string option)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            var dte = await ServiceProvider.GetGlobalServiceAsync(typeof(DTE)) as DTE2;
            if (dte == null)
            {
                MessageBox.Show("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (dte.ActiveDocument == null)
            {
                MessageBox.Show("No active document in Visual Studio.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string textToInject = null;
            string sourceDescription = "";

            if (option == "Code -> AI")
            {
                var textSelection = (TextSelection)dte.ActiveDocument.Selection;
                if (string.IsNullOrEmpty(textSelection?.Text))
                {
                    MessageBox.Show("No text selected in Visual Studio.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                textToInject = textSelection.Text;
                sourceDescription = "VS Selection";
            }
            else if (option == "File -> AI")
            {
                if (dte.ActiveDocument.Object("TextDocument") is TextDocument textDocument)
                {
                    var text = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
                    if (string.IsNullOrEmpty(text))
                    {
                        MessageBox.Show("The active document is empty.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    textToInject = text;
                    sourceDescription = "VS Full File";
                }
                else
                {
                    MessageBox.Show("Could not get text document from active document.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            if (textToInject != null)
            {
                await InjectTextIntoWebViewAsync(textToInject, sourceDescription);
            }
        }

        private async Task InjectTextIntoWebViewAsync(string textToInject, string sourceOfText)
        {
            if (ChatWebView?.CoreWebView2 == null)
            {
                MessageBox.Show("Web view is not ready. Please wait for the page to load.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string currentUrl = ChatWebView.Source?.ToString() ?? "";

            // Updated selector map
            var selectorMap = new Dictionary<string, string>
    {
        { "https://grok.com", "textarea[aria-label=\"Ask Grok anything\"]" },
        { "https://chatgpt.com", "div#prompt-textarea" }, // Changed to div for ChatGPT
        { "https://aistudio.google.com", "textarea[aria-label=\"Type something or tab to choose an example prompt\"]" }
    };

            string selector = selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value;
            if (string.IsNullOrEmpty(selector))
            {
                selector = "textarea"; // Fallback
                Log($"Warning: No specific selector for {currentUrl}. Falling back to generic 'textarea'.");
            }

            // Determine if HTML content formatting is needed
            bool useHtmlContent = currentUrl.StartsWith("https://chatgpt.com"); // Enable for ChatGPT
            string preparedTextForJs;

            if (useHtmlContent)
            {
                // Format for ChatGPT's contenteditable div
                string htmlContent = string.Join("",
                    textToInject.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                                .Select(line => "<p>" + WebUtility.HtmlEncode(line) + "</p>")
                );
                preparedTextForJs = htmlContent
                    .Replace("\\", "\\\\")
                    .Replace("'", "\\'")
                    .Replace("`", "\\`");
            }
            else
            {
                // Standard text escaping for textarea
                preparedTextForJs = textToInject
                    .Replace("\\", "\\\\")
                    .Replace("'", "\\'")
                    .Replace("`", "\\`")
                    .Replace("\n", "\\n")
                    .Replace("\r", "\\r")
                    .Replace("\t", "\\t");
            }

            string escapedSelectorForJs = selector.Replace("'", "\\'");

            string script = $@"
(function() {{
    try {{
        const elem = document.querySelector('{escapedSelectorForJs}');
        if (!elem) {{
            return `FAILURE: Input field not found for selector: {escapedSelectorForJs.Replace("`", "\\`")}`;
        }}

        const style = window.getComputedStyle(elem);
        if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0' || elem.disabled) {{
            return `FAILURE: Input field (selector: {escapedSelectorForJs.Replace("`", "\\`")}) is not visible, interactable, or is disabled.`;
        }}

        const textValue = `{preparedTextForJs}`;

        elem.focus();

        if (elem.tagName.toLowerCase() === 'textarea' || elem.tagName.toLowerCase() === 'input') {{
            elem.value = textValue;
            elem.dispatchEvent(new Event('input', {{ bubbles: true, cancelable: true }}));
            return `SUCCESS: Text injected into {escapedSelectorForJs.Replace("`", "\\`")} (value set)`;
        }} else if (elem.isContentEditable) {{
            elem.innerHTML = textValue;
            const range = document.createRange();
            const sel = window.getSelection();
            range.selectNodeContents(elem);
            range.collapse(false);
            sel.removeAllRanges();
            sel.addRange(range);
            elem.dispatchEvent(new Event('input', {{ bubbles: true, cancelable: true }}));
            return `SUCCESS: Text injected into {escapedSelectorForJs.Replace("`", "\\`")} (contenteditable)`;
        }}
        return `FAILURE: Element (selector: {escapedSelectorForJs.Replace("`", "\\`")}) is neither a text input nor contenteditable.`;
    }} catch (e) {{
        return `FAILURE: JavaScript error: ` + e.message + ` (Selector: {escapedSelectorForJs.Replace("`", "\\`")})`;
    }}
}})();";

            try
            {
                string result = await ChatWebView.ExecuteScriptAsync(script);
                result = result?.Trim('"');

                if (result == null || result == "null" || result.StartsWith("FAILURE:"))
                {
                    string failureMessage = result?.Replace("FAILURE: ", "") ?? "Unknown error during script execution.";
                    MessageBox.Show($"Failed to inject {sourceOfText}: {failureMessage}", "Injection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    Log($"InjectTextIntoWebViewAsync: {result}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error injecting {sourceOfText} into WebView: {ex.Message}", "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Log(string message)
        {
            // TODO: Implement proper logging (e.g., to VS Output Pane)
            Debug.WriteLine($"LOG: {message}");
        }

        private class UrlOption
        {
            public string DisplayName { get; set; }
            public string Url { get; set; }
        }
    }
}