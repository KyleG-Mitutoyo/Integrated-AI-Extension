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
using System.Threading;


namespace Integrated_AI
{
    public partial class ChatWindow : UserControl
    {
        public class AIChatOption
        {
            public string DisplayName { get; set; }
            public string Url { get; set; }
            public string DefaultUrl { get; set; }
            public bool UsesMarkdown { get; set; } = false; // Default to false, can be overridden by specific options
        }

        // Existing fields...
        public List<AIChatOption> _urlOptions = new List<AIChatOption>
        {
            new AIChatOption { DisplayName = "Grok", Url = "https://grok.com", DefaultUrl = "https://grok.com" },
            new AIChatOption { DisplayName = "Google AI Studio", Url = "https://aistudio.google.com", DefaultUrl = "https://aistudio.google.com", UsesMarkdown = true},
            new AIChatOption { DisplayName = "Gemini", Url = "https://gemini.google.com/app", DefaultUrl = "https://gemini.google.com/app"},
            new AIChatOption { DisplayName = "ChatGPT", Url = "https://chatgpt.com", DefaultUrl = "https://chatgpt.com" },
            new AIChatOption { DisplayName = "Claude", Url = "https://claude.ai" , DefaultUrl = "https://claude.ai", UsesMarkdown = true},
            new AIChatOption { DisplayName = "Deepseek", Url = "https://chat.deepseek.com", DefaultUrl = "https://chat.deepseek.com" }
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

        private CancellationTokenSource _navigationCts;


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
            AIChatOption optionToSelect = null;

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
            if (UrlSelector.SelectedItem is AIChatOption selectedOption)
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


        private async Task ToVSButton_ClickLogicAsync(string aiCode, CancellationToken cancellationToken, string pasteType = null, string functionName = null)
        {
            // We are already on the main thread thanks to the caller's SwitchToMainThreadAsync.
            if (_diffContext != null)
            {
                Log("ToVSButton_ClickLogic: Diff window already open. Aborting.");
                return;
            }

            if (_dte == null)
            {
                Log("ToVSButton_ClickLogic: DTE service not available.");
                ShowThemedMessageBox("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (string.IsNullOrEmpty(aiCode))
            {
                Log("ToVSButton_ClickLogic: No AI code from clipboard.");
                ShowThemedMessageBox("No code retrieved from clipboard.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var activeDocument = _dte.ActiveDocument;
            if (activeDocument == null)
            {
                Log("ToVSButton_ClickLogic: No active document found.");
                ShowThemedMessageBox("No active document.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string currentCode = DiffUtility.GetDocumentText(activeDocument);

            ChooseCodeWindow.ReplacementItem selectedItem = null;
            if (pasteType != null || functionName != null)
            {
                selectedItem = new ChooseCodeWindow.ReplacementItem
                {
                    Type = pasteType,
                    DisplayName = functionName
                };
            }

            _diffContext = new DiffUtility.DiffContext { };

            // Change 2: Call the synchronous CreateDocumentContent and capture the full result object.
            var result = StringUtil.CreateDocumentContent(_dte, Window.GetWindow(this), currentCode, aiCode, activeDocument, selectedItem, _diffContext);

            // Change 3: Check the result and branch the logic.
            if (result.IsNewFileCreationRequired)
            {
                // PATH A: NEW FILE (The slow path)
                // Call our non-blocking async method to create the file.
                // The UI will NOT freeze while this runs.
                await FileUtil.CreateNewFileInSolutionAsync(ThreadHelper.JoinableTaskFactory, _dte, result.NewFilePath, result.NewFileContent);

                // No diff view is opened, so we are done.
            }
            else
            {
                // PATH B: DIFF VIEW (The fast path)
                string modifiedCode = result.ModifiedCode;

                // This is a UI operation and must be on the main thread, which we are.
                _diffContext = await DiffUtility.OpenDiffViewAsync(Window.GetWindow(this), activeDocument, currentCode, modifiedCode, aiCode, _diffContext, false, true, cancellationToken);

                if (_diffContext != null)
                {
                    // Update UI on the main thread. Dispatcher.Invoke is redundant if already on UI thread.
                    UpdateButtonsForDiffView(true);
                }
            }
        }

        private void ExecuteToVSCommand()
        {
            // Wrap the entire command in JTF.RunAsync for stability.
            // This prevents "async void" issues and safely manages the task's lifetime.
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                using (var cts = new CancellationTokenSource())
                {
                    var cancellationToken = cts.Token;
                    try
                    {
                        // Ensure we are on the main thread to safely interact with DTE and show UI.
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        if (_dte == null)
                        {
                            Log("ExecuteToVSCommand: DTE service not available.");
                            ShowThemedMessageBox("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            return;
                        }

                        // Await the retrieval of code from the WebView. This is now safely managed.
                        string aiCode = await WebViewUtilities.RetrieveSelectedTextFromWebViewAsync(ChatWebView, Window.GetWindow(this));

                        if (string.IsNullOrEmpty(aiCode) || aiCode == "null")
                        {
                            aiCode = Clipboard.GetText(); // Fallback to clipboard
                            WebViewUtilities.Log("ExecuteToVSCommand: Retrieved code from clipboard as WebView retrieval failed.");
                        }
                        else
                        {
                            aiCode = StringUtil.RemoveBaseIndentation(aiCode);
                        }

                        // If there's still no code, we can't proceed.
                        if (string.IsNullOrEmpty(aiCode))
                        {
                            ShowThemedMessageBox("No code was found in the selection or clipboard.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }

                        // Prepare variables to pass to the logic handler.
                        string pasteType = null;
                        string functionName = null;

                        if (_selectedOptionToVS == "Function -> VS")
                        {
                            // This block determines the function name and type; it runs on the UI thread, which is correct.
                            string analyzedFunctionName = null;
                            string selectedFunctionType = "function"; // Default to replacing an existing function
                            string solutionPath = Path.GetDirectoryName(_dte.Solution.FullName);
                            string filePath = _dte.ActiveDocument.FullName;
                            string relativePath = FileUtil.GetRelativePath(solutionPath, filePath);

                            var functions = CodeSelectionUtilities.GetFunctionsFromDocument(_dte.ActiveDocument);
                            if (!functions.Any())
                            {
                                Log("ExecuteToVSCommand: No functions found in the active document, treating as a new function.");
                                pasteType = "new_function";
                            }
                            else
                            {
                                if (AutoFunctionMatch.IsChecked == true)
                                {
                                    var (isFunction, autoFunctionName, isFullFile) = StringUtil.AnalyzeCodeBlock(_dte, _dte.ActiveDocument, aiCode);
                                    if (isFunction) analyzedFunctionName = autoFunctionName;
                                }
                                else
                                {
                                    var functionSelectionWindow = new FunctionSelectionWindow(functions, FileUtil._recentFunctionsFilePath, relativePath, true);
                                    if (functionSelectionWindow.ShowDialog() == true && functionSelectionWindow.SelectedFunction != null)
                                    {
                                        selectedFunctionType = functionSelectionWindow.SelectedFunction.DisplayName == "New Function" ? "new_function" : "function";
                                        analyzedFunctionName = functionSelectionWindow.SelectedFunction.DisplayName;
                                    }
                                    else
                                    {
                                        return; // User cancelled the selection window.
                                    }
                                }

                                pasteType = selectedFunctionType;
                                functionName = analyzedFunctionName;
                            }
                        }
                        else if (_selectedOptionToVS == "File -> VS")
                        {
                            pasteType = "file";
                        }
                        else if (_selectedOptionToVS == "Snippet -> VS")
                        {
                            pasteType = "snippet";
                        }
                        else if (_selectedOptionToVS == "New File")
                        {
                            pasteType = "new_file";
                        }

                        // Final, single point of execution. We await the main logic handler,
                        // which will correctly handle both diffing and non-blocking file creation.
                        await ToVSButton_ClickLogicAsync(aiCode, cancellationToken, pasteType, functionName);
                    }
                    catch (Exception ex)
                    {
                        // A robust catch-all for any unexpected errors during the operation.
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(); // Ensure on UI thread for MessageBox
                        string option = _selectedOptionToVS ?? "the operation";
                        WebViewUtilities.Log($"ExecuteToVSCommand: Error executing {option} - {ex.Message}\n{ex.StackTrace}");
                        ShowThemedMessageBox($"An unexpected error occurred while executing '{option}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            });
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

            if (UrlSelector.SelectedItem is AIChatOption selectedOption && !string.IsNullOrEmpty(selectedOption.Url))
            {
                try
                {
                    // First update the old selected option URL with the surrent URL before navigating, to save the state for later.
                    string currentUrl = WebViewUtilities.GetCurrentUrl(ChatWebView);

                    foreach (AIChatOption option in _urlOptions)
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
                await WebViewUtilities.ExecuteCommandAsync(_selectedOptionToAI, _dte, Window.GetWindow(this), ChatWebView, _webViewDataFolder, (AIChatOption)UrlSelector.SelectedItem);
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
                await WebViewUtilities.ExecuteCommandAsync(option, _dte, Window.GetWindow(this), ChatWebView, _webViewDataFolder, (AIChatOption)UrlSelector.SelectedItem);
            }
        }

        private void MenuItemToVS_Click(object sender, RoutedEventArgs e)
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

        private void ChooseButton_Click(object sender, RoutedEventArgs e)
        {
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                // Create our own CancellationTokenSource for this operation.
                using (var cts = new CancellationTokenSource())
                {
                    var cancellationToken = cts.Token;
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                        ChooseButton.IsEnabled = false;

                        // --- Step 1: Initial checks and data gathering (remains the same) ---
                        if (_diffContext == null || _diffContext.ActiveDocumentPath == null || string.IsNullOrEmpty(_diffContext.AICodeBlock))
                        {
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

                        // Show the code replacement window, which is a blocking (modal) dialog.
                        var selectedItem = CodeSelectionUtilities.ShowCodeReplacementWindow(_dte, activeDocument, _diffContext?.TempCurrentFile, _diffContext?.TempAiFile);
                        if (selectedItem == null)
                        {
                            // User cancelled, do nothing. The original diff view is still open.
                            return;
                        }

                        // --- Step 2: Determine the target document and code (remains the same) ---
                        if (activeDocument == null)
                        {
                            Log($"ChooseButton_Click: Active document not found for path: {originalDocPath}");
                            ShowThemedMessageBox($"Could not re-acquire a handle to document: {originalDocPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            DiffUtility.CloseDiffAndReset(_diffContext); // Clean up the old view
                            UpdateButtonsForDiffView(false);
                            return;
                        }
                        activeDocument.Activate();

                        string currentCode = DiffUtility.GetDocumentText(activeDocument);
                        if (currentCode == null)
                        {
                            Log("ChooseButton_Click: Unable to retrieve current code from document.");
                            ShowThemedMessageBox("Unable to retrieve current code from document.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                            DiffUtility.CloseDiffAndReset(_diffContext); // Clean up the old view
                            UpdateButtonsForDiffView(false);
                            return;
                        }

                        Document targetDoc = activeDocument;
                        string codeToModify = currentCode;

                        if (selectedItem.Type == "file" || selectedItem.Type == "opened_file")
                        {
                            string targetFilePath = selectedItem.FilePath;
                            try
                            {
                                targetDoc = _dte.Documents.Item(targetFilePath);
                            }
                            catch (ArgumentException)
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
                                    DiffUtility.CloseDiffAndReset(_diffContext); // Clean up the old view
                                    return;
                                }
                            }
                            codeToModify = DiffUtility.GetDocumentText(targetDoc);
                        }

                        // --- Step 3: Call the synchronous decision-making method ---
                        var newDiffContext = new DiffUtility.DiffContext { };

                        // This call is now synchronous. It quickly determines what to do but does NOT
                        // perform the slow file creation itself.
                        var result = StringUtil.CreateDocumentContent(_dte, Window.GetWindow(this), codeToModify, aiCode, targetDoc, selectedItem, newDiffContext);

                        // --- Step 4: Act based on the result ---

                        // At this point, the user has committed, so we close the old diff view.
                        DiffUtility.CloseDiffAndReset(_diffContext);
                        _diffContext = null; // Clear the context

                        if (result.IsNewFileCreationRequired)
                        {
                            // PATH A: NEW FILE CREATION (The slow operation)
                            // The UI is now free. Reset buttons to their default state.
                            UpdateButtonsForDiffView(false);

                            // Use Task.Run to perform the file I/O on a background thread.
                            // This prevents the UI from freezing.
                            await Task.Run(async () =>
                            {
                                try
                                {
                                    // This runs in the background.
                                    await FileUtil.CreateNewFileInSolutionAsync(ThreadHelper.JoinableTaskFactory, _dte, result.NewFilePath, result.NewFileContent);

                                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                }
                                catch (Exception ex)
                                {
                                    // If the background task fails, show an error on the UI thread.
                                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                                    WebViewUtilities.Log($"ChooseButton_Click: Background file creation failed - {ex.Message}");
                                    ShowThemedMessageBox($"Failed to create new file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                }
                            }, cancellationToken);
                        }
                        else
                        {
                            // PATH B: MODIFY EXISTING DOCUMENT (The fast operation)
                            // Get the modified code from the result.
                            string modifiedCode = result.ModifiedCode;

                            // Open a new diff view with the changes.
                            _diffContext = await DiffUtility.OpenDiffViewAsync(Window.GetWindow(this), targetDoc, codeToModify, modifiedCode, aiCode, newDiffContext, false, true, cancellationToken);

                            if (_diffContext == null)
                            {
                                // If opening the new diff view failed for some reason, reset the UI.
                                UpdateButtonsForDiffView(false);
                            }
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        WebViewUtilities.Log("ChooseButton_Click operation was cancelled.");
                    }
                    catch (Exception ex)
                    {
                        WebViewUtilities.Log($"ChooseButton_Click: Error in ChooseButton_Click - {ex.Message}");

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        ShowThemedMessageBox($"Error in ChooseButton_Click: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        // Ensure UI is reset on any failure.
                        if (_diffContext != null)
                        {
                            DiffUtility.CloseDiffAndReset(_diffContext);
                            _diffContext = null;
                        }
                        UpdateButtonsForDiffView(false);
                    }
                    finally
                    {
                        // CRITICAL: Always restore the UI state.
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        if (ChooseButton != null) ChooseButton.IsEnabled = true;
                    }
                }
            });
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

            // The logic to start the diff process now happens AFTER the window is closed.
            if (restoreWindow.SelectedBackup != null)
            {
                // Case 1: User clicked the "Restore" button in the dialog.
                if (result == true)
                {
                    // Run the restore operation in a JTF-managed task to prevent UI lockups.
                    _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        var statusBar = _dte.StatusBar;
                        statusBar.Text = "Restoring solution from backup...";
                        bool success = false;
                        string errorMessage = string.Empty;

                        try
                        {
                            string solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
                            // This is a blocking operation on the UI thread.
                            success = BackupUtilities.RestoreSolution(_dte, Window.GetWindow(this), restoreWindow.SelectedBackup.FolderPath, solutionDir);
                        }
                        catch (Exception ex)
                        {
                            errorMessage = $"An error occurred during restore: {ex.Message}";
                            Log(errorMessage);
                        }
                        finally
                        {
                                // We don't clear the status bar here, as we will set it to the final status.
                        }

                        // Yield control to the message pump. This allows VS to process any dialogs
                        // it may have queued in response to the file changes (e.g., "inconsistent line endings").
                        await Task.Yield();

                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        if (success)
                        {
                            Log("RestoreButton_Click: Solution restored successfully.");
                            // Use the status bar for a non-intrusive success message.
                            statusBar.Text = "Solution restored successfully.";
                        }
                        else
                        {
                            // If there was an error, it's okay to show a message box.
                            ShowThemedMessageBox(string.IsNullOrEmpty(errorMessage) ? "Failed to restore solution." : errorMessage, "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                            statusBar.Clear();
                        }
                    });
                }
                // Case 2: User clicked the "Compare" button in the dialog.
                else if (!restoreWindow.NavigateToUrl)
                {
                    _selectedBackup = restoreWindow.SelectedBackup;

                    // Check if there are files to compare and start the async diff operation.
                    if (restoreWindow.FilesToCompare != null && restoreWindow.FilesToCompare.Count > 0)
                    {
                        _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                        {
                            var diffContexts = await DiffUtility.OpenMultiFileDiffViewAsync(
                                _dte,
                                Window.GetWindow(this),
                                restoreWindow.FilesToCompare,
                                System.Threading.CancellationToken.None);

                            // Once the async operation is complete, switch back to the main thread to update the UI.
                            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                            if (diffContexts != null && diffContexts.Count > 0)
                            {
                                _diffContextsCompare = diffContexts;
                                CloseDiffsButton.Visibility = Visibility.Visible;
                                UseRestoreButton.Visibility = Visibility.Visible;
                            }
                        });
                    }
                }

                // Case 3: User clicked the "Go To Chat" button.
                else
                {
                    var optionToSelect = (UrlSelector.ItemsSource as List<AIChatOption>)
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

            // --- REVISED THEME LOGIC ---
            // One-time initialization of resource dictionaries.
            if (!_isThemeInitialized)
            {
                this.Resources.MergedDictionaries.Add(new HandyControl.Themes.ThemeResources());
                this.Resources.MergedDictionaries.Add(new HandyControl.Themes.Theme());
                _isThemeInitialized = true;
            }

            // Apply the current theme and subscribe to changes every time the control is loaded.
            UpdateTheme(Utilities.ThemeUtility.CurrentTheme);
            Utilities.ThemeUtility.ThemeChanged += UpdateTheme;
            // ---------------------------

            // Start the new, centralized initialization flow.
            await InitializeWebViewAsync();
        }



        private void ChatWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_documentEvents != null)
            {
                _documentEvents.DocumentClosing -= OnDocumentClosing;
            }

            // --- REVISED THEME LOGIC ---
            // Unsubscribe when the control is hidden/closed to prevent memory leaks and dangling event handlers.
            Utilities.ThemeUtility.ThemeChanged -= UpdateTheme;
            // ---------------------------


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

        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (AutoDiffToggle.IsChecked != true)
            {
                return;
            }

            string message = args.TryGetWebMessageAsString();
            if (message != "copy_signal" && message != "manual_test_signal")
            {
                return;
            }

            WebViewUtilities.Log("Signal received successfully! Starting clipboard processing task...");

            // The CORRECT AND SAFE PATTERN for an event handler kicking off async work.
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                using (var cts = new CancellationTokenSource())
                {
                    var cancellationToken = cts.Token;
                    try
                    {
                        // First, switch to the main thread in a non-blocking way.
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        await Task.Delay(100); // Give clipboard time to update.

                        // This happens if the user clicks a copy button while a diff view is already open.
                        if (_diffContext != null)
                        {
                            WebViewUtilities.Log("Diff context is not null, aborting.");
                            return;
                        }

                        string clipboardText = Clipboard.ContainsText() ? Clipboard.GetText() : string.Empty;
                        WebViewUtilities.Log($"Clipboard content length: {clipboardText?.Length ?? 0}");

                        if (!string.IsNullOrEmpty(clipboardText))
                        {
                            WebViewUtilities.Log("Clipboard has text, calling logic...");
                            // Now we can safely await our async logic method.
                            // It will not deadlock, and its lifetime is managed by the JTF.
                            await ToVSButton_ClickLogicAsync(clipboardText, cancellationToken);
                            WebViewUtilities.Log("Logic call completed.");
                        }
                        else
                        {
                            WebViewUtilities.Log("Signal received, but clipboard was empty after delay.");
                        }
                    }
                    catch (Exception ex)
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                        WebViewUtilities.Log($"FATAL ERROR in web message processing: {ex.ToString()}");
                        ShowThemedMessageBox($"An error occurred while processing the code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    }
                }
            });
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
                ChatWebView.CoreWebView2.WebMessageReceived -= CoreWebView2_WebMessageReceived;
                ChatWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;

                ChatWebView.CoreWebView2.NavigationStarting -= CoreWebView2_NavigationStarting;
                ChatWebView.CoreWebView2.NavigationStarting += CoreWebView2_NavigationStarting;

                ChatWebView.CoreWebView2.NavigationCompleted -= CoreWebView2_NavigationCompleted;
                ChatWebView.CoreWebView2.NavigationCompleted += CoreWebView2_NavigationCompleted;

                Log("WebMessageReceived, NavigationStarting, and NavigationCompleted handlers attached.");

                // Step 5: Perform the initial navigation.
                if (UrlSelector.SelectedItem is AIChatOption selectedOption && !string.IsNullOrEmpty(selectedOption.Url))
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

        private async void CoreWebView2_NavigationStarting(object sender, Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            // A new navigation is starting, so cancel any previous timeout task
            _navigationCts?.Cancel();
            _navigationCts = new CancellationTokenSource();

            try
            {
                // Start a 20-second timer. If it completes, we timed out.
                // The CancellationToken will be triggered by NavigationCompleted, cancelling this delay.
                await Task.Delay(TimeSpan.FromSeconds(20), _navigationCts.Token);

                // --- If the code reaches this line, it means Task.Delay was NOT cancelled ---
                // This is the timeout case. We are likely still on a background thread here.

                // Now, switch to the main thread to safely interact with UI and DTE.
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Check if the CoreWebView2 still exists (the window might have closed)
                if (ChatWebView?.CoreWebView2 != null)
                {
                    // Stop the hung navigation
                    ChatWebView.CoreWebView2.Stop();

                    string errorMessage = $"Navigation timed out. The page might be stuck on a CAPTCHA, login, or is unresponsive.";
                    Log(errorMessage);
                    ShowThemedMessageBox(errorMessage, "Navigation Timeout", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (OperationCanceledException)
            {
                // This is the EXPECTED outcome when navigation completes successfully
                // before the timeout. NavigationCompleted cancels the token, which
                // throws this exception. We can safely ignore it.
                Log("Navigation timeout was successfully cancelled.");
            }
            catch (Exception ex)
            {
                // Catch any other unexpected errors from the timeout logic
                Log($"An unexpected error occurred in the navigation timeout logic: {ex.Message}");
            }
        }


        // This is the navigation error handler, kept separate for clarity.
        private void CoreWebView2_NavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            _navigationCts?.Cancel();

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