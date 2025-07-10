using EnvDTE;
using EnvDTE80;
using HandyControl.Themes;
using Integrated_AI.Utilities; // Assuming FileUtil, DiffUtility, FunctionSelectionUtilities, SentCodeContextManager, StringUtilities are here
using Microsoft.VisualStudio.Shell;
// using Microsoft.VisualStudio.Shell.Interop; // Not directly used in the provided snippet, but likely in the project
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
// using System.Diagnostics; // Not directly used
using System.IO;
// using System.IO.Packaging; // Not directly used
using System.Linq;
using System.Net; // For WebUtility
// using System.Reflection; // Not directly used
// using System.Text; // For StringBuilder - Not directly used
using System.Threading.Tasks;
// using System.Web; // HttpUtility, WebUtility is in System.Net
using System.Windows;
using System.Windows.Controls; // For ComboBox (used in InitializeWebView2Async)
using static Integrated_AI.ChatWindow;
using MessageBox = HandyControl.Controls.MessageBox;

namespace Integrated_AI
{
    public static class WebViewUtilities
    {
        // Modified _selectorMap to support a list of selectors for each URL
        private static readonly Dictionary<string, List<string>> _selectorMap = new Dictionary<string, List<string>>
        {
            { "https://grok.com", new List<string> { "textarea[aria-label=\"Ask Grok anything\"]" } },
            { "https://chatgpt.com", new List<string> { "div#prompt-textarea" } },
            {
                "https://aistudio.google.com", new List<string>
                {
                    "textarea[aria-label=\"Start typing a prompt\"]",
                    "textarea[aria-label=\"Type something or tab to choose an example prompt\"]"
                }
            }
            // Add other URLs and their selectors here
        };

        // Helper method to get selectors for a given URL, finding the most specific match
        private static List<string> GetSelectorsForUrl(string url)
        {
            if (string.IsNullOrEmpty(url))
            {
                Log("URL is null or empty, using default selector 'textarea'.");
                return new List<string> { "textarea" };
            }

            string bestMatchKey = null;
            foreach (var key in _selectorMap.Keys)
            {
                // Use case-insensitive matching for URL prefixes
                if (url.StartsWith(key, StringComparison.OrdinalIgnoreCase))
                {
                    if (bestMatchKey == null || key.Length > bestMatchKey.Length)
                    {
                        bestMatchKey = key;
                    }
                }
            }

            if (bestMatchKey != null && _selectorMap.TryGetValue(bestMatchKey, out var selectors) && selectors != null && selectors.Any())
            {
                Log($"Found selectors for URL prefix '{bestMatchKey}': [{string.Join("], [", selectors)}]");
                return selectors;
            }

            Log($"No specific selectors found for URL '{url}'. Using default selector 'textarea'.");
            return new List<string> { "textarea" }; // Default selector list
        }


        public static async Task InitializeWebView2Async(WebView2 webView, string userDataFolder, List<ChatWindow.UrlOption> urlOptions, ComboBox urlSelector)
        {
            try
            {
                // The CoreWebView2InitializationCompleted event handler must be added before
                // the EnsureCoreWebView2Async method is called. This ensures that the event
                // is not missed if the initialization completes synchronously.
                webView.CoreWebView2InitializationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        // Initialization is successful, now we can configure the settings.
                        webView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                        webView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                        webView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                        // And perform the initial navigation based on the ComboBox's selected item.
                        if (urlSelector.SelectedItem is ChatWindow.UrlOption selectedOption && !string.IsNullOrEmpty(selectedOption.Url))
                        {
                            try
                            {
                                webView.Source = new Uri(selectedOption.Url);
                            }
                            catch (UriFormatException ex)
                            {
                                MessageBox.Show($"Invalid URL '{selectedOption.Url}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            }
                        }
                    }
                    else
                    {
                        MessageBox.Show($"WebView2 initialization failed: {e.InitializationException?.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                };

                // This handler can also be set up before initialization begins.
                webView.NavigationCompleted += (s, e) =>
                {
                    if (e.IsSuccess)
                    {
                        string monitorScript = FileUtil.LoadScript("monitorClipboardWrite.js");
                        if (monitorScript.StartsWith("LoadScript ERROR:"))
                        {
                            Log($"Failed to inject clipboard monitoring script after navigation: {monitorScript}");
                        }
                        else
                        {
                            try
                            {
                                webView.CoreWebView2.ExecuteScriptAsync(monitorScript);
                                Log("Clipboard monitoring script injected after navigation.");
                            }
                            catch (Exception ex)
                            {
                                Log($"Failed to inject clipboard monitoring script after navigation: {ex.Message}");
                            }
                        }
                    }
                    else
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

                // Now, create the environment and trigger the initialization.
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to initialize WebView2: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static async Task<bool> IsProgrammaticCopyAsync(WebView2 webView)
        {
            try
            {
                string result = await webView.CoreWebView2.ExecuteScriptAsync("localStorage.getItem('isProgrammaticCopy')");
                return result == "\"true\"";
            }
            catch (Exception ex)
            {
                Log($"Error checking programmatic copy flag: {ex.Message}");
                return false;
            }
        }


        public static async Task ExecuteCommandAsync(string option, DTE2 dte, WebView2 chatWebView, string userDataFolder)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
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

            string solutionPath = Path.GetDirectoryName(dte.Solution.FullName);
            string filePath = dte.ActiveDocument.FullName;
            string relativePath = FileUtil.GetRelativePath(solutionPath, filePath);

            string functionName = null;
            string textToInject = null;
            string sourceDescription = "";
            string type = "";

            if (option == "Code -> AI")
            {
                var textSelection = (TextSelection)dte.ActiveDocument.Selection;
                if (string.IsNullOrEmpty(textSelection?.Text))
                {
                    MessageBox.Show("No text selected in Visual Studio.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                textToInject = textSelection.Text;
                type = "snippet";
                sourceDescription = $"---{relativePath} (partial code block)---\n{textToInject}\n---End code---\n\n";
            }
            else if (option == "File -> AI")
            {
                var text = DiffUtility.GetDocumentText(dte.ActiveDocument);
                if (string.IsNullOrEmpty(text))
                {
                    MessageBox.Show("The active document is empty.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                string currentContent = await RetrieveTextFromWebViewAsync(chatWebView);
                if (currentContent != null && currentContent.Contains($"---{relativePath} (whole file contents)---"))
                {
                    MessageBox.Show("This file's contents have already been injected.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                textToInject = text;
                type = "file";
                var textDoc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                sourceDescription = $"---{relativePath} (whole file contents)---\n{textToInject}\n---End code---\n\n";
            }
            else if (option == "Method -> AI")
            {
                var functions = CodeSelectionUtilities.GetFunctionsFromDocument(dte.ActiveDocument);
                if (!functions.Any())
                {
                    MessageBox.Show("No functions found in the active document.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var functionSelectionWindow = new FunctionSelectionWindow(functions, FileUtil._recentFunctionsFilePath, relativePath);
                if (functionSelectionWindow.ShowDialog() == true && functionSelectionWindow.SelectedFunction != null)
                {
                    textToInject = functionSelectionWindow.SelectedFunction.FullCode;
                    type = "function";
                    functionName = functionSelectionWindow.SelectedFunction.DisplayName;
                    sourceDescription = $"---{relativePath} (function: {functionName})---\n{textToInject}\n---End code---\n\n";
                }
                else
                {
                    return;
                }
            }

            else if (option == "Error -> AI")
            {
                MessageBox.Show("Error to AI not done yet.");
            }

            else if (option == "Exception -> AI")
            {
                MessageBox.Show("Exception to AI not done yet.");
            }

            else if (option == "Method -> VS")
            {
                MessageBox.Show("Method to VS not done yet.");
            }

            else if (option == "File -> VS")
            {
                MessageBox.Show("File to VS not done yet.");
            }

            else if (option == "Code -> VS")
            {
                MessageBox.Show("Code to VS not done yet.");
            }

            if (textToInject != null)
            {
                await InjectTextIntoWebViewAsync(chatWebView, sourceDescription, option);
            }
        }

        // Executes a JavaScript script with multiple selectors, handling results and errors.
        // Parameters:
        // - webView: The WebView2 control to execute the script in.
        // - scriptFileName: The name of the script file to load (e.g., "retrieveText.js" or "injectText.js").
        // - currentUrl: The URL of the current page in the WebView.
        // - operationName: The name of the operation for logging (e.g., "RetrieveText" or "InjectText").
        // - jsFunctionName: The JavaScript function to call (e.g., "retrieveTextFromElement" or "injectTextIntoElement").
        // - preparedTextForJs: Optional text to inject (for injectTextIntoElement).
        // - isChatGpt: Indicates if the URL is for ChatGPT (for injectTextIntoElement).
        // Returns a tuple with the result (or null if failed) and the last error message.
        private static async Task<(string Result, string LastError)> ExecuteScriptWithSelectors(
            WebView2 webView,
            string scriptFileName,
            string currentUrl,
            string operationName,
            string jsFunctionName,
            bool stopOnSuccess = true,
            bool isChatGpt = false,
            string preparedTextForJs = null)
        {
            string scriptFileContent = FileUtil.LoadScript(scriptFileName);
            if (scriptFileContent.StartsWith("LoadScript ERROR:"))
            {
                string errorMessage = "Problem loading script file: " + scriptFileContent.Replace("LoadScript ERROR: ", "");
                Log($"Failed to perform {operationName}: {errorMessage}");
                return (null, errorMessage);
            }

            List<string> selectors = GetSelectorsForUrl(currentUrl);
            string lastError = $"No suitable selector found or all {operationName} attempts failed.";

            foreach (string selector in selectors)
            {
                string escapedSelectorForJs = selector.Replace("'", "\\'");
                // Construct the script based on the function name and parameters
                string scriptToExecute = jsFunctionName == "injectTextIntoElement"
                    ? $"{scriptFileContent}\n{jsFunctionName}('{escapedSelectorForJs}', '{preparedTextForJs}', {isChatGpt.ToString().ToLowerInvariant()});"
                    : $"{scriptFileContent}\n{jsFunctionName}('{escapedSelectorForJs}');";
                Log($"{operationName} - Attempting with selector: '{selector}'. Script (first 500 chars of file may be shown):\n{scriptFileContent.Substring(0, Math.Min(scriptFileContent.Length, 500))}\n{scriptToExecute.Split('\n').Last()}");

                try
                {
                    string result = await webView.CoreWebView2.ExecuteScriptAsync(scriptToExecute);
                    Log($"Raw result from {operationName} ExecuteScriptAsync for selector '{selector}': {(result == null ? "C# null" : $"\"{result}\"")}");
                    result = result?.Trim('"');

                    if (result == null) // C# null from ExecuteScriptAsync
                    {
                        lastError = $"ExecuteScriptAsync returned C# null for selector '{selector}'. This often indicates a JavaScript syntax error in the generated script or a problem with the WebView. Check JS console in WebView DevTools.";
                        Log($"{operationName} attempt failed: {lastError}");
                        continue; // Try next selector
                    }
                    if (result == "null") // JS 'null' or 'undefined'
                    {
                        lastError = $"Script returned JavaScript 'null' or 'undefined' for selector '{selector}'. This commonly means the function (e.g., '{jsFunctionName}') was not defined, or the element was not found and the function returned null. Check JS console in WebView DevTools.";
                        Log($"{operationName} attempt failed: {lastError}");
                        continue; // Try next selector
                    }
                    // Check for "FAILURE: Element not found" specifically to try next selector
                    if (result.StartsWith("FAILURE: Element not found", StringComparison.OrdinalIgnoreCase))
                    {
                        lastError = $"Element not found with selector '{selector}'.";
                        Log($"{operationName} attempt: {lastError}");
                        continue; // Try next selector
                    }
                    if (result.StartsWith("FAILURE:")) // Other FAILURE types
                    {
                        lastError = result.Replace("FAILURE: ", "");
                        Log($"{operationName} attempt for selector '{selector}' failed critically: {lastError}");
                        continue; // Continue to try other selectors
                    }

                    // Success for this selector
                    Log($"{operationName} succeeded using selector '{selector}'. Result: {result}");
                    if (stopOnSuccess)
                    {
                        return (result, null);
                    }
                }
                catch (Exception ex) // Exception during ExecuteScriptAsync for this selector
                {
                    lastError = $"Error executing JavaScript for selector '{selector}': {ex.Message}";
                    Log($"{operationName} attempt failed: {lastError}\nStackTrace: {ex.StackTrace}");
                    continue; // Try next selector
                }
            }

            // If loop completes, all selectors failed
            Log($"Failed to complete {operationName} after trying all selectors. Last error: {lastError}");
            return (null, lastError);
        }

        public static async Task<string> RetrieveTextFromWebViewAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
            {
                Log("Web view is not ready. Cannot retrieve text.");
                return null;
            }

            try
            {
                string currentUrl = webView.Source?.ToString() ?? "";
                var (result, lastError) = await ExecuteScriptWithSelectors(
                    webView,
                    "retrieveText.js",
                    currentUrl,
                    "RetrieveText",
                    "retrieveTextFromElement");

                if (result != null)
                {
                    // Using StringUtilities.UnescapeJsonString for robust unescaping of the JSON string result
                    result = StringUtil.UnescapeJsonString(result);
                    return result;
                }

                // If loop completes, all selectors failed
                MessageBox.Show($"Could not retrieve text from AI: {lastError}", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            catch (Exception ex)
            {
                Log($"Error in RetrieveTextFromWebViewAsync: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Could not retrieve text from AI: {ex.Message}", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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

            try
            {
                string currentUrl = webView.Source?.ToString() ?? "";
                // Case-insensitive check for chatgpt.com
                bool isChatGpt = currentUrl.StartsWith("https://chatgpt.com", StringComparison.OrdinalIgnoreCase);

                string preparedTextForJs;
                if (isChatGpt)
                {
                    var htmlLines = textToInject.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                        .Select(line => string.IsNullOrEmpty(line) ? "<span></span>" : $"<span>{WebUtility.HtmlEncode(line)}</span>");
                    string rawHtmlPayload = $"<div style=\"white-space: pre-wrap; line-height: 1.4;\">{string.Join("<br>", htmlLines)}</div>";
                    // Escape the HTML payload to be a valid JS string literal
                    preparedTextForJs = rawHtmlPayload.Replace("\\", "\\\\").Replace("'", "\\'").Replace("`", "\\`").Replace("\r", "").Replace("\n", "\\n");
                }
                else
                {
                    // Escape plain text for JS string literal
                    preparedTextForJs = textToInject.Replace("\\", "\\\\").Replace("'", "\\'").Replace("`", "\\`").Replace("\r", "").Replace("\n", "\\n");
                }

                var (result, lastError) = await ExecuteScriptWithSelectors(
                    webView,
                    "injectText.js",
                    currentUrl,
                    "InjectText",
                    "injectTextIntoElement",
                    true,
                    isChatGpt,
                    preparedTextForJs);

                if (result == null)
                {
                    string fullErrorMessage = $"Failed to append {sourceOfText}: {lastError}";
                    MessageBox.Show(fullErrorMessage, "Injection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    Log(fullErrorMessage);
                }
                else
                {
                    Log($"InjectTextIntoWebViewAsync: {result} (source: {sourceOfText})");
                }
            }
            catch (Exception ex) // General exception in preparation or other parts
            {
                string errorMsg = $"Failed to prepare for text injection ('{sourceOfText}'). Problem: {ex.Message}";
                MessageBox.Show(errorMsg, "Preparation Error", MessageBoxButton.OK, MessageBoxImage.Error);
                Log(errorMsg + "\nStackTrace: " + ex.StackTrace);
            }
        }

        public static async Task<string> RetrieveSelectedTextFromWebViewAsync(WebView2 webView)
        {
            if (webView?.CoreWebView2 == null)
            {
                Log("Web view is not ready. Cannot retrieve selected text.");
                return null;
            }

            try
            {
                string scriptFileContent = FileUtil.LoadScript("retrieveSelectedText.js");
                if (scriptFileContent.StartsWith("LoadScript ERROR:"))
                {
                    string errorMessage = "Problem loading script file: " + scriptFileContent.Replace("LoadScript ERROR: ", "");
                    Log($"Failed to retrieve selected text: {errorMessage}");
                    MessageBox.Show(errorMessage, "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }

                string scriptToExecute = $"{scriptFileContent}\nretrieveSelectedText();";
                Log($"RetrieveSelectedText - Full scriptToExecute (first 500 chars of scriptFileContent may be shown):\n{scriptFileContent.Substring(0, Math.Min(scriptFileContent.Length, 500))}\nretrieveSelectedText();\n---END SCRIPT PREVIEW---");

                string result = await webView.ExecuteScriptAsync(scriptToExecute);
                Log($"Raw result from RetrieveSelectedText ExecuteScriptAsync: {(result == null ? "C# null" : $"\"{result}\"")}");

                result = result?.Trim('"');
                // Check for various failure conditions
                if (result == null)
                {
                    Log("Failed to retrieve selected text: ExecuteScriptAsync returned C# null (JS syntax error or severe issue).");
                    MessageBox.Show("Could not retrieve selected text: Script execution failed.", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                if (result == "null") // JS 'null' or 'undefined' often means no text selected or function error
                {
                    Log("Failed to retrieve selected text: Script returned 'null'. This might mean no text is selected in the WebView, or the 'retrieveSelectedText' function had an issue.");
                    //MessageBox.Show("No text selected in the chat window, or unable to retrieve selection. Please highlight text and try again.", "Retrieval Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return null;
                }
                if (result.StartsWith("FAILURE:"))
                {
                    string failureMessage = result.Replace("FAILURE: ", "");
                    Log($"Failed to retrieve selected text: {failureMessage}");
                    MessageBox.Show($"Could not retrieve selected text: {failureMessage}", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                // LoadScript ERROR is already checked when loading scriptFileContent

                // Unescape the JSON-encoded string (handles \n, \t, \", \\, etc.)
                result = StringUtil.UnescapeJsonString(result);
                Log("Selected text retrieved successfully from WebView.");
                return result;
            }
            catch (Exception ex)
            {
                Log($"Error in RetrieveSelectedTextFromWebViewAsync: {ex.Message}\n{ex.StackTrace}");
                MessageBox.Show($"Could not retrieve selected text from AI: {ex.Message}", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        public static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"Integrated AI LOG: {message}");
        }
    }
}