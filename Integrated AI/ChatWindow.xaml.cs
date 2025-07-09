using EnvDTE;
using EnvDTE80;
using HandyControl.Controls;
using HandyControl.Themes;
using HandyControl.Tools.Extension;
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
using static Integrated_AI.WebViewUtilities;
using MessageBox = System.Windows.MessageBox;


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
            new UrlOption { DisplayName = "ChatGPT", Url = "https://chatgpt.com" }
        };

        private readonly string _webViewDataFolder;
        private readonly string _appDataFolder;
        private readonly string _backupsFolder;
        private string _selectedOptionToAI = "Method -> AI";
        private string _selectedOptionToVS = "Method -> VS";
        private bool _executeCommandOnClick = true;
        private readonly DTE2 _dte;
        private DiffUtility.DiffContext _diffContext;
        private static bool _isWebViewInFocus;
        private IntPtr _hwndSource; // Handle for the window
        private bool _isClipboardListenerRegistered;
        private string _lastClipboardText; // Tracks last processed clipboard content
        private string _currentClipboardText; // Tracks clipboard content for current burst of events
        private bool _isOpeningCodeWindow = false;
        private bool _isProcessingClipboardAction = false;

        // Windows API declarations
        private const int WM_CLIPBOARDUPDATE = 0x031D;

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AddClipboardFormatListener(IntPtr hwnd);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool RemoveClipboardFormatListener(IntPtr hwnd);

        public ChatWindow()
        {
            InitializeComponent();
            var dummy = typeof(HandyControl.Controls.Window);
            UrlSelector.ItemsSource = _urlOptions;
            _dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            _diffContext = null;
            _isWebViewInFocus = false;
            _isClipboardListenerRegistered = false;

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

            ThemeUtility.CurrentTheme = ApplicationTheme.Light; // Default theme until saving is implemented
            //if (Enum.TryParse(Settings.Default.ApplicationTheme, out ApplicationTheme theme))
            //{
            //    //Update the theme
            //ThemeManager.Current.ApplicationTheme = savedTheme;
            //}
        }

        private void ChatWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // Get the window handle
            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource != null)
            {
                _hwndSource = hwndSource.Handle;
                hwndSource.AddHook(WndProc);
                _isClipboardListenerRegistered = AddClipboardFormatListener(_hwndSource);
                if (!_isClipboardListenerRegistered)
                {
                    WebViewUtilities.Log("Failed to register clipboard listener.");
                }
            }
        }

        private void ChatWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            // Clean up clipboard listener
            if (_isClipboardListenerRegistered && _hwndSource != IntPtr.Zero)
            {
                RemoveClipboardFormatListener(_hwndSource);
                _isClipboardListenerRegistered = false;
            }

            var hwndSource = PresentationSource.FromVisual(this) as HwndSource;
            if (hwndSource != null)
            {
                hwndSource.RemoveHook(WndProc);
            }
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_CLIPBOARDUPDATE && _isWebViewInFocus)
            {
                // Clipboard changed and WebView is in focus
                //HandleClipboardChange();
            }
            return IntPtr.Zero;
        }

        //Not used for now until the multiple diff error is fixed
        private async void HandleClipboardChange()
        {
            // A single, robust lock to prevent re-entrancy and race conditions
            if (_isProcessingClipboardAction)
            {
                return;
            }

            // Run on UI thread
            await Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Get clipboard text
                    string clipboardText = null;
                    if (Clipboard.ContainsText())
                    {
                        clipboardText = Clipboard.GetText();
                    }

                    // Ignore if there's no text or if it's the same as the last processed text
                    if (string.IsNullOrEmpty(clipboardText) || clipboardText == _lastClipboardText)
                    {
                        return;
                    }

                    // Prevent a clipboard change from being processed while a diff view is open
                    if (_diffContext != null)
                    {
                        MessageBox.Show("Clipboard change ignored: Diff view is open. Please accept or decline the changes first.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // Check if WebView is focused and clipboard write was programmatic
                    if (!_isWebViewInFocus || !await WebViewUtilities.IsProgrammaticCopyAsync(ChatWebView))
                    {
                        return; // Ignore if not focused or not a programmatic copy (e.g., Ctrl+C)
                    }

                    // Set the lock BEFORE starting the background operation
                    _isProcessingClipboardAction = true; 

                    // Process clipboard text
                    PasteButton_ClickLogic(clipboardText);

                    // Update _lastClipboardText immediately to prevent reprocessing this specific text
                    // This is safe to do here now because the _isProcessingClipboardAction lock is active.
                    _lastClipboardText = clipboardText;

                }
                catch (Exception ex)
                {
                    WebViewUtilities.Log($"Clipboard change handling error: {ex.Message}");
                    // Ensure the lock is released on error
                    _isProcessingClipboardAction = false; 
                }
                // Note: The lock is NOT released in a `finally` block here.
                // It will be released by the methods that conclude the action (Accept, Decline, or an abort in PasteButton_ClickLogic).
            });
        }        

        
        private void PasteButton_ClickLogic(string aiCode)
        {
            // Check if a diff window is already open. The new _isProcessingClipboardAction handles the race condition.
            if (_diffContext != null)
            {
                WebViewUtilities.Log("PasteButton_ClickLogic: Diff window already open. Aborting.");
                _isProcessingClipboardAction = false; // Release the lock
                return;
            }

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

                if (!(bool)AutoDiffToggle.IsChecked)
                {
                    // Use _isOpeningCodeWindow as a sub-lock for just this window, which is fine
                    _isOpeningCodeWindow = true;
                    try
                    {
                        selectedItem = CodeSelectionUtilities.ShowCodeReplacementWindow(_dte, activeDocument, ThemeUtility.CurrentTheme);
                        if (selectedItem == null)
                        {
                            abortAction(); // User cancelled, so abort and release the main lock
                            return;
                        }
                    }
                    finally
                    {
                        _isOpeningCodeWindow = false;
                    }
                }

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
            if (_executeCommandOnClick)
            {
                await WebViewUtilities.ExecuteCommandAsync(_selectedOptionToVS, _dte, ChatWebView, _webViewDataFolder);
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

        private async void MenuItemToAI_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string option)
            {
                _selectedOptionToAI = option;
                VSToAISplitButton.Content = option;
                await WebViewUtilities.ExecuteCommandAsync(option, _dte, ChatWebView, _webViewDataFolder);
            }
        }

        private async void MenuItemToVS_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string option)
            {
                _selectedOptionToVS = option;
                AIToVSSplitButton.Content = option;
                await WebViewUtilities.ExecuteCommandAsync(option, _dte, ChatWebView, _webViewDataFolder);
            }
        }

        private async void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (_dte == null)
            {
                MessageBox.Show("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string aiCode = await WebViewUtilities.RetrieveSelectedTextFromWebViewAsync(ChatWebView);


            if (aiCode == null || aiCode == "null" || string.IsNullOrEmpty(aiCode))
            {
                aiCode = Clipboard.GetText(); // Fallback to clipboard text if WebView retrieval fails
                //Optional: check again if aicode is null or empty after clipboard retrieval
                WebViewUtilities.Log("PasteButton_Click: Retrieved code from clipboard as WebView retrieval failed.");
            }

            PasteButton_ClickLogic(aiCode);
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string aiCodeFullFile = null;
            var contextToClose = _diffContext;
            var documentToModify = _dte.Documents.Item(contextToClose.ActiveDocumentPath);

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
                BackupUtilities.CreateSolutionBackup(_dte, _backupsFolder);
            }
            
            // Save the document after making the backup
            documentToModify?.Save();
            WebViewUtilities.Log($"ApplyChanges: Successfully saved '{contextToClose.ActiveDocumentPath}'.");

            // Scroll to the added code section start
            if (contextToClose?.ActiveDocumentPath != null && contextToClose.NewCodeStartIndex >= 0)
            {
                try
                {
                    var textDocument = (TextDocument)documentToModify.Object("TextDocument");
                    var selection = textDocument.Selection;
                    var editPoint = textDocument.CreateEditPoint(textDocument.StartPoint);

                    // Move to the character index and get the line and column
                    editPoint.MoveToAbsoluteOffset(contextToClose.NewCodeStartIndex + 1); // +1 because DTE uses 1-based indexing
                    int line = editPoint.Line;
                    int column = editPoint.LineCharOffset;

                    // Move the cursor to the start of the new code
                    selection.MoveTo(line, column);
                    selection.ActivePoint.TryToShow(vsPaneShowHow.vsPaneShowTop, null); // Scroll to top of the view
                    WebViewUtilities.Log($"Scrolled to line {line}, column {column} at index {contextToClose.NewCodeStartIndex}.");
                }
                catch (Exception ex)
                {
                    WebViewUtilities.Log($"Error scrolling to index {contextToClose.NewCodeStartIndex}: {ex.Message}");
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

            // First, close the existing diff view. This will invalidate the old _diffContext.ActiveDocument reference.
            DiffUtility.CloseDiffAndReset(_diffContext);

            // Now that the diff view is closed, we must get a fresh handle to the document.
            var activeDocument = _dte.Documents.Item(originalDocPath);
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

            // Show the code replacement window. This now uses a valid Document object, which prevents the freeze.
            var selectedItem = CodeSelectionUtilities.ShowCodeReplacementWindow(_dte, activeDocument, ThemeUtility.CurrentTheme);
            if (selectedItem == null)
            {
                // User cancelled the selection. Reset UI and state.
                UpdateButtonsForDiffView(false);
                _isProcessingClipboardAction = false;
                _lastClipboardText = null;
                return;
            }

            // Create a new context for the new diff view, following the pattern in PasteButton_ClickLogic.
            var newDiffContext = new DiffUtility.DiffContext { };

            // Calculate the modified code based on user's selection, passing the new context to be populated.
            string modifiedCode = StringUtil.ReplaceOrAddCode(_dte, currentCode, aiCode, activeDocument, selectedItem, newDiffContext);

            // Open a new diff view and assign the fully populated context to our member field.
            _diffContext = DiffUtility.OpenDiffView(activeDocument, currentCode, modifiedCode, aiCode, newDiffContext);

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

        private void ErrorToAISplitButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for future implementation
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

            var solutionBackupsFolder = Path.Combine(_backupsFolder, BackupUtilities.GetUniqueSolutionFolder(_dte));
            var restoreWindow = new RestoreSelectionWindow(_dte, solutionBackupsFolder);
            bool? result = restoreWindow.ShowDialog();
            if (result == true && restoreWindow.SelectedBackup != null)
            {
                string solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
                if (BackupUtilities.RestoreSolution(_dte, restoreWindow.SelectedBackup.FolderPath, solutionDir))
                {
                    MessageBox.Show("Solution restored successfully.", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
        }

        private void SaveBackupButton_Click(object sender, RoutedEventArgs e)
        {
            string path = BackupUtilities.CreateSolutionBackup(_dte, _backupsFolder);

            if (path != null)
            {
                string folderName = Path.GetDirectoryName(path);
                MessageBox.Show($"Backup created successfully: {folderName}", "Success");
            }
        }

        private void ButtonConfig_Click(object sender, RoutedEventArgs e) => PopupConfig.IsOpen = true;

        private void ButtonSkins_OnClick(object sender, RoutedEventArgs e)
        {
            if (e.OriginalSource is Button button)
            {
                if (button.Tag is ApplicationTheme themeTag)
                {
                    ThemeManager.Current.ApplicationTheme = themeTag;
                    ThemeUtility.CurrentTheme = themeTag; // Update the current theme variable
                    //Settings.Default.ApplicationTheme = themeTag.ToString();
                    //Settings.Default.Save();
                }
            }
        }
    }
}