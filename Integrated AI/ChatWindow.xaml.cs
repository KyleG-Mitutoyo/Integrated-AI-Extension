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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Contexts;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Forms.VisualStyles;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using static Integrated_AI.RestoreSelectionWindow;
using static Integrated_AI.Utilities.DiffUtility;
using static Integrated_AI.WebViewUtilities;
using MessageBox = HandyControl.Controls.MessageBox;

//TODO: Show method previews on hover in function list box
//AI chat text select to search for the matching restore point
//Fix copy button detection in WebView2-------
//Finish deepseek integration-------
//Add error to AI------
//Fix extension freezes when navigation fails, eg a captcha pops up------
//Verify that freezes won't happen------
//Make auto diff view bring up choose code window rather than using cursor insert with failed matches
//Make recent functions work per file
//Make file list a treeview with folders and files, with a + button under each folder to make a new file there
//Combine choosecodewindow and functionselectionwindow into a single window
//Use better method to detect functions, clean up program flow for code replacement
//In context menus for the toAI commands
//Add code snippet option to choosecodewindow

namespace Integrated_AI
{
    public partial class ChatWindow : UserControl
    {
        public class UrlOption
        {
            public string DisplayName { get; set; }
            public string Url { get; set; }
        }

        // Existing fields...
        public List<UrlOption> _urlOptions = new List<UrlOption>
        {
            new UrlOption { DisplayName = "Grok", Url = "https://grok.com" },
            new UrlOption { DisplayName = "Google AI Studio", Url = "https://aistudio.google.com" },
            new UrlOption { DisplayName = "ChatGPT", Url = "https://chatgpt.com" },
            new UrlOption { DisplayName = "Claude", Url = "https://claude.ai" },
            new UrlOption { DisplayName = "Deepseek", Url = "https://chat.deepseek.com" }
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
        private static bool _isWebViewInFocus;
        private string _lastClipboardText; // Tracks last processed clipboard content
        private string _currentClipboardText; // Tracks clipboard content for current burst of events
        private bool _isOpeningCodeWindow = false;
        private bool _isProcessingClipboardAction = false;
        private DocumentEvents _documentEvents;


        public ChatWindow()
        {
            InitializeComponent();
            var dummy = typeof(HandyControl.Controls.Window);
            InitializeUrlSelector();
            _dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            _diffContext = null;
            _isWebViewInFocus = false;

            // Define base folder for the extension
            var baseFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIChatExtension",
                Environment.UserName);
            Directory.CreateDirectory(baseFolder);

            // Define separate folders for WebView2, app data, and backups
            _webViewDataFolder = Path.Combine(baseFolder, "WebViewData");
            _appDataFolder = Path.Combine(baseFolder, "AppData");
            _backupsFolder = Path.Combine(baseFolder, "Backups");
            Directory.CreateDirectory(_webViewDataFolder);
            Directory.CreateDirectory(_appDataFolder);
            Directory.CreateDirectory(_backupsFolder);

            FileUtil._recentFunctionsFilePath = Path.Combine(_appDataFolder, "recent_functions.txt");

            // Initialize WebView2
            WebViewUtilities.InitializeWebView2Async(ChatWebView, _webViewDataFolder, _urlOptions, UrlSelector);

            // Register for window messages when the control is loaded
            Loaded += ChatWindow_Loaded;
            Unloaded += ChatWindow_Unloaded;

            PopupConfig.Closed += PopupConfig_Closed;

            ChatWebView.CoreWebView2InitializationCompleted += ChatWebView_CoreWebView2InitializationCompleted;
        }

        private void ChatWindow_Loaded(object sender, RoutedEventArgs e)
        {
            var events = _dte.Events;
            _documentEvents = events.DocumentEvents;
            _documentEvents.DocumentClosing += OnDocumentClosing;
        }

        private void ChatWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            if (_documentEvents != null)
            {
                _documentEvents.DocumentClosing -= OnDocumentClosing;
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

        private void InitializeUrlSelector()
        {
            // Populate ComboBox with URL options
            UrlSelector.ItemsSource = _urlOptions;

            // Load saved selection
            if (!string.IsNullOrEmpty(Settings.Default.selectedChat))
            {
                var savedOption = (UrlSelector.ItemsSource as List<UrlOption>)
                    ?.FirstOrDefault(option => option.DisplayName == Settings.Default.selectedChat);
                if (savedOption != null)
                {
                    UrlSelector.SelectedItem = savedOption;
                }
            }
        }

        private string GetSelectedComboBoxText()
        {
            if (UrlSelector.SelectedItem is UrlOption selectedOption)
            {
                return selectedOption.DisplayName; // Returns the display text
            }
            return string.Empty; // Return empty string if nothing is selected
        }
   
        
        private void ToVSButton_ClickLogic(string aiCode, string pasteType = null, string functionName = null)
        {
            ThreadHelper.Generic.BeginInvoke(() =>
            {
                Action abortAction = () =>
                {
                    Dispatcher.Invoke(() =>
                    {
                        _isProcessingClipboardAction = false;
                        _lastClipboardText = null;
                    });
                };

                // Check if a diff window is already open. The new _isProcessingClipboardAction handles the race condition.
                if (_diffContext != null)
                {
                    WebViewUtilities.Log("PasteButton_ClickLogic: Diff window already open. Aborting.");
                    abortAction();
                    return;
                }

                if (_dte == null)
                {
                    MessageBox.Show("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    abortAction();
                    return;
                }

                if (string.IsNullOrEmpty(aiCode))
                {
                    MessageBox.Show("No code retrieved from clipboard.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    abortAction();
                    return;
                }

                var activeDocument = _dte.ActiveDocument;
                if (activeDocument == null)
                {
                    MessageBox.Show("No active document.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    abortAction();
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
                
                //Later, this will only occur at the end of analyzecodeblock with auto diff mode on, as a fallback
                //if (!(bool)AutoDiffToggle.IsChecked)
                //{
                //    // Use _isOpeningCodeWindow as a sub-lock for just this window, which is fine
                //    _isOpeningCodeWindow = true;
                //    try
                //    {
                //        selectedItem = CodeSelectionUtilities.ShowCodeReplacementWindow(_dte, activeDocument);
                //        if (selectedItem == null)
                //        {
                //            abortAction(); // User cancelled, so abort and release the main lock
                //            return;
                //        }
                //    }
                //    finally
                //    {
                //        _isOpeningCodeWindow = false;
                //    }
                //}

                _diffContext = new DiffUtility.DiffContext { };
                modifiedCode = StringUtil.ReplaceOrAddCode(_dte, modifiedCode, aiCode, activeDocument, selectedItem, _diffContext);
                _diffContext = DiffUtility.OpenDiffView(activeDocument, currentCode, modifiedCode, aiCode, _diffContext);

                if (_diffContext == null)
                {
                    // If diff view failed to open, abort and release the lock
                    abortAction();
                    return;
                }

                // If diff view opened successfully, the lock remains active.
                // Update UI on the main thread.
                Dispatcher.Invoke(() =>
                {
                    UpdateButtonsForDiffView(true);
                });
            });
        }

        private void Window_GotFocus(object sender, RoutedEventArgs e)
        {
            _isWebViewInFocus = true;
        }

        private void Window_LostFocus(object sender, RoutedEventArgs e)
        {
            _isWebViewInFocus = false;
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
                        Settings.Default.selectedChat = selectedOption.DisplayName; // Save the selected chat option
                        Settings.Default.Save(); // Save settings to persist the selection
                    }
                }
                catch (UriFormatException ex)
                {
                    MessageBox.Show($"Invalid URL '{selectedOption.Url}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private async void SplitButtonToAI_Click(object sender, RoutedEventArgs e)
        {
            if (_executeCommandOnClick)
            {
                await WebViewUtilities.ExecuteCommandAsync(_selectedOptionToAI, _dte, ChatWebView, _webViewDataFolder);
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
                MessageBox.Show($"Error executing {_selectedOptionToVS}: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
                await WebViewUtilities.ExecuteCommandAsync(option, _dte, ChatWebView, _webViewDataFolder);
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

        private async void ExecuteToVSCommand()
        {
            if (_dte == null)
            {
                MessageBox.Show("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            //Get AI code either from selected text or the clipboard if no text is selected
            string aiCode = await WebViewUtilities.RetrieveSelectedTextFromWebViewAsync(ChatWebView);


            if (aiCode == null || aiCode == "null" || string.IsNullOrEmpty(aiCode))
            {
                aiCode = Clipboard.GetText(); // Fallback to clipboard text if WebView retrieval fails
                                                //Optional: check again if aicode is null or empty after clipboard retrieval
                WebViewUtilities.Log("SplitButtonToVS_Click: Retrieved code from clipboard as WebView retrieval failed.");
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
                    ToVSButton_ClickLogic(aiCode, "new_function");
                    return;
                }

                // If there are functions, show the selection window or auto match
                if ((bool)AutoFunctionMatch.IsChecked)
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
                        MessageBox.Show($"Selected function: {functionName}", "Function Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        return;
                    }
                }

                ToVSButton_ClickLogic(aiCode, functionType, functionName);
            }

            else if (_selectedOptionToVS == "File -> VS")
            {
                ToVSButton_ClickLogic(aiCode, pasteType: "file");
            }

            else if (_selectedOptionToVS == "Snippet -> VS")
            {
                ToVSButton_ClickLogic(aiCode, pasteType: "snippet");
            }
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

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
                aiCodeFullFile = FileUtil.GetAICode(contextToClose.TempAiFile);
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
                    DiffUtility.ApplyChanges(_dte, documentToModify, aiCodeFullFile);
                }
            }

            // A backup of the solution files are created for every accept button click, if enabled.
            if ((bool)AutoRestore.IsChecked)
            {
                // If we want to add the code extension to get syntax highlghting in the restore window
                // we'd use documentToModify file extension
                BackupUtilities.CreateSolutionBackup(_dte, _backupsFolder, contextToClose.AICodeBlock, GetSelectedComboBoxText());
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

            UpdateButtonsForDiffView(false);

            _isProcessingClipboardAction = false;
            _lastClipboardText = null;
        }

        private void ChooseButton_Click(object sender, RoutedEventArgs e)
        {
            // Capture necessary data from the existing context before it gets reset.
            if (_diffContext == null || _diffContext.ActiveDocumentPath == null || string.IsNullOrEmpty(_diffContext.AICodeBlock))
            {
                // Not enough info to proceed. Reset UI and state.
                UpdateButtonsForDiffView(false);
                _isProcessingClipboardAction = false;
                _lastClipboardText = null;
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

            if (activeDocument == null) {
                MessageBox.Show($"Could not re-acquire a handle to document: {originalDocPath}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateButtonsForDiffView(false);
                _isProcessingClipboardAction = false;
                _lastClipboardText = null;
                return;
            }
            activeDocument.Activate(); // Good practice to ensure it's the active document.

            string currentCode = DiffUtility.GetDocumentText(activeDocument);
            if (currentCode == null)
            {
                MessageBox.Show("Unable to retrieve current code from document.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                UpdateButtonsForDiffView(false);
                _isProcessingClipboardAction = false;
                _lastClipboardText = null;
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
                        MessageBox.Show($"Error opening selected file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
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
            string modifiedCode = StringUtil.ReplaceOrAddCode(_dte, codeToModify, aiCode, targetDoc, selectedItem, newDiffContext);

            // Open a new diff view and assign the fully populated context to our member field.
            _diffContext = DiffUtility.OpenDiffView(targetDoc, codeToModify, modifiedCode, aiCode, newDiffContext);

            // If opening the diff view failed, reset the UI.
            if (_diffContext == null)
            {
                UpdateButtonsForDiffView(false);
                _isProcessingClipboardAction = false;
                _lastClipboardText = null;
            }
            // If successful, the UI remains in the "diff view open" state with the new diff.
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_diffContext != null)
            {
                DiffUtility.CloseDiffAndReset(_diffContext);
                _diffContext = null;
            }

            UpdateButtonsForDiffView(false);

            _isProcessingClipboardAction = false;
            _lastClipboardText = null;
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

        // Integrated AI/ChatWindow.xaml.cs
        private void DebugButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(StringUtil.GetIndentPosition("        private void RestoreButton_Click(object sender, RoutedEventArgs e)").ToString(), "Debug", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dte.Solution == null || string.IsNullOrEmpty(_dte.Solution.FullName))
            {
                MessageBox.Show("No solution is currently open to restore.", "Error", MessageBoxButton.OK, MessageBoxImage.Error, MessageBoxResult.None);
                return;
            }

            // Make sure to close any open compare diffs before opening the restore window
            CloseDiffButtonLogic();

            var solutionBackupsFolder = Path.Combine(_backupsFolder, BackupUtilities.GetUniqueSolutionFolder(_dte));
            var restoreWindow = new RestoreSelectionWindow(_dte, solutionBackupsFolder);
            bool? result = restoreWindow.ShowDialog();
            // Show close diffs button if there are any diff contexts available
            if (restoreWindow.DiffContexts != null)
            {
                _diffContextsCompare = restoreWindow.DiffContexts;
                CloseDiffsButton.Visibility = Visibility.Visible;
                UseRestoreButton.Visibility = Visibility.Visible;
            }

            if (result == true && restoreWindow.SelectedBackup != null)
            {
                string solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
                if (BackupUtilities.RestoreSolution(_dte, restoreWindow.SelectedBackup.FolderPath, solutionDir))
                {
                    MessageBox.Show("Solution restored successfully.", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }

            // Handle the case where the user compares with a selected backup
            else if (restoreWindow.SelectedBackup != null)
            {
                _selectedBackup = restoreWindow.SelectedBackup;
            }
        }

        private void SaveBackupButton_Click(object sender, RoutedEventArgs e)
        {
            string path = BackupUtilities.CreateSolutionBackup(_dte, _backupsFolder, "(Manual save: No AI code available.)", GetSelectedComboBoxText());

            if (path != null)
            {
                string lastFolder = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar));
                MessageBox.Show($"Backup created successfully in: {lastFolder}", "Success");
            }
        }

        private void CloseDiffsButton_Click(object sender, RoutedEventArgs e)
        {
            CloseDiffButtonLogic();
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

        private void UseRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            string solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
            if (BackupUtilities.RestoreSolution(_dte, _selectedBackup.FolderPath, solutionDir))
            {
                MessageBox.Show("Solution restored successfully.", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            }

            CloseDiffButtonLogic();
        }

        private void ButtonConfig_Click(object sender, RoutedEventArgs e) => PopupConfig.IsOpen = true;

        private void ButtonSkins_OnClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Button button)
            {
                if (button.Tag is ApplicationTheme themeTag)
                {
                    ThemeUtility.ChangeTheme(themeTag);
                }
            }
        }

        private void PopupConfig_Closed(object sender, EventArgs e)
        {
            Settings.Default.Save(); // Save settings when the popup is closed
        }

        private void CoreWebView2_WebMessageReceived(object sender, Microsoft.Web.WebView2.Core.CoreWebView2WebMessageReceivedEventArgs args)
        {
            string message = args.TryGetWebMessageAsString();
            if (message == "programmatic_copy_complete")
            {
                // The message now confirms the clipboard IS ready.
                // No polling needed. Just process the clipboard content.
                ProcessCopiedCode();
            }
        }

        private void ChatWebView_CoreWebView2InitializationCompleted(object sender, Microsoft.Web.WebView2.Core.CoreWebView2InitializationCompletedEventArgs e)
        {
            // Check if the initialization was successful
            if (e.IsSuccess)
            {
                // It is now SAFE to access CoreWebView2 and attach further event handlers.
                ChatWebView.CoreWebView2.WebMessageReceived += CoreWebView2_WebMessageReceived;
                WebViewUtilities.Log("ChatWindow is now listening for WebMessages from the WebView.");
            }
            else
            {
                WebViewUtilities.Log($"FATAL: CoreWebView2 initialization failed in ChatWindow. Exception: {e.InitializationException}");
                MessageBox.Show($"WebView2 failed to initialize. The chat view may not function correctly. Error: {e.InitializationException.Message}", "WebView Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ProcessCopiedCode()
        {
            // Ensure we run on the UI thread for clipboard access, then call the logic
            Dispatcher.Invoke(() =>
            {
                // Ignore if a diff view is already open. This is our only guard.
                if (_diffContext != null)
                {
                    return;
                }

                string clipboardText = string.Empty;
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        clipboardText = Clipboard.GetText();
                    }
                }
                catch (Exception ex)
                {
                    WebViewUtilities.Log($"Failed to get clipboard text: {ex.Message}");
                    return; // Exit if we can't access the clipboard
                }


                if (!string.IsNullOrEmpty(clipboardText))
                {
                    WebViewUtilities.Log("Programmatic copy confirmed. Processing content.");
                    ToVSButton_ClickLogic(clipboardText);
                }
                else
                {
                    WebViewUtilities.Log("Received copy confirmation, but clipboard was empty.");
                }
            });
        }  
    }
}