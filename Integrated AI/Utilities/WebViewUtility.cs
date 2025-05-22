using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public class WebViewUtility
    {
        private readonly WebView2 _webView;
        private readonly string _userDataFolder;
        private readonly List<Models.UrlOption> _urlOptions;
        private bool _isNavigationComplete;

        public WebViewUtility(WebView2 webView, string userDataFolder, List<Models.UrlOption> urlOptions)
        {
            _webView = webView ?? throw new ArgumentNullException(nameof(webView));
            _userDataFolder = userDataFolder ?? throw new ArgumentNullException(nameof(userDataFolder));
            _urlOptions = urlOptions ?? throw new ArgumentNullException(nameof(urlOptions));
            _isNavigationComplete = false;
        }

        public async Task InitializeAsync()
        {
            try
            {
                Directory.CreateDirectory(_userDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, _userDataFolder);
                await _webView.EnsureCoreWebView2Async(env);

                _webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                _webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _webView.CoreWebView2.Settings.IsStatusBarEnabled = false;
                LoggingUtility.Log("WebView2 initialized successfully.");
            }
            catch (Exception ex)
            {
                LoggingUtility.Log($"Failed to initialize WebView2: {ex.Message}");
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void HandleInitializationCompleted(object sender, CoreWebView2InitializationCompletedEventArgs e)
        {
            if (e.IsSuccess && _urlOptions.Any())
            {
                LoggingUtility.Log("WebView2 initialization completed successfully.");
            }
            else if (!e.IsSuccess)
            {
                LoggingUtility.Log($"WebView2 initialization failed: {e.InitializationException?.Message}");
                MessageBox.Show($"WebView2 initialization failed: {e.InitializationException?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public void HandleNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _isNavigationComplete = e.IsSuccess;
            if (!e.IsSuccess)
            {
                string errorMessage = $"Failed to load {_webView.Source?.ToString() ?? "page"}. Error status: {e.WebErrorStatus}.";
                if (e.WebErrorStatus == CoreWebView2WebErrorStatus.CannotConnect ||
                    e.WebErrorStatus == CoreWebView2WebErrorStatus.HostNameNotResolved)
                {
                    errorMessage += " Please check your internet connection and firewall settings.";
                }
                LoggingUtility.Log(errorMessage);
                MessageBox.Show(errorMessage, "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
            else
            {
                LoggingUtility.Log($"Navigation completed for {_webView.Source?.ToString()}");
            }
        }

        public async Task<string> RetrieveTextAsync(Action<string> log)
        {
            if (_webView?.CoreWebView2 == null || !_isNavigationComplete)
            {
                log("Web view is not ready or navigation is incomplete. Cannot retrieve text.");
                return null;
            }

            string currentUrl = _webView.Source?.ToString() ?? "";
            var selectorMap = new Dictionary<string, string>
            {
                { "https://grok.com", "textarea[aria-label=\"Ask Grok anything\"]" },
                { "https://chatgpt.com", "div#prompt-textarea" },
                { "https://aistudio.google.com", "textarea[aria-label=\"Type something or tab to choose an example prompt\"]" }
            };

            string selector = selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value ?? "textarea";
            log($"Using selector: {selector} for URL: {currentUrl}");

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

            log($"Executing RetrieveText script for selector: {selector}");

            try
            {
                string result = await _webView.ExecuteScriptAsync(script);
                log($"RetrieveText script result: {result ?? "null"}");
                result = result?.Trim('"');

                if (result == null || result == "null" || result.StartsWith("FAILURE:"))
                {
                    string failureMessage = result?.Replace("FAILURE: ", "") ?? "Unknown error during script execution.";
                    log($"Failed to retrieve text: {failureMessage}");
                    return null;
                }

                result = result.Replace("\\n", "\n");
                log("Text retrieved successfully from WebView.");
                return result;
            }
            catch (Exception ex)
            {
                log($"Error retrieving text from WebView: {ex.Message}");
                return null;
            }
        }

        public async Task InjectTextAsync(string textToInject, string sourceOfText, Action<string> log)
        {
            if (_webView?.CoreWebView2 == null || !_isNavigationComplete)
            {
                log("Web view is not ready or navigation is incomplete. Cannot inject text.");
                MessageBox.Show("Web view is not ready or page is still loading. Please wait.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string currentUrl = _webView.Source?.ToString() ?? "";
            log($"Attempting to inject text into URL: {currentUrl}");

            var selectorMap = new Dictionary<string, string>
    {
        { "https://grok.com", "textarea[aria-label=\"Ask Grok anything\"]" },
        { "https://chatgpt.com", "div#prompt-textarea" },
        { "https://aistudio.google.com", "textarea[aria-label=\"Type something or tab to choose an example prompt\"]" }
    };

            string selector = selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value ?? "textarea";
            log($"Using selector: {selector} for URL: {currentUrl}");

            // Test selector before injection
            if (!await TestSelectorAsync(selector, log))
            {
                log($"Selector '{selector}' not found in the current page.");
                MessageBox.Show($"Cannot find input field for {currentUrl}. Please ensure the page is loaded and supports text input.", "Injection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Use sourceOfText instead of textToInject
            string preparedTextForJs;
            bool isChatGpt = currentUrl.StartsWith("https://chatgpt.com");
            if (isChatGpt)
            {
                string escapedText = sourceOfText.Replace("\\", "\\\\");
                var lines = escapedText.Split(new[] { '\n' }, StringSplitOptions.None)
                    .Select(line => string.IsNullOrEmpty(line) ? "<span></span>" : $"<span>{WebUtility.HtmlEncode(line)}</span>");
                preparedTextForJs = $"<div style=\"white-space: pre-wrap; line-height: 1.4;\">{string.Join("<br>", lines)}</div>";
                preparedTextForJs = preparedTextForJs.Replace("'", "\\'").Replace("`", "\\`");
            }
            else
            {
                preparedTextForJs = sourceOfText.Replace("\\", "\\\\").Replace("'", "\\'").Replace("`", "\\`");
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

            log($"Executing InjectText script: {script}");

            try
            {
                string result = await _webView.CoreWebView2.ExecuteScriptAsync(script);
                log($"InjectText script result: {result ?? "null"}");
                result = result?.Trim('"');

                if (result == null || result == "null" || result.StartsWith("FAILURE:"))
                {
                    string failureMessage = result?.Replace("FAILURE: ", "") ?? "Unknown error during script execution.";
                    failureMessage = $"Failed to append {sourceOfText}: {failureMessage}";
                    log(failureMessage);
                    MessageBox.Show(failureMessage, "Injection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    log($"InjectTextIntoWebViewAsync: {result}");
                }
            }
            catch (Exception ex)
            {
                log($"Error executing injection script: {ex.Message}");
                MessageBox.Show($"Error appending {sourceOfText} to WebView: {ex.Message}", "Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task<bool> TestSelectorAsync(string selector, Action<string> log)
        {
            string testScript = $@"
(function() {{
    try {{
        const elem = document.querySelector('{selector.Replace("'", "\\'")}');
        return elem != null;
    }} catch (e) {{
        return false;
    }}
}})();";

            try
            {
                string result = await _webView.ExecuteScriptAsync(testScript);
                log($"Selector test result for '{selector}': {result}");
                return result == "true";
            }
            catch (Exception ex)
            {
                log($"Error testing selector '{selector}': {ex.Message}");
                return false;
            }
        }
    }
}