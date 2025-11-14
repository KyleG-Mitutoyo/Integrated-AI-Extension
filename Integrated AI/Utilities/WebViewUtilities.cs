// Integrated AI
// Copyright (C) 2025 Kyle Grubbs

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any other later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using EnvDTE;
using EnvDTE80;
using HandyControl.Themes;
using Integrated_AI.Properties;
using Integrated_AI.Utilities;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net; // For WebUtility
using System.Text.Json; // For JSON parsing
using System.Threading.Tasks;
using System.Windows;
using static Integrated_AI.ChatWindow;
using MessageBox = HandyControl.Controls.MessageBox;
using WebView2 = Microsoft.Web.WebView2.Wpf.WebView2;

namespace Integrated_AI
{
    public static class WebViewUtilities
    {
        // Modified _selectorMap to support a list of selectors for each URL
        private static readonly Dictionary<string, List<string>> _selectorMap = new Dictionary<string, List<string>>
        {
            {
                "https://grok.com", new List<string> 
                { 
                    "textarea[aria-label=\"Ask Grok anything\"]",
                    "div[contenteditable=\"true\"]",
                    "div.ProseMirror",
                    "div.tiptap.ProseMirror",
                    "div[contenteditable=\"true\"].ProseMirror"
                } 
            },
            { 
                "https://chat.deepseek.com", new List<string> 
                { 
                    "textarea[placeholder=\"Message DeepSeek\"]", 
                    "textarea#chat-input" 
                } 
            },
            { 
                "https://chatgpt.com", new List<string> { "div#prompt-textarea" } 
            },
            { 
                "https://gemini.google.com/app", new List<string>
                {
                    "div.ql-editor", // Primary selector for the rich text editor
                    "div[contenteditable=\"true\"]" // Fallback selector
                }
            },
            {
                "https://aistudio.google.com", new List<string>
                {
                    "textarea[aria-label=\"Start typing a prompt\"]",
                    "textarea[aria-label=\"Type something or tab to choose an example prompt\"]"
                }
            },
            { 
                "https://claude.ai", new List<string>
                {
                    "textarea[aria-label=\"Type your prompt to Claude\"]",
                    "div[contenteditable=\"true\"]", // Primary, more stable selector
                    "p[data-placeholder*=\"Type your prompt to Claude\"]", // Good fallback for empty state
                    "div.ProseMirror" // Another possible stable selector
                } 
            }
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


        public static async Task InitializeWebView2Async(WebView2 webView, string userDataFolder)
        {
            try
            {
                // Its only jobs are to create the environment and ensure the CoreWebView2 object exists.
                var env = await CoreWebView2Environment.CreateAsync(null, userDataFolder);
                await webView.EnsureCoreWebView2Async(env);
            }
            catch (Exception ex)
            {
                // We will let the caller handle the message box to keep this a pure utility.
                Log($"FATAL: Failed to initialize WebView2 Environment/Core: {ex.Message}");
                // Rethrow the exception so the caller knows initialization failed.
                throw new InvalidOperationException("Failed to initialize WebView2. See inner exception for details.", ex);
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

                // UNIFIED LOGIC: Simplified script construction.
                // It now handles both inject (2 args) and retrieve (1 arg) cleanly.
                string scriptToExecute = jsFunctionName == "injectTextIntoElement"
                    ? $"{scriptFileContent}\n{jsFunctionName}('{escapedSelectorForJs}', '{preparedTextForJs}');"
                    : $"{scriptFileContent}\n{jsFunctionName}('{escapedSelectorForJs}');";

                Log($"{operationName} - Attempting with selector: '{selector}'.");

                try
                {
                    string result = await webView.CoreWebView2.ExecuteScriptAsync(scriptToExecute);
                    Log($"Raw result from {operationName} ExecuteScriptAsync for selector '{selector}': {(result == null ? "C# null" : $"\"{result}\"")}");
                    result = result?.Trim('"');

                    if (result == null)
                    {
                        lastError = $"ExecuteScriptAsync returned C# null for selector '{selector}'. This often indicates a JavaScript syntax error in the generated script or a problem with the WebView. Check JS console in WebView DevTools.";
                        Log($"{operationName} attempt failed: {lastError}");
                        continue;
                    }
                    if (result == "null")
                    {
                        lastError = $"Script returned JavaScript 'null' or 'undefined' for selector '{selector}'. This commonly means the function (e.g., '{jsFunctionName}') was not defined, or the element was not found and the function returned null. Check JS console in WebView DevTools.";
                        Log($"{operationName} attempt failed: {lastError}");
                        continue;
                    }
                    if (result.StartsWith("FAILURE: Element not found", StringComparison.OrdinalIgnoreCase))
                    {
                        lastError = $"Element not found with selector '{selector}'.";
                        Log($"{operationName} attempt: {lastError}");
                        continue;
                    }
                    if (result.StartsWith("FAILURE:"))
                    {
                        lastError = result.Replace("FAILURE: ", "");
                        Log($"{operationName} attempt for selector '{selector}' failed critically: {lastError}");
                        continue;
                    }

                    Log($"{operationName} succeeded using selector '{selector}'. Result: {result}");
                    if (stopOnSuccess)
                    {
                        return (result, null);
                    }
                }
                catch (Exception ex)
                {
                    lastError = $"Error executing JavaScript for selector '{selector}': {ex.Message}";
                    Log($"{operationName} attempt failed: {lastError}\nStackTrace: {ex.StackTrace}");
                    continue;
                }
            }

            Log($"Failed to complete {operationName} after trying all selectors. Last error: {lastError}");
            return (null, lastError);
        }

        public static async Task<string> RetrieveTextFromWebViewAsync(WebView2 webView, System.Windows.Window window)
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
                Log($"Failed to retrieve text from WebView for URL '{currentUrl}': {lastError}");
                ThemedMessageBox.Show(window, $"Could not retrieve text from AI: {lastError}", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
            catch (Exception ex)
            {
                Log($"Error in RetrieveTextFromWebViewAsync: {ex.Message}\n{ex.StackTrace}");
                ThemedMessageBox.Show(window, $"Could not retrieve text from AI: {ex.Message}", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }


        public static async Task InjectTextIntoWebViewAsync(WebView2 webView, System.Windows.Window window, string textToInject, string sourceOfText)
        {
            if (webView?.CoreWebView2 == null)
            {
                Log("Web view is not ready. Cannot inject text.");
                ThemedMessageBox.Show(window, "Web view is not ready. Please wait for the page to load.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            try
            {
                string currentUrl = webView.Source?.ToString() ?? "";

                // UNIFIED LOGIC: Always create a JSON payload of commands.
                // This is the most robust method for all modern text inputs.
                var lines = textToInject.Replace("\r\n", "\n").Split('\n');
                var commands = new List<string>();

                for (int i = 0; i < lines.Length; i++)
                {
                    var line = lines[i];

                    // Escape characters that would break a JSON string value.
                    string escapedLine = line.Replace("\\", "\\\\").Replace("\"", "\\\"");
                    commands.Add($"{{\"type\":\"text\",\"content\":\"{escapedLine}\"}}");

                    // If it's not the last line, add a line break command.
                    if (i < lines.Length - 1)
                    {
                        commands.Add("{\"type\":\"break\"}");
                    }
                }

                string jsonPayload = $"[{string.Join(",", commands)}]";
                string preparedTextForJs = jsonPayload.Replace("\\", "\\\\").Replace("'", "\\'");


                var (result, lastError) = await ExecuteScriptWithSelectors(
                    webView,
                    "injectText.js",
                    currentUrl,
                    "InjectText",
                    "injectTextIntoElement",
                    true,
                    preparedTextForJs);

                if (result == null)
                {
                    string fullErrorMessage = $"Failed to append {sourceOfText}: {lastError}";
                    Log(fullErrorMessage);
                    ThemedMessageBox.Show(window, fullErrorMessage, "Injection Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else
                {
                    Log($"InjectTextIntoWebViewAsync: {result} (source: {sourceOfText})");
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Failed to prepare for text injection ('{sourceOfText}'). Problem: {ex.Message}";
                Log(errorMsg + "\nStackTrace: " + ex.StackTrace);
                ThemedMessageBox.Show(window, errorMsg, "Preparation Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        public static async Task<string> RetrieveSelectedTextFromWebViewAsync(WebView2 webView, System.Windows.Window window)
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
                    ThemedMessageBox.Show(window, errorMessage, "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }

                string scriptToExecute = $"{scriptFileContent}\nretrieveSelectedText();";

                string result = await webView.ExecuteScriptAsync(scriptToExecute);
                Log($"Raw result from RetrieveSelectedText ExecuteScriptAsync: {(result == null ? "C# null" : $"\"{result}\"")}");

                result = result?.Trim('"');
                // Check for various failure conditions
                if (result == null)
                {
                    Log("Failed to retrieve selected text: ExecuteScriptAsync returned C# null (JS syntax error or severe issue).");
                    ThemedMessageBox.Show(window, "Could not retrieve selected text: Script execution failed.", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
                if (result == "null") // JS 'null' or 'undefined' often means no text selected or function error
                {
                    //Log("Failed to retrieve selected text: Script returned 'null'. This might mean no text is selected in the WebView, or the 'retrieveSelectedText' function had an issue.");
                    return null;
                }
                if (result.StartsWith("FAILURE:"))
                {
                    string failureMessage = result.Replace("FAILURE: ", "");
                    Log($"Failed to retrieve selected text: {failureMessage}");
                    ThemedMessageBox.Show(window, $"Could not retrieve selected text: {failureMessage}", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
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
                ThemedMessageBox.Show(window, $"Could not retrieve selected text from AI: {ex.Message}", "Retrieval Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }
        }

        public static string GetCurrentUrl(WebView2 webView)
        {
            return webView?.CoreWebView2?.Source?.ToString() ?? string.Empty;
        }

        public static void Log(string message)
        {
            System.Diagnostics.Debug.WriteLine($"INTEGRATED AI: {message}");

            // Use the new centralized logging service
            LoggingService.Log(message);

            // You can keep the status bar update if you still want it
            if (Settings.Default.showStatusLog) // Assuming you have a setting like this
            {
                IVsStatusbar statusBar = (IVsStatusbar)ServiceProvider.GlobalProvider.GetService(typeof(SVsStatusbar));
                statusBar.SetText($"INTEGRATED AI: {message}");
            }
        }
    }
}