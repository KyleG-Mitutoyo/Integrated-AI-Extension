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
                else if (!e.IsSuccess)
                {
                    MessageBox.Show($"WebView2 initialization failed: {e.InitializationException?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            };

            ChatWebView.NavigationCompleted += (s, e) =>
            {
                if (!e.IsSuccess)
                {
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
                }
                catch (UriFormatException ex)
                {
                    MessageBox.Show($"Invalid URL '{selectedOption.Url}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                if (clickPosition.X > splitButton.ActualWidth * 0.7)
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

        private List<FunctionSelectionWindow.FunctionItem> GetFunctionsFromActiveDocument(DTE2 dte)
        {
            var functions = new List<FunctionSelectionWindow.FunctionItem>();
            if (dte.ActiveDocument?.Object("TextDocument") is TextDocument textDocument)
            {
                var codeModel = dte.ActiveDocument.ProjectItem?.FileCodeModel;
                if (codeModel != null)
                {
                    foreach (CodeElement element in codeModel.CodeElements)
                    {
                        CollectFunctions(element, functions);
                    }
                }
            }
            return functions;
        }

        private void CollectFunctions(CodeElement element, List<FunctionSelectionWindow.FunctionItem> functions)
        {
            if (element.Kind == vsCMElement.vsCMElementFunction)
            {
                var codeFunction = (CodeFunction)element;
                string functionCode = codeFunction.StartPoint.CreateEditPoint().GetText(codeFunction.EndPoint);
                string displayName = codeFunction.Name;
                string listBoxDisplayName = $"{codeFunction.Name} ({codeFunction.Parameters.Cast<CodeParameter>().Count()} params)";
                string fullName = $"{codeFunction.FullName}";
                functions.Add(new FunctionSelectionWindow.FunctionItem
                {
                    DisplayName = displayName,
                    ListBoxDisplayName = listBoxDisplayName,
                    FullName = fullName,
                    Function = codeFunction,
                    FullCode = functionCode
                });
            }

            // Recursively check for nested elements (e.g., in classes or namespaces)
            if (element.Children != null)
            {
                foreach (CodeElement child in element.Children)
                {
                    CollectFunctions(child, functions);
                }
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
            string solutionPath = Path.GetDirectoryName(dte.Solution.FullName);
            string filePath = dte.ActiveDocument.FullName;
            string relativePath = "";

            if (!string.IsNullOrEmpty(solutionPath) && filePath.StartsWith(solutionPath, StringComparison.OrdinalIgnoreCase))
            {
                relativePath = filePath.Substring(solutionPath.Length + 1).Replace("\\", "/");
            }
            else
            {
                relativePath = Path.GetFileName(filePath);
            }

            if (option == "Code -> AI")
            {
                var textSelection = (TextSelection)dte.ActiveDocument.Selection;
                if (string.IsNullOrEmpty(textSelection?.Text))
                {
                    MessageBox.Show("No text selected in Visual Studio.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                textToInject = textSelection.Text;
                sourceDescription = $"---{relativePath} (partial code block)---\n{textToInject}\n---End code---\n\n";
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
                    string currentContent = await RetrieveTextFromWebViewAsync();
                    if (currentContent != null && currentContent.Contains($"---From {relativePath} (whole file contents)---"))
                    {
                        MessageBox.Show("This file's contents have already been injected.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }
                    textToInject = text;
                    sourceDescription = $"---{relativePath} (whole file contents)---\n{textToInject}\n---End code---\n\n";
                }
                else
                {
                    MessageBox.Show("Could not get text document from active document.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (option == "Function -> AI")
            {
                var functions = GetFunctionsFromActiveDocument(dte);
                if (!functions.Any())
                {
                    MessageBox.Show("No functions found in the active document.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string recentFunctionsFilePath = Path.Combine(_userDataFolder, "recent_functions.txt");
                var functionSelectionWindow = new FunctionSelectionWindow(functions, recentFunctionsFilePath);
                if (functionSelectionWindow.ShowDialog() == true && functionSelectionWindow.SelectedFunction != null)
                {
                    textToInject = functionSelectionWindow.SelectedFunction.FullCode;
                    sourceDescription = $"---{relativePath} (function: {functionSelectionWindow.SelectedFunction.DisplayName})---\n{textToInject}\n---End code---\n\n";
                }
                else
                {
                    return; // User cancelled selection
                }
            }

            if (textToInject != null)
            {
                await InjectTextIntoWebViewAsync(sourceDescription, option);
            }
        }

        private async Task<string> RetrieveTextFromWebViewAsync()
        {
            if (ChatWebView?.CoreWebView2 == null)
            {
                Log("Web view is not ready. Cannot retrieve text.");
                return null;
            }

            string currentUrl = ChatWebView.Source?.ToString() ?? "";
            var selectorMap = new Dictionary<string, string>
            {
                { "https://grok.com", "textarea[aria-label=\"Ask Grok anything\"]" },
                { "https://chatgpt.com", "div#prompt-textarea" },
                { "https://aistudio.google.com", "textarea[aria-label=\"Type something or tab to choose an example prompt\"]" }
            };

            string selector = selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value;
            if (string.IsNullOrEmpty(selector))
            {
                selector = "textarea"; // Fallback
                Log($"Warning: No specific selector for {currentUrl}. Falling back to generic 'textarea'.");
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
        if (style.display === 'none' || style.visibility == 'hidden' || style.opacity == '0' || elem.disabled) {{
            return `FAILURE: Input field (selector: {escapedSelectorForJs.Replace("`", "\\`")}) is not visible or is disabled.`;
        }}

        let text;
        if (elem.tagName.toLowerCase() == 'textarea' || elem.tagName.toLowerCase() == 'input') {{
            text = elem.value || '';
        }} else if (elem.isContentEditable) {{
            text = elem.innerText || ''; // Use innerText to avoid HTML tags
        }} else {{
            return `FAILURE: Element (selector: {escapedSelectorForJs.Replace("`", "\\`")}) is neither a text input nor contenteditable.`;
        }}

        // Normalize newlines and remove literal escape sequences
        text = text.replace(/\\\\n/g, '\\n');
        return text;
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
                    Log($"Failed to retrieve text: {failureMessage}");
                    return null;
                }

                // Further normalize in C# to ensure consistent newlines
                result = result.Replace("\\n", "\n");
                Log("Text retrieved successfully from WebView.");
                return result;
            }
            catch (Exception ex)
            {
                Log($"Error retrieving text from WebView: {ex.Message}");
                return null;
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
            var selectorMap = new Dictionary<string, string>
            {
                { "https://grok.com", "textarea[aria-label=\"Ask Grok anything\"]" },
                { "https://chatgpt.com", "div#prompt-textarea" },
                { "https://aistudio.google.com", "textarea[aria-label=\"Type something or tab to choose an example prompt\"]" }
            };

            string selector = selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value ?? "textarea";
            Log($"Using selector: {selector} for URL: {currentUrl}");

            // Normalize input text, preserving trailing newline
            textToInject = textToInject.Replace("\r\n", "\n").Replace("\r", "\n");

            // Prepare text for JavaScript
            string preparedTextForJs;
            bool isChatGpt = currentUrl.StartsWith("https://chatgpt.com");
            if (isChatGpt)
            {
                // Escape backslashes before HTML encoding
                string escapedText = textToInject.Replace("\\", "\\\\");
                var lines = escapedText.Split(new[] { '\n' }, StringSplitOptions.None)
                    .Select(line => string.IsNullOrEmpty(line) ? "<span></span>" : $"<span>{WebUtility.HtmlEncode(line)}</span>");
                preparedTextForJs = $"<div style=\"white-space: pre-wrap; line-height: 1.4;\">{string.Join("<br>", lines)}</div>";
                // Escape quotes and backticks for JavaScript
                preparedTextForJs = preparedTextForJs
                    .Replace("'", "\\'")
                    .Replace("`", "\\`");
            }
            else
            {
                preparedTextForJs = textToInject
                    .Replace("\\", "\\\\")
                    .Replace("'", "\\'")
                    .Replace("`", "\\`");
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
        if (style.display == 'none' || style.visibility == 'hidden' || style.opacity == '0' || elem.disabled) {{
            return `FAILURE: Input field (selector: {escapedSelectorForJs.Replace("`", "\\`")}) is not visible or is disabled.`;
        }}

        {(isChatGpt ? @"
        elem.style.setProperty('white-space', 'pre-wrap', 'important');
        elem.style.setProperty('line-height', '1.4', 'important');
        elem.style.setProperty('margin', '0', 'important');
        elem.style.setProperty('padding', '0', 'important');
        " : "")}

        elem.focus();

        if (elem.tagName.toLowerCase() == 'textarea' || elem.tagName.toLowerCase() == 'input') {{
            const currentValue = elem.value || '';
            elem.value = currentValue + (currentValue ? '\n' : '') + `{preparedTextForJs}`;
            elem.dispatchEvent(new Event('input', {{ bubbles: true, cancelable: true }}));
            return `SUCCESS: Text appended to {escapedSelectorForJs.Replace("`", "\\`")} (value set)`;
        }} else if (elem.isContentEditable) {{
            const currentContent = elem.innerHTML;
            elem.innerHTML = currentContent + '' + `{preparedTextForJs}`;
            const range = document.createRange();
            const sel = window.getSelection();
            range.selectNodeContents(elem);
            range.collapse(false);
            sel.removeAllRanges();
            sel.addRange(range);
            elem.dispatchEvent(new Event('input', {{ bubbles: true, cancelable: true }}));
            return `SUCCESS: Text appended to {escapedSelectorForJs.Replace("`", "\\`")} (contenteditable)`;
        }}
        return `FAILURE: Element (selector: {escapedSelectorForJs.Replace("`", "\\`")}) is neither a text input nor contenteditable.`;
    }} catch (e) {{
        return `FAILURE: JavaScript error: ` + e.message + ` (Selector: {escapedSelectorForJs.Replace("`", "\\`")})`;
    }}
}})();";

            Log($"Generated JavaScript: {script}");

            try
            {
                string result = await ChatWebView.CoreWebView2.ExecuteScriptAsync(script);
                result = result?.Trim('"');

                if (result == null || result == "null" || result.StartsWith("FAILURE:"))
                {
                    string failureMessage = result?.Replace("FAILURE: ", "") ?? "Unknown error during script execution.";
                    failureMessage = $"Failed to append {sourceOfText}: {failureMessage}";
                    MessageBox.Show(failureMessage, "Injection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    Log($"InjectTextIntoWebViewAsync: {result}");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error appending {sourceOfText} to WebView: {ex.Message}", "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Log(string message)
        {
            Debug.WriteLine($"LOG: {message}");
        }

        private class UrlOption
        {
            public string DisplayName { get; set; }
            public string Url { get; set; }
        }
    }
}