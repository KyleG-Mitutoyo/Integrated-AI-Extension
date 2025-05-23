using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace Integrated_AI
{
    public static class ChatWindowUtilities
    {
        private static readonly Dictionary<string, string> _selectorMap = new Dictionary<string, string>
        {
            { "https://grok.com", "textarea[aria-label=\"Ask Grok anything\"]" },
            { "https://chatgpt.com", "div#prompt-textarea" },
            { "https://aistudio.google.com", "textarea[aria-label=\"Type something or tab to choose an example prompt\"]" }
        };

        public static async Task InitializeWebView2Async(WebView2 webView, string userDataFolder, List<ChatWindow.UrlOption> urlOptions, ComboBox urlSelector)
        {
            try
            {
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(env);

                webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                webView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess && urlOptions.Any())
                    {
                        urlSelector.SelectedIndex = 0; // Load first URL
                    }
                    else if (!e.IsSuccess)
                    {
                        MessageBox.Show($"WebView2 initialization failed: {e.InitializationException?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                webView.NavigationCompleted += (s, e) =>
                {
                    if (!e.IsSuccess)
                    {
                        string errorMessage = $"Failed to load {webView.Source?.ToString() ?? "page"}. Error status: {e.WebErrorStatus}.";
                        if (e.WebErrorStatus == CoreWebView2WebErrorStatus.CannotConnect ||
                            e.WebErrorStatus == CoreWebView2WebErrorStatus.HostNameNotResolved)
                        {
                            errorMessage += " Please check your internet connection and firewall settings.";
                        }
                        MessageBox.Show(errorMessage, "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    }
                };
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static async Task<string> RetrieveTextFromWebViewAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
            {
                Log("Web view is not ready. Cannot retrieve text.");
                return null;
            }

            string currentUrl = webView.Source?.ToString() ?? "";
            string selector = _selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value ?? "textarea";
            Log($"Using selector: {selector} for URL: {currentUrl}");

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
            text = elem.innerText || '';
        }} else {{
            return `FAILURE: Element (selector: {escapedSelectorForJs.Replace("`", "\\`")}) is neither a text input nor contenteditable.`;
        }}

        text = text.replace(/\\\\n/g, '\\n');
        return text;
    }} catch (e) {{
        return `FAILURE: JavaScript error: ` + e.message + ` (Selector: {escapedSelectorForJs.Replace("`", "\\`")})`;
    }}
}})();";

            try
            {
                string result = await webView.ExecuteScriptAsync(script);
                result = result?.Trim('"');

                if (result == null || result == "null" || result.StartsWith("FAILURE:"))
                {
                    string failureMessage = result?.Replace("FAILURE: ", "") ?? "Unknown error during script execution.";
                    Log($"Failed to retrieve text: {failureMessage}");
                    return null;
                }

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

        public static async Task InjectTextIntoWebViewAsync(WebView2 webView, string textToInject, string sourceOfText)
        {
            if (webView?.CoreWebView2 == null)
            {
                MessageBox.Show("Web view is not ready. Please wait for the page to load.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string currentUrl = webView.Source?.ToString() ?? "";
            string selector = _selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value ?? "textarea";
            Log($"Using selector: {selector} for URL: {currentUrl}");

            textToInject = textToInject.Replace("\r\n", "\n").Replace("\r", "\n");

            string preparedTextForJs;
            bool isChatGpt = currentUrl.StartsWith("https://chatgpt.com");
            if (isChatGpt)
            {
                string escapedText = textToInject.Replace("\\", "\\\\");
                var lines = escapedText.Split(new[] { '\n' }, StringSplitOptions.None)
                    .Select(line => string.IsNullOrEmpty(line) ? "<span></span>" : $"<span>{WebUtility.HtmlEncode(line)}</span>");
                preparedTextForJs = $"<div style=\"white-space: pre-wrap; line-height: 1.4;\">{string.Join("<br>", lines)}</div>";
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
                string result = await webView.CoreWebView2.ExecuteScriptAsync(script);
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

        public static void Log(string message)
        {
            Debug.WriteLine($"LOG: {message}");
        }
    }
}