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
            public bool UseMarkdown { get; set; } = false; // Default to false, can be overridden by specific options
        }

        // Existing fields...
        public List<AIChatOption> _urlOptions = new List<AIChatOption>
        {
            new AIChatOption { DisplayName = "Grok", Url = "https://grok.com", DefaultUrl = "https://grok.com", UseMarkdown = true },
            new AIChatOption { DisplayName = "Google AI Studio", Url = "https://aistudio.google.com", DefaultUrl = "https://aistudio.google.com", UseMarkdown = true},
            new AIChatOption { DisplayName = "Gemini", Url = "https://gemini.google.com/app", DefaultUrl = "https://gemini.google.com/app"},
            new AIChatOption { DisplayName = "ChatGPT", Url = "https://chatgpt.com", DefaultUrl = "https://chatgpt.com" },
            new AIChatOption { DisplayName = "Claude", Url = "https://claude.ai" , DefaultUrl = "https://claude.ai", UseMarkdown = true},
            new AIChatOption { DisplayName = "Deepseek", Url = "https://chat.deepseek.com", DefaultUrl = "https://chat.deepseek.com" }
        };

        private readonly string _webViewDataFolder;
        private readonly string _appDataFolder;
        private readonly string _backupsFolder;

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

        #region Methods for To VS Commands

        /// <summary>
        /// Retrieves code, prioritizing highlighted text in the WebView over the clipboard.
        /// </summary>
        public async Task<string> GetAiCodeAsync(bool checkSelectedText = true)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            string aiCode = null;

            if (checkSelectedText)
            {
                aiCode = await WebViewUtilities.RetrieveSelectedTextFromWebViewAsync(ChatWebView, Window.GetWindow(this));
            }

            if (string.IsNullOrEmpty(aiCode) || aiCode == "null")
            {
                aiCode = System.Windows.Clipboard.GetText();
                WebViewUtilities.Log("No text highlighted in WebView or checkSelectedText set to false. Using clipboard content.");
            }
            else
            {
                WebViewUtilities.Log("Using highlighted text from WebView.");
            }

            return string.IsNullOrEmpty(aiCode) ? null : StringUtil.RemoveBaseIndentation(aiCode);
        }

        /// <summary>
        /// Creates a diff view or a new file based on the provided AI code and parameters.
        /// </summary>
        public async Task CreateDiffOrNewFileAsync(string aiCode, CancellationToken cancellationToken, string pasteType = null, string functionName = null)
        {
            if (_diffContext != null)
            {
                Log("CreateDiffOrNewFileAsync: Diff window already open. Aborting.");
                return;
            }
            if (_dte == null)
            {
                Log("CreateDiffOrNewFileAsync: DTE service not available.");
                return;
            }

            var activeDocument = _dte.ActiveDocument;
            string currentCode = activeDocument != null ? DiffUtility.GetDocumentText(activeDocument) : string.Empty;

            ChooseCodeWindow.ReplacementItem selectedItem = null;

            // If pastetype is given, assign it to selectedItem to prevent unneeded analyzecodeblock later
            if (pasteType != null)
            {
                selectedItem = new ChooseCodeWindow.ReplacementItem { Type = pasteType, DisplayName = functionName };
            }
            
            // Create a local context for the operation. Do not assign to the member variable until a diff is actually opened.
            var newDiffContext = new DiffUtility.DiffContext { };

            var result = DocumentTextUtil.CreateDocumentContent(_dte, Window.GetWindow(this), currentCode, aiCode, activeDocument, selectedItem, newDiffContext);

            if (result.IsNewFileCreationRequired)
            {
                // User chose to create a new file. Await its creation.
                await FileUtil.CreateNewFileInSolutionAsync(ThreadHelper.JoinableTaskFactory, _dte, result.NewFilePath, result.NewFileContent);
                
                // Since a new file was created and no diff view is open, ensure the UI reflects this by hiding the diff buttons.
                UpdateButtonsForDiffView(false);
            }
            else
            {
                // Attempt to open a diff view for the code replacement.
                _diffContext = await DiffUtility.OpenDiffViewAsync(Window.GetWindow(this), activeDocument, currentCode, result.ModifiedCode, aiCode, newDiffContext, false, true, cancellationToken);
                
                // Update the UI based on whether the diff view was successfully created.
                if (_diffContext != null)
                {
                    // Diff view is open, so show the Accept/Decline buttons.
                    UpdateButtonsForDiffView(true);
                }
                else
                {
                    // If the diff view was not opened (e.g., user cancelled, or an error occurred),
                    // ensure the diff buttons are hidden.
                    UpdateButtonsForDiffView(false);
                }
            }
        }

        #endregion

        #region Helper Methods

        /// <summary>
        /// Shows a HandyControl MessageBox that is correctly themed by associating it with the parent window.
        /// </summary>
        public MessageBoxResult ShowThemedMessageBox(
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

        private void UpdateButtonsForDiffView(bool showDiffButons)
        {
            if (showDiffButons)
            {
                ToAIInfoButton.Visibility = Visibility.Collapsed;
                ToVSInfoButton.Visibility = Visibility.Collapsed;
                RestoreButton.Visibility = Visibility.Collapsed;
                SaveBackupButton.Visibility = Visibility.Collapsed;
                AcceptButton.Visibility = Visibility.Visible;
                ChooseButton.Visibility = Visibility.Visible;
                DeclineButton.Visibility = Visibility.Visible;
            }

            else
            {
                ToAIInfoButton.Visibility = Visibility.Visible;
                ToVSInfoButton.Visibility = Visibility.Visible;
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
                // Use a CancellationToken for this specific operation, as it can be long.
                using (var cts = new CancellationTokenSource())
                {
                    var cancellationToken = cts.Token;
                    try
                    {
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

                        ChooseButton.IsEnabled = false; // Prevent double-clicks during the operation.

                        // --- Step 1: Guard clause and capture current context ---
                        if (_diffContext == null || string.IsNullOrEmpty(_diffContext.AICodeBlock))
                        {
                            UpdateButtonsForDiffView(false); // Reset UI state as it's invalid.
                            if (_diffContext != null)
                            {
                                DiffUtility.CloseDiffAndReset(_diffContext);
                                _diffContext = null;
                            }
                            return;
                        }

                        // Capture essential info from the current diff context before showing a modal dialog.
                        string aiCode = _diffContext.AICodeBlock;
                        var originalDocument = _dte.Documents.Item(_diffContext.ActiveDocumentPath);
                        string tempCurrentFile = _diffContext.TempCurrentFile;
                        string tempAiFile = _diffContext.TempAiFile;

                        // --- Step 2: Show the code selection window (this is a blocking, modal call) ---
                        var selectedItem = CodeSelectionUtilities.ShowCodeReplacementWindow(
                            _dte, originalDocument, tempCurrentFile, tempAiFile);

                        // --- Step 3: Handle the user's choice ---
                        if (selectedItem == null)
                        {
                            // User cancelled by closing the window. The original diff view is still open.
                            // We simply abort the "choose different code" operation.
                            WebViewUtilities.Log("ChooseButton_Click: User cancelled selection. Original diff view remains.");
                            return; // Exit the operation.
                        }

                        // The user has committed to a new target. Now we will replace the current diff view.

                        // --- Step 4: Close the original diff view to make way for the new one ---
                        DiffUtility.CloseDiffAndReset(_diffContext);
                        _diffContext = null; // This is critical for the guard clause in CreateDiffOrNewFileAsync.

                        // --- Step 5: Prepare the environment for the new operation ---
                        // If the user selected a different file, we must open and activate it
                        // so it becomes the `_dte.ActiveDocument` for the next step.
                        if (selectedItem.Type == "file" || selectedItem.Type == "opened_file")
                        {
                            try
                            {
                                _dte.ItemOperations.OpenFile(selectedItem.FilePath);
                            }
                            catch (Exception ex)
                            {
                                WebViewUtilities.Log($"ChooseButton_Click: Error opening selected file '{selectedItem.FilePath}': {ex.Message}");
                                ShowThemedMessageBox($"Error opening selected file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                                UpdateButtonsForDiffView(false); // Reset UI on failure since there's no diff view now.
                                return;
                            }
                        }

                        // --- Step 6: Execute the core logic as if a new command was triggered ---
                        // This single call now handles all cases (snippet, function, file, new_file)
                        // based on the user's selection, and will create the new diff view or file.
                        await CreateDiffOrNewFileAsync(aiCode, cancellationToken, selectedItem.Type, selectedItem.DisplayName);
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
                        // CRITICAL: Always re-enable the button in case of cancellation or error.
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

        private void ToAIButton_Click(object sender, RoutedEventArgs e)
        {
            PopupGuide.IsOpen = true;
        }

        private void GuideGotItButton_Click(object sender, RoutedEventArgs e)
        {
            // Close the popup
            PopupGuide.IsOpen = false;
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
            string textForSearch = await GetAiCodeAsync();

            var restoreWindow = new RestoreSelectionWindow(_dte, ChatWebView, solutionBackupsFolder, textForSearch);
            bool? result = restoreWindow.ShowDialog();

            if (restoreWindow.SelectedBackup != null)
            {
                // Case 1: User clicked the "Restore" button in the dialog.
                if (result == true)
                {
                    // REPLACED BLOCK: Call the new unified restore method.
                    _ = BackupUtilities.RestoreSolutionAsync(_dte, Window.GetWindow(this), restoreWindow.SelectedBackup.FolderPath);
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

        private async void UseRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            // Add a guard clause to ensure a backup was actually selected.
            if (_selectedBackup == null)
            {
                WebViewUtilities.Log("UseRestoreButton_Click called but _selectedBackup was null.");
                ShowThemedMessageBox("No backup was selected to apply.", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Call the new unified restore method.
                await BackupUtilities.RestoreSolutionAsync(_dte, Window.GetWindow(this), _selectedBackup.FolderPath);
            }
            catch (Exception ex)
            {
                // The async method has its own internal error handling, but we can log if the task itself fails.
                WebViewUtilities.Log($"Error calling RestoreSolutionAsync: {ex.ToString()}");
                ShowThemedMessageBox($"An unexpected error occurred: {ex.Message}", "Restore Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                // This logic remains, as it's specific to closing the compare view.
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

        // This handler is just for the auto code replace feature
        private void CoreWebView2_WebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            if (AutoDiffToggle.IsChecked != true) return;

            string message = args.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(message)) return;

            // Our custom string protocol identifiers
            const string selectionPrefix = "copy_signal_with_selection::";
            const string simpleSignal = "copy_signal";

            string codeFromSelection = null;
            bool isOurSignal = false;
            
            // Check if the message contains our prefixed selection
            if (message.StartsWith(selectionPrefix))
            {
                isOurSignal = true;
                // Extract the text that comes after the prefix
                codeFromSelection = message.Substring(selectionPrefix.Length);
                Log("Auto-Diff signal received with selection payload.");
            }
            // Check if it's just the simple fallback signal
            else if (message == simpleSignal)
            {
                isOurSignal = true;
                Log("Auto-Diff simple signal received. Will fall back to clipboard.");
            }

            // If the message was neither, ignore it.
            if (!isOurSignal) return;

            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                using (var cts = new CancellationTokenSource())
                {
                    try
                    {
                        await Task.Delay(50); // Small delay for clipboard, just in case
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                        string aiCode;

                        // --- NEW LOGIC: Prioritize the text sent directly in our message ---
                        if (!string.IsNullOrEmpty(codeFromSelection))
                        {
                            Log("Using highlighted text provided by the copy interceptor.");
                            aiCode = StringUtil.RemoveBaseIndentation(codeFromSelection);
                            //Log(aiCode);
                        }
                        else
                        {
                            // If no text was sent, fall back to the clipboard.
                            // GetAiCodeAsync checks clipboard.
                            Log("No selection in payload. Falling back to GetAiCodeAsync (clipboard).");
                            aiCode = await GetAiCodeAsync(false);
                        }

                        if (string.IsNullOrEmpty(aiCode) || _dte.ActiveDocument == null) return;

                        await CreateDiffOrNewFileAsync(aiCode, cts.Token, null, null);
                    }
                    catch (Exception ex)
                    {
                        Log($"FATAL ERROR in web message processing: {ex.ToString()}");
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
                // Start a 60-second timer. If it completes, we timed out.
                // The CancellationToken will be triggered by NavigationCompleted, cancelling this delay.
                await Task.Delay(TimeSpan.FromSeconds(120), _navigationCts.Token);

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
            // Always cancel the timeout token immediately
            _navigationCts?.Cancel();

            if (e.IsSuccess)
            {
                string currentUrl = WebViewUtilities.GetCurrentUrl(ChatWebView);
                Log($"Navigation successful to: {currentUrl}");

                Settings.Default.selectedChatUrl = currentUrl;
                Settings.Default.Save();

                string chatName = GetChatNameFromUrl(currentUrl);
                if (!string.IsNullOrEmpty(chatName))
                {
                    var currentOption = _urlOptions.FirstOrDefault(o => o.DisplayName == chatName);
                    if (currentOption != null && currentOption.Url != currentUrl)
                    {
                        currentOption.Url = currentUrl;
                    }
                }
            }
            else
            {
                // --- FIX START: Filter out specific error statuses ---

                // ConnectionAborted happens when:
                // 1. The Timeout logic calls Stop()
                // 2. The user clicks a link, then quickly clicks another
                // 3. The user navigates away while page is loading
                // We should SILENTLY log this and return. Do not show a MessageBox.
                if (e.WebErrorStatus == CoreWebView2WebErrorStatus.ConnectionAborted ||
                    e.WebErrorStatus == CoreWebView2WebErrorStatus.OperationCanceled || 
                    e.WebErrorStatus == CoreWebView2WebErrorStatus.ValidAuthenticationCredentialsRequired)
                {
                    Log($"Navigation interrupted (Status: {e.WebErrorStatus}). This is usually normal behavior or a timeout.");
                    return; 
                }
                // --- FIX END ---

                string errorMessage = $"Failed to load {ChatWebView.Source?.ToString() ?? "page"}. Error status: {e.WebErrorStatus}.";

                if (e.WebErrorStatus == CoreWebView2WebErrorStatus.CannotConnect ||
                    e.WebErrorStatus == CoreWebView2WebErrorStatus.HostNameNotResolved)
                {
                    errorMessage += " Please check your internet connection.";
                }

                WebViewUtilities.Log(errorMessage);

                // Only show MessageBox for genuine, unexpected network errors
                ShowThemedMessageBox(errorMessage, "Navigation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        #endregion
    }
}