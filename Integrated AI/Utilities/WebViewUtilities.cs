using EnvDTE;
using EnvDTE80;
using Integrated_AI.Utilities;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text; // For StringBuilder
using System.Threading.Tasks;
using System.Web;
using System.Windows;
using System.Windows.Controls;

namespace Integrated_AI
{
    public static class WebViewUtilities
    {
        private static readonly Dictionary<string, string> _selectorMap = new Dictionary<string, string>
        {
            { "https://grok.com", "textarea[aria-label=\"Ask Grok anything\"]" },
            { "https://chatgpt.com", "div#prompt-textarea" },
            { "https://aistudio.google.com", "textarea[aria-label=\"Start typing a prompt\"]" }
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
                    if (e.IsSuccess)
                    {
                        // Execute test script when navigation is successful
                        //string testScript = "console.log('Navigation completed successfully!'); alert('Test script executed.');";
                        //webView.CoreWebView2.ExecuteScriptAsync(testScript);
                        // Inject clipboard monitoring script after successful navigation

                        string monitorScript = FileUtil.LoadScript("monitorClipboardWrite.js");
                        try
                        {
                            webView.CoreWebView2.ExecuteScriptAsync(monitorScript);
                            Log("Clipboard monitoring script injected after navigation.");
                        }
                        catch (Exception ex)
                        {
                            Log($"Failed to inject clipboard monitoring script: {ex.Message}");
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

            string textToInject = null;
            string sourceDescription = "";
            string type = "";
            int startLine = 1;
            int endLine = 1;

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
                startLine = textSelection.TopPoint.Line;
                endLine = textSelection.BottomPoint.Line;
                sourceDescription = $"---{relativePath} (partial code block)---\n{textToInject}\n---End code---\n\n";
            }
            else if (option == "File -> AI")
            {
                var text = DiffUtility.GetActiveDocumentText(dte);
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
                endLine = textDoc?.EndPoint.Line ?? 1;
                sourceDescription = $"---{relativePath} (whole file contents)---\n{textToInject}\n---End code---\n\n";
            }
            else if (option == "Function -> AI")
            {
                var functions = FunctionSelectionUtilities.GetFunctionsFromActiveDocument(dte);
                if (!functions.Any())
                {
                    MessageBox.Show("No functions found in the active document.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string recentFunctionsFilePath = Path.Combine(userDataFolder, "recent_functions.txt");
                var functionSelectionWindow = new FunctionSelectionWindow(functions, recentFunctionsFilePath, relativePath);
                if (functionSelectionWindow.ShowDialog() == true && functionSelectionWindow.SelectedFunction != null)
                {
                    textToInject = functionSelectionWindow.SelectedFunction.FullCode;
                    type = "function";
                    startLine = functionSelectionWindow.SelectedFunction.StartLine;
                    endLine = functionSelectionWindow.SelectedFunction.EndLine;
                    sourceDescription = $"---{relativePath} (function: {functionSelectionWindow.SelectedFunction.DisplayName})---\n{textToInject}\n---End code---\n\n";
                }
                else
                {
                    return;
                }
            }

            if (textToInject != null)
            {
                var context = new SentCodeContextManager.SentCodeContext
                {
                    Code = textToInject,
                    Type = type,
                    FilePath = filePath,
                    StartLine = startLine,
                    EndLine = endLine,
                    AgeCounter = 0
                };
                SentCodeContextManager.AddSentContext(context);
                await InjectTextIntoWebViewAsync(chatWebView, sourceDescription, option);
            }
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
                string scriptFileContent = FileUtil.LoadScript("retrieveText.js");

                string currentUrl = webView.Source?.ToString() ?? "";
                string selector = _selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value ?? "textarea";
                string escapedSelectorForJs = selector.Replace("'", "\\'"); // Escape for JS string

                string scriptToExecute = $"{scriptFileContent}\nretrieveTextFromElement('{escapedSelectorForJs}');";
                Log($"RetrieveText - Full scriptToExecute (first 500 chars of scriptFileContent):\n{scriptFileContent.Substring(0, Math.Min(scriptFileContent.Length, 500))}\nretrieveTextFromElement('{escapedSelectorForJs}');\n---END SCRIPT PREVIEW---");

                string result = await webView.ExecuteScriptAsync(scriptToExecute);
                Log($"Raw result from RetrieveText ExecuteScriptAsync: {(result == null ? "C# null" : $"\"{result}\"")}");
                result = result?.Trim('"');

                if (result == null || result == "null" || result.StartsWith("FAILURE:") || result.StartsWith("LoadScript ERROR:"))
                {
                    string failureMessage = result ?? "Unknown error during script execution.";
                    if (result == null) failureMessage = "ExecuteScriptAsync returned C# null (JS syntax error or severe issue).";
                    else if (result == "null") failureMessage = "Script returned JavaScript 'null' or 'undefined' (e.g., function not defined, or element not found by function). Check JS console in WebView DevTools.";
                    else if (result.StartsWith("FAILURE:")) failureMessage = result.Replace("FAILURE: ", "");
                    else if (result.StartsWith("LoadScript ERROR:")) failureMessage = "Problem loading script file: " + result.Replace("LoadScript ERROR: ", "");

                    Log($"Failed to retrieve text: {failureMessage}");
                    return null;
                }

                result = result.Replace("\\n", "\n"); // Unescape newlines if ExecuteScriptAsync JSON-encoded them
                Log("Text retrieved successfully from WebView.");
                return result;
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
                string scriptFileContent = FileUtil.LoadScript("injectText.js");

                string currentUrl = webView.Source?.ToString() ?? "";
                string selector = _selectorMap.FirstOrDefault(x => currentUrl.StartsWith(x.Key)).Value ?? "textarea";
                Log($"Using selector: {selector} for URL: {currentUrl} (InjectText)");

                string preparedTextForJs;
                bool isChatGpt = currentUrl.StartsWith("https://chatgpt.com");

                if (isChatGpt)
                {
                    // For ChatGPT (contentEditable div), textToInject is HTML.
                    // Ensure the HTML string itself is properly escaped for a JS string literal.
                    string escapedHtml = textToInject.Replace("\\", "\\\\") // Escape backslashes in original text first
                                                 .Replace("'", "\\'")   // Escape single quotes
                                                 .Replace("`", "\\`")   // Escape backticks
                                                 .Replace("\r", "")      // Remove \r
                                                 .Replace("\n", "\\n");  // Escape \n to \\n for JS string

                    var lines = escapedHtml.Split(new[] { "\\n" }, StringSplitOptions.None) // Split on the escaped newlines
                        .Select(line => string.IsNullOrEmpty(line.Trim()) ? "<span></span>" : $"<span>{WebUtility.HtmlEncode(line)}</span>"); // HtmlEncode each line content
                    preparedTextForJs = $"<div style=\\\"white-space: pre-wrap; line-height: 1.4;\\\">{string.Join("<br>", lines)}</div>";
                    // The above preparedTextForJs is already escaped for JS string.
                    // WebUtility.HtmlEncode is for the *content* of the spans, not the whole structure.
                    // The outer string preparedTextForJs needs to be a valid JS string passed to injectTextIntoElement.
                    // Let's re-evaluate how preparedTextForJs is built for ChatGPT more carefully.

                    // Simpler approach for ChatGPT HTML:
                    // 1. Build the desired HTML string.
                    // 2. Escape that HTML string to be a valid JS string literal.
                    var htmlLines = textToInject.Replace("\r\n", "\n").Replace("\r", "\n").Split('\n')
                        .Select(line => string.IsNullOrEmpty(line) ? "<span></span>" : $"<span>{WebUtility.HtmlEncode(line)}</span>");
                    string rawHtmlPayload = $"<div style=\"white-space: pre-wrap; line-height: 1.4;\">{string.Join("<br>", htmlLines)}</div>";

                    preparedTextForJs = rawHtmlPayload
                        .Replace("\\", "\\\\")
                        .Replace("'", "\\'")
                        .Replace("`", "\\`")
                        .Replace("\r", "")
                        .Replace("\n", "\\n"); // Escape newlines within the HTML structure for the JS string literal
                }
                else
                {
                    // For regular textareas/inputs, escape the plain text for JS string literal
                    preparedTextForJs = textToInject
                        .Replace("\\", "\\\\")  // Backslashes first
                        .Replace("'", "\\'")   // Single quotes
                        .Replace("`", "\\`")   // Backticks
                        .Replace("\r", "")      // Remove \r
                        .Replace("\n", "\\n");  // Escape \n to \\n for JS string
                }

                string escapedSelectorForJs = selector.Replace("'", "\\'");

                string scriptToExecute = $"{scriptFileContent}\ninjectTextIntoElement('{escapedSelectorForJs}', '{preparedTextForJs}', {isChatGpt.ToString().ToLowerInvariant()});";

                Log($"InjectText - Full scriptToExecute (first 500 chars of scriptFileContent):\n{scriptFileContent.Substring(0, Math.Min(scriptFileContent.Length, 500))}\ninjectTextIntoElement('{escapedSelectorForJs}', '/* preparedTextForJs (see next log for full) */', {isChatGpt.ToString().ToLowerInvariant()});\n---END SCRIPT PREVIEW---");
                Log($"InjectText - preparedTextForJs: {preparedTextForJs}"); // Log the potentially long string separately
                // For extremely long text, you might only log a portion of preparedTextForJs

                try
                {
                    string result = await webView.CoreWebView2.ExecuteScriptAsync(scriptToExecute);
                    Log($"Raw result from InjectText ExecuteScriptAsync: {(result == null ? "C# null" : $"\"{result}\"")}");
                    result = result?.Trim('"');

                    if (result == null || result == "null" || result.StartsWith("FAILURE:") || result.StartsWith("LoadScript ERROR:"))
                    {
                        string failureMessageDetail;
                        if (result == null)
                        {
                            failureMessageDetail = "ExecuteScriptAsync returned C# null. This often indicates a JavaScript syntax error in the generated script (check the full script logged above, especially 'preparedTextForJs') or a problem with the WebView. Check JS console in WebView DevTools.";
                        }
                        else if (result == "null")
                        {
                            failureMessageDetail = "JavaScript execution returned 'null' or 'undefined'. This commonly means the function (e.g., 'injectTextIntoElement') was not defined (check 'scriptFileContent' was loaded and is correct, and no JS syntax errors in the .js file itself), or the script logic failed silently. Check JS console in WebView DevTools.";
                        }
                        else if (result.StartsWith("FAILURE:"))
                        {
                            failureMessageDetail = result.Replace("FAILURE: ", "");
                        }
                        else if (result.StartsWith("LoadScript ERROR:"))
                        {
                            failureMessageDetail = "Problem loading script file: " + result.Replace("LoadScript ERROR: ", "");
                        }
                        else
                        {
                            failureMessageDetail = result; // Should not happen based on current checks
                        }

                        string fullErrorMessage = $"Failed to append {sourceOfText}: {failureMessageDetail}";
                        MessageBox.Show(fullErrorMessage, "Injection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                        Log(fullErrorMessage);
                    }
                    else
                    {
                        Log($"InjectTextIntoWebViewAsync: {result}");
                    }
                }
                catch (Exception ex)
                {
                    string errorMsg = $"Error executing JavaScript in WebView for '{sourceOfText}': {ex.Message}";
                    MessageBox.Show(errorMsg, "WebView Script Execution Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    Log(errorMsg + "\nStackTrace: " + ex.StackTrace);
                }
            }
            catch (Exception ex)
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
                string scriptToExecute = $"{scriptFileContent}\nretrieveSelectedText();";
                Log($"RetrieveSelectedText - Full scriptToExecute (first 500 chars of scriptFileContent):\n{scriptFileContent.Substring(0, Math.Min(scriptFileContent.Length, 500))}\nretrieveSelectedText();\n---END SCRIPT PREVIEW---");

                string result = await webView.ExecuteScriptAsync(scriptToExecute);
                Log($"Raw result from RetrieveSelectedText ExecuteScriptAsync: {(result == null ? "C# null" : $"\"{result}\"")}");

                result = result?.Trim('"');
                if (result == null || result == "null" || result.StartsWith("FAILURE:") || result.StartsWith("LoadScript ERROR:"))
                {
                    string failureMessage = result ?? "Unknown error during script execution.";
                    if (result == null) failureMessage = "ExecuteScriptAsync returned C# null (JS syntax error or severe issue).";
                    else if (result == "null") failureMessage = "No text selected in the chat window. Please highlight text and try again.";
                    else if (result.StartsWith("FAILURE:")) failureMessage = result.Replace("FAILURE: ", "");
                    else if (result.StartsWith("LoadScript ERROR:")) failureMessage = "Problem loading script file: " + result.Replace("LoadScript ERROR: ", "");

                    Log($"Failed to retrieve selected text: {failureMessage}");
                    MessageBox.Show(failureMessage, "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }

                // Unescape the JSON-encoded string
                result = StringUtilities.UnescapeJsonString(result);
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