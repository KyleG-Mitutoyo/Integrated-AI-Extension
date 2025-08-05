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
using HandyControl.Controls;
using HandyControl.Themes;
using HandyControl.Tools.Extension;
using Integrated_AI.Properties;
using Integrated_AI.Utilities;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Contexts;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Configuration;
using System.Windows.Documents;
using System.Windows.Forms.VisualStyles;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static Integrated_AI.RestoreSelectionWindow;
using static Integrated_AI.Utilities.DiffUtility;
using static Integrated_AI.WebViewUtilities;
using Configuration = System.Configuration;
using Window = System.Windows.Window;
using HcMessageBox = HandyControl.Controls.MessageBox;


namespace Integrated_AI
{
    public partial class ChatWindow : UserControl
    {
        public class UrlOption
        {
            public string DisplayName { get; set; }
            public string Url { get; set; }
            public string DefaultUrl { get; set; }
        }

        // Existing fields...
        public List<UrlOption> _urlOptions = new List<UrlOption>
        {
            new UrlOption { DisplayName = "Grok", Url = "https://grok.com", DefaultUrl = "https://grok.com" },
            new UrlOption { DisplayName = "Google AI Studio", Url = "https://aistudio.google.com", DefaultUrl = "https://aistudio.google.com"},
            new UrlOption { DisplayName = "Gemini", Url = "https://gemini.google.com/app", DefaultUrl = "https://gemini.google.com/app"},
            new UrlOption { DisplayName = "ChatGPT", Url = "https://chatgpt.com", DefaultUrl = "https://chatgpt.com" },
            new UrlOption { DisplayName = "Claude", Url = "https://claude.ai" , DefaultUrl = "https://claude.ai"}
        };

        private readonly string _webViewDataFolder;
        private readonly string _appDataFolder;
        private readonly string _backupsFolder;
        private string _selectedOptionToAI = "Function -> AI";
        private string _selectedOptionToVS = "Function -> VS";
        private bool _executeCommandOnClick = true;
        private readonly DTE2 _dte;
        private DiffUtility.DiffContext _diffContext;
        //Used to store the compare muliple diff views
        private List<DiffContext> _diffContextsCompare;
        public BackupItem _selectedBackup;
        private DocumentEvents _documentEvents;
        private bool _isWebViewInitialized = false;
        private bool _isThemeInitialized = false;


        public ChatWindow()
        {
            InitializeComponent();

            var dummy = typeof(HandyControl.Controls.Window);
            InitializeUrlSelector();
            _dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            _diffContext = null;

            // Define base folder for the extension
            var baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "IntegratedAIExtension",
                Environment.UserName);
            Directory.CreateDirectory(baseFolder);

            // Define separate folders for WebView2, app data, and backups
            _webViewDataFolder = Path.Combine(baseFolder, "WebViewData");
            _appDataFolder = Path.Combine(baseFolder, "Data");
            _backupsFolder = Path.Combine(baseFolder, "Backups");
            Directory.CreateDirectory(_webViewDataFolder);
            Directory.CreateDirectory(_appDataFolder);
            Directory.CreateDirectory(_backupsFolder);

            FileUtil._recentFunctionsFilePath = Path.Combine(_appDataFolder, "recent_functions.txt");

            // Register for window messages when the control is loaded
            Loaded += ChatWindow_Loaded;
            Unloaded += ChatWindow_Unloaded;

            PopupConfig.Closed += PopupConfig_Closed;
            this.Loaded += ChatWindow_ThemeLoader;
        }

        #region Helper Methods

        /// <summary>
        /// Shows a HandyControl MessageBox that is correctly themed by associating it with the parent window.
        /// </summary>
        private MessageBoxResult ShowThemedMessageBox(
                    string message,
                    string caption,
                    MessageBoxButton button = MessageBoxButton.OK,
                    MessageBoxImage icon = MessageBoxImage.None)
        {
            // The types here are the standard System.Windows enums, which is correct.
            var owner = Window.GetWindow(this);

            // Call our new, bulletproof helper.
            return ThemedMessageBox.Show(owner, message, caption, button, icon);
        }

        private void UpdateTheme(ApplicationTheme newTheme)
        {
            var dictionaries = this.Resources.MergedDictionaries;

            // Find and remove the old color theme dictionary if it exists.
            var oldThemeDictionary = dictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.ToString().Contains("/Colors/"));

            if (oldThemeDictionary != null)
            {
                dictionaries.Remove(oldThemeDictionary);
            }

            // Define the URI for the new color theme.
            string newThemeSource = (newTheme == ApplicationTheme.Dark)
                ? "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Dark.xaml"
                : "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Light.xaml";

            // Add the new color theme dictionary.
            dictionaries.Add(new ResourceDictionary { Source = new Uri(newThemeSource) });
        }

        private void InitializeUrlSelector()
        {
            // Populate ComboBox with URL options
            UrlSelector.ItemsSource = _urlOptions;

            var savedUrl = Settings.Default.selectedChatUrl;
            UrlOption optionToSelect = null;

            // Try to find an option that corresponds to the saved URL.
            if (!string.IsNullOrEmpty(savedUrl))
            {
                string name = GetChatNameFromUrl(savedUrl);
                if (!string.IsNullOrEmpty(name))
                {
                    // Find the option in our list by the name we derived.
                    optionToSelect = _urlOptions.FirstOrDefault(o => o.DisplayName == name);
                }
            }

            if (optionToSelect != null)
            {
                optionToSelect.Url = savedUrl;
                WebViewUtilities.Log($"InitializeUrlSelector: Restored URL for {optionToSelect.DisplayName} to {optionToSelect.Url}");
            }
            else
            {
                // This is the fallback for a fresh install, corrupted setting, or obsolete URL.
                // Default to a known-good provider.
                optionToSelect = _urlOptions.FirstOrDefault(o => o.DisplayName == "Google AI Studio")
                               ?? _urlOptions.FirstOrDefault(); // Fallback to the first if default is missing.

                if (optionToSelect != null)
                {
                    optionToSelect.Url = optionToSelect.DefaultUrl;

                    WebViewUtilities.Log($"InitializeUrlSelector: No valid saved URL found. Defaulting to {optionToSelect.DisplayName} at {optionToSelect.Url}.");
                }
                else
                {
                    // This should never happen since _urlOptions is hardcoded.
                    WebViewUtilities.Log("InitializeUrlSelector: CRITICAL - No URL options available to select.");
                }
            }

            // This ensures that SelectedItem is always set, preventing a blank ComboBox.
            UrlSelector.SelectedItem = optionToSelect;
        }

        private string GetUrlSelectorText()
        {
            if (UrlSelector.SelectedItem is UrlOption selectedOption)
            {
                return selectedOption.DisplayName; // Returns the display text
            }
            return string.Empty; // Return empty string if nothing is selected
        }

        private string GetChatNameFromUrl(string url)
        {
            // Handle null or empty URL input gracefully.
            if (string.IsNullOrEmpty(url))
            {
                return null;
            }

            // ALWAYS match against the DefaultUrl, which is the stable base URL for the service.
            // The Url property is dynamic and stores the last-visited page, so it should not be used for matching.
            var bestMatch = _urlOptions
                .Where(option => !string.IsNullOrEmpty(option.DefaultUrl) && url.StartsWith(option.DefaultUrl, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(option => option.DefaultUrl.Length) // Also order by the length of the URL we matched on.
                .FirstOrDefault();

            //WebViewUtilities.Log($"GetChatNameFromUrl: Matched URL '{url}' to chat provider '{bestMatch?.DisplayName}'.");
            return bestMatch?.DisplayName;
        }


        private async Task ToVSButton_ClickLogicAsync(string aiCode, string pasteType = null, string functionName = null)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Check if a diff window is already open. The new _isProcessingClipboardAction handles the race condition.
            if (_diffContext != null)
            {
                Log("PasteButton_ClickLogic: Diff window already open. Aborting.");
                return;
            }

            if (_dte == null)
            {
                Log("PasteButton_ClickLogic: DTE service not available.");
                // *** CHANGE 3: Use the themed MessageBox helper ***
                ShowThemedMessageBox("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(aiCode))
            {
                Log("PasteButton_ClickLogic: No AI code retrieved from WebView or clipboard.");
                ShowThemedMessageBox("No code retrieved from clipboard.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var activeDocument = _dte.ActiveDocument;
            if (activeDocument == null)
            {
                Log("PasteButton_ClickLogic: No active document found.");
                ShowThemedMessageBox("No active document.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string currentCode = DiffUtility.GetDocumentText(activeDocument);
            string modifiedCode = currentCode;
            ChooseCodeWindow.ReplacementItem selectedItem = null;
            if (pasteType != null || functionName != null)
            {
                selectedItem = new ChooseCodeWindow.ReplacementItem();
                selectedItem.Type = pasteType;
                selectedItem.DisplayName = functionName;
            }

            _diffContext = new DiffUtility.DiffContext { };
            modifiedCode = await StringUtil.CreateDocumentContent(_dte, Window.GetWindow(this), modifiedCode, aiCode, activeDocument, selectedItem, _diffContext);
            _diffContext = DiffUtility.OpenDiffView(Window.GetWindow(this), activeDocument, currentCode, modifiedCode, aiCode, _diffContext);

            if (_diffContext == null)
            {
                return;
            }

            // If diff view opened successfully, the lock remains active.
            // Update UI on the main thread.
            Dispatcher.Invoke(() =>
            {
                UpdateButtonsForDiffView(true);
            });
        }

        private async void ExecuteToVSCommand()
        {
            try
            {
                if (_dte == null)
                {
                    Log("ExecuteToVSCommand: DTE service not available.");
                    ShowThemedMessageBox("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                //Get AI code either from selected text or the clipboard if no text is selected
                string aiCode = await WebViewUtilities.RetrieveSelectedTextFromWebViewAsync(ChatWebView, Window.GetWindow(this));


                if (aiCode == null || aiCode == "null" || string.IsNullOrEmpty(aiCode))
                {
                    aiCode = Clipboard.GetText(); // Fallback to clipboard text if WebView retrieval fails
                                                  //Optional: check again if aicode is null or empty after clipboard retrieval
                    WebViewUtilities.Log("ExecuteToVSCommand: Retrieved code from clipboard as WebView retrieval failed.");
                }
                else
                {
                    // Only remove base indentation if the AI code isn't from the clipboard
                    aiCode = StringUtil.RemoveBaseIndentation(aiCode);
                }

                if (_selectedOptionToVS == "Function -> VS")
                {
                    // First need to find the function name from the AI code
                    string functionName = null;
                    string functionType = "function";
                    string solutionPath = Path.GetDirectoryName(_dte.Solution.FullName);
                    string filePath = _dte.ActiveDocument.FullName;
                    string relativePath = FileUtil.GetRelativePath(solutionPath, filePath);

                    var functions = CodeSelectionUtilities.GetFunctionsFromDocument(_dte.ActiveDocument);
                    // Finding no functions means we treat it as a new function
                    if (!functions.Any())
                    {
                        Log("SplitButtonToVS_Click: No functions found in the active document, treating as new function");
                        await ToVSButton_ClickLogicAsync(aiCode, "new_function");
                        return;
                    }

                    // If there are functions, show the selection window or auto match
                    if (AutoFunctionMatch.IsChecked == true)
                    {
                        var (isFunction, analyzedFunctionName, isFullFile) = StringUtil.AnalyzeCodeBlock(_dte, _dte.ActiveDocument, aiCode);
                        if (isFunction) functionName = analyzedFunctionName;
                    }

                    else
                    {
                        var functionSelectionWindow = new FunctionSelectionWindow(functions, FileUtil._recentFunctionsFilePath, relativePath, true);
                        if (functionSelectionWindow.ShowDialog() == true && functionSelectionWindow.SelectedFunction != null)
                        {
                            functionType = functionSelectionWindow.SelectedFunction.DisplayName == "New Function" ? "new_function" : "function";
                            functionName = functionSelectionWindow.SelectedFunction.DisplayName;
                        }
                        else
                        {
                            return;
                        }
                    }

                    await ToVSButton_ClickLogicAsync(aiCode, functionType, functionName);
                }

                else if (_selectedOptionToVS == "File -> VS")
                {
                    await ToVSButton_ClickLogicAsync(aiCode, pasteType: "file");
                }

                else if (_selectedOptionToVS == "Snippet -> VS")
                {
                    await ToVSButton_ClickLogicAsync(aiCode, pasteType: "snippet");
                }

                else if (_selectedOptionToVS == "New File")
                {
                    await ToVSButton_ClickLogicAsync(aiCode, pasteType: "new_file");
                }
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"ExecuteToVSCommand: Error executing {_selectedOptionToVS} - {ex.Message}");
                ShowThemedMessageBox($"Error executing {_selectedOptionToVS}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        private void UpdateButtonsForDiffView(bool showDiffButons)
        {
            if (showDiffButons)
            {
                VSToAISplitButton.Visibility = Visibility.Collapsed;
                AIToVSSplitButton.Visibility = Visibility.Collapsed;
                RestoreButton.Visibility = Visibility.Collapsed;
                SaveBackupButton.Visibility = Visibility.Collapsed;
                AcceptButton.Visibility = Visibility.Visible;
                ChooseButton.Visibility = Visibility.Visible;
                DeclineButton.Visibility = Visibility.Visible;
            }

            else
            {
                VSToAISplitButton.Visibility = Visibility.Visible;
                AIToVSSplitButton.Visibility = Visibility.Visible;
                RestoreButton.Visibility = Visibility.Visible;
                SaveBackupButton.Visibility = Visibility.Visible;
                AcceptButton.Visibility = Visibility.Collapsed;
                ChooseButton.Visibility = Visibility.Collapsed;
                DeclineButton.Visibility = Visibility.Collapsed;
            }
        }

        private void CloseDiffButtonLogic()
        {
            if (_diffContextsCompare == null)
            {
                CloseDiffsButton.Visibility = Visibility.Collapsed; // Hide the button if no diffs are open
                UseRestoreButton.Visibility = Visibility.Collapsed;
                return;
            }
            // To avoid issues with modifying the collection while iterating, create a copy
            var contextsToClose = new List<DiffContext>(_diffContextsCompare);
            foreach (var diffContext in contextsToClose)
            {
                DiffUtility.CloseDiffAndReset(diffContext);
            }
            _diffContextsCompare.Clear();
            CloseDiffsButton.Visibility = Visibility.Collapsed; // Hide the button after closing diffs
            UseRestoreButton.Visibility = Visibility.Collapsed;
        }

        #endregion

        #region UI interactions

        private void UrlSelector_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // Do not allow navigation until our async initialization is 100% complete.
            if (!_isWebViewInitialized || ChatWebView?.CoreWebView2 == null)
            {
                return;
            }

            if (UrlSelector.SelectedItem is UrlOption selectedOption && !string.IsNullOrEmpty(selectedOption.Url))
            {
                try
                {
                    // First update the old selected option URL with the surrent URL before navigating, to save the state for later.
                    string currentUrl = WebViewUtilities.GetCurrentUrl(ChatWebView);

                    foreach (UrlOption option in _urlOptions)
                    {
                        // Check if the display name matches the selected option
                        if (option.DisplayName == GetChatNameFromUrl(currentUrl))
                        {
                            option.Url = currentUrl;
                            WebViewUtilities.Log($"UrlSelector_SelectionChanged: Updated URL for {option.DisplayName} to {option.Url}");
                        }
                    }

                    ChatWebView.Source = new Uri(selectedOption.Url);
                }
                catch (UriFormatException ex)
                {
                    Log($"Invalid URL '{selectedOption.Url}': {ex.Message}");
                    // Optional: Fallback to DefaultUrl if the cached Url is somehow invalid.
                    ChatWebView.Source = new Uri(selectedOption.DefaultUrl);
                }
            }
        }

        private async void SplitButtonToAI_Click(object sender, RoutedEventArgs e)
        {
            if (_executeCommandOnClick)
            {
                await WebViewUtilities.ExecuteCommandAsync(_selectedOptionToAI, _dte, Window.GetWindow(this), ChatWebView, _webViewDataFolder);
            }
            _executeCommandOnClick = true;
        }

        private async void SplitButtonToVS_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_executeCommandOnClick) ExecuteToVSCommand();
            }

            catch (Exception ex)
            {
                WebViewUtilities.Log($"SplitButtonToVS_Click: Error executing {_selectedOptionToVS} - {ex.Message}");
                ShowThemedMessageBox($"Error executing {_selectedOptionToVS}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            finally
            {
                _executeCommandOnClick = true;
            }
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

        private async void MenuItemToAI_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string option)
            {
                _selectedOptionToAI = option;
                VSToAISplitButton.Content = option;
                await WebViewUtilities.ExecuteCommandAsync(option, _dte, Window.GetWindow(this), ChatWebView, _webViewDataFolder);
            }
        }

        private async void MenuItemToVS_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string option)
            {
                _selectedOptionToVS = option;
                AIToVSSplitButton.Content = option;

                ExecuteToVSCommand();
            }
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            try
            {
                string aiCodeFullFile = null;
                var contextToClose = _diffContext;
                var documentToModify = _dte.Documents.Item(contextToClose.ActiveDocumentPath);

                // Capture the cursor's position BEFORE the document is modified.
                int originalLine = -1;
                int originalColumn = -1;
                if (documentToModify != null)
                {
                    try
                    {
                        var textDocument = (TextDocument)documentToModify.Object("TextDocument");
                        var selection = textDocument.Selection;
                        originalLine = selection.ActivePoint.Line;
                        originalColumn = selection.ActivePoint.LineCharOffset;
                    }
                    catch (Exception ex)
                    {
                        WebViewUtilities.Log($"Error getting original cursor position: {ex.Message}");
                    }
                }

                if (contextToClose?.TempAiFile != null && File.Exists(contextToClose.TempAiFile))
                {
                    aiCodeFullFile = FileUtil.GetAICode(Window.GetWindow(this), contextToClose.TempAiFile);
                    if (string.IsNullOrEmpty(aiCodeFullFile))
                    {
                        WebViewUtilities.Log("AcceptButton_Click: AI code is empty.");
                    }
                }
                else
                {
                    WebViewUtilities.Log("AcceptButton_Click: No valid diff context or temp file.");
                }


                if (contextToClose != null)
                {
                    DiffUtility.CloseDiffAndReset(contextToClose);
                    _diffContext = null;
                }

                // Make sure to use the ActiveDocument from the diffcontext rather than the current active document!
                if (aiCodeFullFile != null && _dte != null)
                {
                    if (contextToClose.ActiveDocumentPath != null)
                    {
                        DiffUtility.ApplyChanges(_dte, Window.GetWindow(this), documentToModify, aiCodeFullFile);
                    }
                }

                // A backup of the solution files are created for every accept button click, if enabled.
                if (AutoRestore.IsChecked == true)
                {
                    // If we want to add the code extension to get syntax highlghting in the restore window
                    // we'd use documentToModify file extension
                    string currentUrl = WebViewUtilities.GetCurrentUrl(ChatWebView);
                    BackupUtilities.CreateSolutionBackup(_dte, Window.GetWindow(this), _backupsFolder, contextToClose.AICodeBlock, GetUrlSelectorText(), currentUrl);
                }

                // Save the document after making the backup
                documentToModify?.Save();
                WebViewUtilities.Log($"ApplyChanges: Successfully saved '{contextToClose.ActiveDocumentPath}'.");

                // Scroll to the original cursor position
                if (documentToModify != null && originalLine > 0 && originalColumn > 0)
                {
                    try
                    {
                        var textDocument = (TextDocument)documentToModify.Object("TextDocument");
                        var selection = textDocument.Selection;

                        // Move the cursor back to the original position
                        selection.MoveTo(originalLine, originalColumn);

                        // Center the view on the cursor
                        selection.ActivePoint.TryToShow(vsPaneShowHow.vsPaneShowCentered, null);
                        WebViewUtilities.Log($"Scrolled to original cursor position at line {originalLine}, column {originalColumn}.");
                    }
                    catch (Exception ex)
                    {
                        // The original line/column might not exist if the file changed drastically.
                        WebViewUtilities.Log($"Error scrolling to original cursor position (line {originalLine}, column {originalColumn}): {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"AcceptButton_Click: Error in AcceptButton_Click - {ex.Message}");
                ShowThemedMessageBox($"Error in AcceptButton_Click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                // If an error occurs, we should still try to clean up the diff context
                if (_diffContext != null)
                {
                    DiffUtility.CloseDiffAndReset(_diffContext);
                    _diffContext = null;
                }
            }
            finally
            {
                // This ensures the buttons are always reset, regardless of success or failure.
                UpdateButtonsForDiffView(false);
            }

        }

        private async void ChooseButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Capture necessary data from the existing context before it gets reset.
                if (_diffContext == null || _diffContext.ActiveDocumentPath == null || string.IsNullOrEmpty(_diffContext.AICodeBlock))
                {
                    // Not enough info to proceed. Reset UI and state.
                    UpdateButtonsForDiffView(false);
                    if (_diffContext != null)
                    {
                        DiffUtility.CloseDiffAndReset(_diffContext);
                        _diffContext = null;
                    }
                    return;
                }

                string originalDocPath = _diffContext.ActiveDocumentPath;
                string aiCode = _diffContext.AICodeBlock;
                var activeDocument = _dte.Documents.Item(originalDocPath);

                // Show the code replacement window. This now uses a valid Document object.
                // The temp files are passed in so they will be skipped in the ChooseCodeWindow.
                var selectedItem = CodeSelectionUtilities.ShowCodeReplacementWindow(_dte, activeDocument, _diffContext?.TempCurrentFile, _diffContext?.TempAiFile);
                if (selectedItem == null)
                {
                    // User cancelled the selection. Go back to the diff view that's still open.
                    return;
                }

                // First, close the existing diff view. This will invalidate the old _diffContext.ActiveDocument reference.
                DiffUtility.CloseDiffAndReset(_diffContext);

                if (activeDocument == null)
                {
                    Log($"ChooseButton_Click: Active document not found for path: {originalDocPath}");
                    ShowThemedMessageBox($"Could not re-acquire a handle to document: {originalDocPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateButtonsForDiffView(false);
                    return;
                }
                activeDocument.Activate(); // Good practice to ensure it's the active document.

                string currentCode = DiffUtility.GetDocumentText(activeDocument);
                if (currentCode == null)
                {
                    Log("ChooseButton_Click: Unable to retrieve current code from document.");
                    ShowThemedMessageBox("Unable to retrieve current code from document.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    UpdateButtonsForDiffView(false);
                    return;
                }


                // --- START: BUG FIX ---
                // Default to the active document, but check if the user selected a different file.
                Document targetDoc = activeDocument;
                string codeToModify = currentCode;

                // If the user chose a specific file, update the target document and its code.
                if (selectedItem.Type == "file" || selectedItem.Type == "opened_file")
                {
                    string targetFilePath = selectedItem.FilePath;
                    try
                    {
                        // Attempt to get the document if open, otherwise open it.
                        targetDoc = _dte.Documents.Item(targetFilePath);
                    }
                    catch (ArgumentException) // Not open, so open it
                    {
                        try
                        {
                            _dte.ItemOperations.OpenFile(targetFilePath);
                            targetDoc = _dte.Documents.Item(targetFilePath);
                        }
                        catch (Exception ex)
                        {
                            WebViewUtilities.Log($"ChooseButton_Click: Error opening selected file '{targetFilePath}': {ex.Message}");
                            ShowThemedMessageBox($"Error opening selected file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }
                    }

                    // Retrieve the code from the now-targeted document.
                    codeToModify = DiffUtility.GetDocumentText(targetDoc);
                }
                // --- END: BUG FIX ---

                // Create a new context for the new diff view, following the pattern in PasteButton_ClickLogic.
                var newDiffContext = new DiffUtility.DiffContext { };

                // Calculate the modified code based on the user's selection, using the correct target document and code.
                string modifiedCode = await StringUtil.CreateDocumentContent(_dte, Window.GetWindow(this), codeToModify, aiCode, targetDoc, selectedItem, newDiffContext);

                // Open a new diff view and assign the fully populated context to our member field.
                _diffContext = DiffUtility.OpenDiffView(Window.GetWindow(this), targetDoc, codeToModify, modifiedCode, aiCode, newDiffContext);

                // If opening the diff view failed, reset the UI.
                if (_diffContext == null)
                {
                    UpdateButtonsForDiffView(false);
                }
                // If successful, the UI remains in the "diff view open" state with the new diff.
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"ChooseButton_Click: Error in ChooseButton_Click - {ex.Message}");
                ShowThemedMessageBox($"Error in ChooseButton_Click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateButtonsForDiffView(false);
            }
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_diffContext != null)
            {
                DiffUtility.CloseDiffAndReset(_diffContext);
                _diffContext = null;
            }

            UpdateButtonsForDiffView(false);
        }

        // This button can be relabeled in XAML to "Reset Settings" for clarity.
        private void DebugButton_Click(object sender, RoutedEventArgs e)
        {
            
        }

        private async void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dte.Solution == null || string.IsNullOrEmpty(_dte.Solution.FullName))
            {
                Log("RestoreButton_Click: No solution is currently open.");
                ShowThemedMessageBox("No solution is currently open to restore.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Make sure to close any open compare diffs before opening the restore window
            CloseDiffButtonLogic();

            var solutionBackupsFolder = Path.Combine(_backupsFolder, BackupUtilities.GetUniqueSolutionFolder(_dte));
            string selectedTextForSearch = await WebViewUtilities.RetrieveSelectedTextFromWebViewAsync(ChatWebView, Window.GetWindow(this));

            var restoreWindow = new RestoreSelectionWindow(_dte, ChatWebView, solutionBackupsFolder, selectedTextForSearch);
            bool? result = restoreWindow.ShowDialog();
            // Show close diffs button if there are any diff contexts available
            if (restoreWindow.DiffContexts != null)
            {
                _diffContextsCompare = restoreWindow.DiffContexts;
                CloseDiffsButton.Visibility = Visibility.Visible;
                UseRestoreButton.Visibility = Visibility.Visible;
            }

            if (restoreWindow.SelectedBackup != null)
            {
                if (result == true)
                {
                    string solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
                    if (BackupUtilities.RestoreSolution(_dte, Window.GetWindow(this), restoreWindow.SelectedBackup.FolderPath, solutionDir))
                    {
                        Log("RestoreButton_Click: Solution restored successfully.");
                        ShowThemedMessageBox("Solution restored successfully.", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                    }
                }

                // Handle the case where the user compares with a selected backup
                else if (!restoreWindow.NavigateToUrl)
                {
                    _selectedBackup = restoreWindow.SelectedBackup;
                }

                // Handle the Go To Chat button
                else
                {
                    var optionToSelect = (UrlSelector.ItemsSource as List<UrlOption>)
                        ?.FirstOrDefault(option => option.DisplayName == restoreWindow.SelectedBackup.AIChatTag);

                    if (optionToSelect != null)
                    {
                        optionToSelect.Url = restoreWindow.SelectedBackup.Url;
                        WebViewUtilities.Log($"RestoreButton_Click: Preparing to navigate to '{optionToSelect.DisplayName}' at URL '{optionToSelect.Url}'.");

                        // If the selected option is the one currently displayed, we need to navigate directly. Otherwise, let
                        // the SelectionChanged event handle it.
                        if (UrlSelector.SelectedItem == optionToSelect)
                        {
                            ChatWebView.Source = new Uri(optionToSelect.Url);
                        }
                        else
                        {
                            UrlSelector.SelectedItem = optionToSelect;
                        }
                    }
                }
            }
        }


        private void SaveBackupButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentUrl = WebViewUtilities.GetCurrentUrl(ChatWebView);
                string path = BackupUtilities.CreateSolutionBackup(_dte, Window.GetWindow(this), _backupsFolder, "(Manual save: No AI code available.)", GetUrlSelectorText(), currentUrl);

                if (path != null)
                {
                    string lastFolder = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                    WebViewUtilities.Log($"Manual backup created at: {path}");
                    ShowThemedMessageBox($"Backup created successfully: {lastFolder}", "Success");
                }
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error creating manual backup: {ex.ToString()}");
                ShowThemedMessageBox($"Failed to create backup: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void CloseDiffsButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDiffButtonLogic();
        }

        private void UseRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
                if (BackupUtilities.RestoreSolution(_dte, Window.GetWindow(this), _selectedBackup.FolderPath, solutionDir))
                {
                    Log("Solution restored successfully from backup.");
                    ShowThemedMessageBox("Solution restored successfully.", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error restoring solution from backup: {ex.ToString()}");
                ShowThemedMessageBox($"Failed to restore solution: {ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CloseDiffButtonLogic();
            }
        }

        private void ButtonConfig_Click(object sender, RoutedEventArgs e) => PopupConfig.IsOpen = true;

        private void ButtonSkins_OnClick(object sender, RoutedEventArgs e)
            {
                if (e.OriginalSource is Button button)
                {
                    if (button.Tag is ApplicationTheme themeTag)
                    {
                        // Call the central manager to broadcast the change to all listeners.
                        Utilities.ThemeUtility.ChangeTheme(themeTag);
                    }
                }
            }

        private void OpenLogButton_Click(object sender, RoutedEventArgs e)
        {
            var logWindow = new LogWindow(PopupConfig);

            logWindow.Show();
        }

        private void Reseturls_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                WebViewUtilities.Log("User confirmed URL reset. Proceeding...");

                // Reset the in-memory URL for each option to its default.
                foreach (var option in _urlOptions)
                {
                    option.Url = option.DefaultUrl;
                }
                WebViewUtilities.Log("All in-memory URLs have been reset to their defaults.");

                // 4. Reset the persisted setting for the next session and save it.
                Settings.Default.selectedChatUrl = "https://aistudio.google.com";
                Settings.Default.Save();
                WebViewUtilities.Log($"Persisted setting 'selectedChatUrl' reset to: {Settings.Default.selectedChatUrl}");

                ShowThemedMessageBox("URLs have been successfully reset.", "Reset Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"An error occurred during URL reset: {ex.ToString()}");
                ShowThemedMessageBox($"Failed to reset URLs: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void TestWebMessageButton_Click(object sender, RoutedEventArgs e)
        {
            if (ChatWebView?.CoreWebView2 == null)
            {
                WebViewUtilities.Log("TestWebMessageButton_Click: CoreWebView2 is not initialized.");
                ShowThemedMessageBox("CoreWebView2 is not initialized.", "Error");
                return;
            }

            try
            {
                // This script will execute inside the WebView and send a message back to us.
                ChatWebView.CoreWebView2.ExecuteScriptAsync("window.chrome.webview.postMessage('manual_test_signal')");
                WebViewUtilities.Log("Manually sent 'manual_test_signal' to WebView.");
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"TestWebMessageButton_Click: Failed to execute script - {ex.Message}");
                ShowThemedMessageBox($"Failed to execute script: {ex.Message}", "Error");
            }
        }

        #endregion

        #region Events

        // This can occur multiple times 
        private async void ChatWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Hook DTE events here
            var events = _dte.Events;
            _documentEvents = events.DocumentEvents;
            _documentEvents.DocumentClosing -= OnDocumentClosing;
            _documentEvents.DocumentClosing += OnDocumentClosing;

            // Start the new, centralized initialization flow.
            await InitializeWebViewAsync();
        }



        private void ChatWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_documentEvents != null)
            {
                _documentEvents.DocumentClosing -= OnDocumentClosing;
            }

            // The saving logic is now handled by CoreWebView2_NavigationCompleted,
            // but saving here as a final fallback is okay.
            string currentUrl = WebViewUtilities.GetCurrentUrl(ChatWebView);
            if (!string.IsNullOrEmpty(currentUrl) && Settings.Default.selectedChatUrl != currentUrl)
            {
                Settings.Default.selectedChatUrl = currentUrl;
                Settings.Default.Save();
                WebViewUtilities.Log($"ChatWindow_Unloaded: Final URL updated: {Settings.Default.selectedChatUrl}");
            }
        }

        private void ChatWindow_ThemeLoader(object sender, RoutedEventArgs e)
        {
            if (_isThemeInitialized) return;

            // 1. Add base dictionaries LOCALLY.
            this.Resources.MergedDictionaries.Add(new HandyControl.Themes.ThemeResources());
            this.Resources.MergedDictionaries.Add(new HandyControl.Themes.Theme());

            // 2. Apply the current theme correctly on first load.
            UpdateTheme(Utilities.ThemeUtility.CurrentTheme);

            // 3. Subscribe to future theme changes.
            Utilities.ThemeUtility.ThemeChanged += UpdateTheme;

            // 4. Unsubscribe on unload.
            this.Unloaded += (s, ev) => Utilities.ThemeUtility.ThemeChanged -= UpdateTheme;

            _isThemeInitialized = true;
        }

        private void OnDocumentClosing(Document document)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            WebViewUtilities.Log($"Document closing: {document.FullName}");

            // Single diff views are skipped
            var singleDiffContext = _diffContext;
            if (singleDiffContext != null &&
                (document.FullName.Equals(singleDiffContext.TempCurrentFile, StringComparison.OrdinalIgnoreCase) ||
                 document.FullName.Equals(singleDiffContext.TempAiFile, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            // Check if the closing document is part of a multi-compare diff view
            if (_diffContextsCompare != null && _diffContextsCompare.Any())
            {
                var contextToRemove = _diffContextsCompare.FirstOrDefault(ctx =>
                    ctx.TempCurrentFile.Equals(document.FullName, StringComparison.OrdinalIgnoreCase) ||
                    ctx.TempAiFile.Equals(document.FullName, StringComparison.OrdinalIgnoreCase));

                if (contextToRemove != null)
                {
                    WebViewUtilities.Log($"Cleaning up manually closed compare-diff view for: {contextToRemove.ActiveDocumentPath}");
                    FileUtil.CleanUpTempFiles(contextToRemove);
                    _diffContextsCompare.Remove(contextToRemove);

                    if (_diffContextsCompare.Count == 0)
                    {
                        WebViewUtilities.Log("All compare diff views now closed. Hiding buttons.");
                        Dispatcher.Invoke(() =>
                        {
                            CloseDiffsButton.Visibility = Visibility.Collapsed;
                            UseRestoreButton.Visibility = Visibility.Collapsed;
                        });
                    }
                }
            }
        }

        private void PopupConfig_Closed(object sender, EventArgs e)
        {
            Settings.Default.Save(); // Save settings when the popup is closed
        }

        private async void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (AutoDiffToggle.IsChecked != true)
            {
                return; 
            }

            string message = args.TryGetWebMessageAsString();
            WebViewUtilities.Log($"Received web message: '{message}'");

            if (message == "copy_signal" || message == "manual_test_signal")
            {
                WebViewUtilities.Log("Signal received successfully! Processing clipboard...");

                // STEP 2: Remove the Dispatcher.Invoke wrapper.
                // Since the event handler is now async, we can await tasks directly.
                try
                {
                    // By making the event handler async, we can properly await the delay.
                    // This small delay gives the browser time to put the text on the clipboard.
                    await Task.Delay(100);

                    // STEP 3: Ensure we are on the main thread for clipboard and DTE access.
                    // This is safer than assuming the event fires on the UI thread.
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    if (_diffContext != null)
                    {
                        // If a diff is already open, don't start a new one.
                        return;
                    }

                    // Clipboard.GetText() can throw and must be on the UI thread.
                    string clipboardText = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;

                    if (!string.IsNullOrEmpty(clipboardText))
                    {
                        // Now we can properly await our async logic method.
                        // The UI will remain responsive, and exceptions will be caught by our try/catch block.
                        await ToVSButton_ClickLogicAsync(clipboardText);
                    }
                    else
                    {
                        WebViewUtilities.Log("Signal received, but clipboard was empty after delay.");
                    }
                }
                catch (Exception ex)
                {
                    // This catch block will now correctly handle exceptions from both
                    // clipboard access and the entire ToVSButton_ClickLogicAsync operation.
                    WebViewUtilities.Log($"Error processing web message signal: {ex.Message}");

                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(); // Ensure UI thread for message box
                    ShowThemedMessageBox($"An error occurred while processing the code from the chat window: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async Task InitializeWebViewAsync()
        {
            if (_isWebViewInitialized)
            {
                return;
            }

            try
            {
                // Step 1: Call the simplified utility to ensure CoreWebView2 is ready.
                await WebViewUtilities.InitializeWebView2Async(ChatWebView, _webViewDataFolder);

                // Step 2: CoreWebView2 is now guaranteed to be available. Configure it.
                Log("CoreWebView2 initialized successfully. Now configuring settings and events.");
                ChatWebView.CoreWebView2.Settings.IsWebMessageEnabled = true;
                ChatWebView.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                ChatWebView.CoreWebView2.Settings.IsStatusBarEnabled = false;

                // Step 3: Load and inject the interceptor script.
                string script = FileUtil.LoadScript("copyInterceptor.js");
                if (!script.StartsWith("console.error"))
                {
                    await ChatWebView.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(script);
                    Log("Successfully registered the copyInterceptor.js script.");
                }
                else
                {
                    Log($"CRITICAL: Failed to load copyInterceptor.js. Script content: {script}");
                }

                // Step 4: Hook the event handlers DIRECTLY. This is now safe and reliable.
                ChatWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                ChatWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted; // For error reporting
                Log("WebMessageReceived and NavigationCompleted handlers attached.");

                // Step 5: Perform the initial navigation.
                if (UrlSelector.SelectedItem is UrlOption selectedOption && !string.IsNullOrEmpty(selectedOption.Url))
                {
                    ChatWebView.Source = new Uri(selectedOption.Url);
                }

                _isWebViewInitialized = true;
                Log("WebView initialization complete. UI interactions are now enabled.");
            }
            catch (Exception ex)
            {
                Log($"A critical error occurred during WebView initialization: {ex.Message}");
                ShowThemedMessageBox($"The AI Chat panel could not be initialized. Please try restarting Visual Studio.\n\nError: {ex.Message}", "Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }


        // This is the navigation error handler, kept separate for clarity.
        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess)
            {
                string currentUrl = WebViewUtilities.GetCurrentUrl(ChatWebView);
                Log($"Navigation successful to: {currentUrl}");

                // 1. Persist this URL for the next time Visual Studio starts. 
                Settings.Default.selectedChatUrl = currentUrl;
                Settings.Default.Save();
                Log($"CoreWebView2_NavigationCompleted: Saved last known URL for session restore: {Settings.Default.selectedChatUrl}");

                // 2. Update the in-memory cache for the current UrlOption.
                // This is mainly for the go to chat button
                string chatName = GetChatNameFromUrl(currentUrl);
                if (!string.IsNullOrEmpty(chatName))
                {
                    var currentOption = _urlOptions.FirstOrDefault(o => o.DisplayName == chatName);
                    if (currentOption != null && currentOption.Url != currentUrl)
                    {
                        currentOption.Url = currentUrl;
                        Log($"Updated in-memory URL for '{chatName}' to: {currentUrl}");
                    }
                }
            }
            else
            {
                string errorMessage = $"Failed to load {ChatWebView.Source?.ToString() ?? "page"}. Error status: {e.WebErrorStatus}.";
                if (e.WebErrorStatus == Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.CannotConnect ||
                    e.WebErrorStatus == Microsoft.Web.WebView2.Core.CoreWebView2WebErrorStatus.HostNameNotResolved)
                {
                    errorMessage += " Please check your internet connection.";
                }

                WebViewUtilities.Log(errorMessage);
                ShowThemedMessageBox(errorMessage, "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }


        #endregion

        
    }
}