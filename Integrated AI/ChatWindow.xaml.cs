using EnvDTE;
using EnvDTE80;
using HandyControl.Controls;
using HandyControl.Tools.Extension;
using Integrated_AI.Utilities;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Remoting.Contexts;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
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
        private string _selectedOption = "Method -> AI";
        private bool _executeCommandOnClick = true;
        private readonly DTE2 _dte;
        private DiffUtility.DiffContext _diffContext;
        private static bool _isWebViewInFocus;
        private IntPtr _hwndSource; // Handle for the window
        private bool _isClipboardListenerRegistered;
        private string _lastClipboardText; // Tracks last processed clipboard content
        private string _currentClipboardText; // Tracks clipboard content for current burst of events

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
                HandleClipboardChange();
            }
            return IntPtr.Zero;
        }

        private async void HandleClipboardChange()
        {
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
                        // Check if this is a duplicate event in the current burst
                        if (!string.IsNullOrEmpty(clipboardText) && clipboardText == _currentClipboardText)
                        {
                            return; // Ignore duplicate event in this burst
                        }
                        // Update current clipboard text for this burst
                        if (!string.IsNullOrEmpty(clipboardText))
                        {
                            _currentClipboardText = clipboardText;
                        }
                    }

                    // Prevent a clipboard change from being processed while a diff view is open
                    if (_diffContext != null)
                    {
                        MessageBox.Show("Clipboard change ignored: Diff view is open. Please accept or decline the changes first.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        // Track ignored content to prevent reprocessing later
                        if (!string.IsNullOrEmpty(clipboardText))
                        {
                            _lastClipboardText = clipboardText;
                        }
                        return;
                    }

                    // Check if WebView is focused and clipboard write was programmatic
                    if (!_isWebViewInFocus || !await WebViewUtilities.IsProgrammaticCopyAsync(ChatWebView))
                    {
                        //MessageBox.Show("Clipboard change ignored: WebView not focused or not a programmatic copy.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        // Track ignored content to prevent reprocessing later
                        if (!string.IsNullOrEmpty(clipboardText))
                        {
                            _lastClipboardText = clipboardText;
                        }
                        return; // Ignore if not focused or not a programmatic copy (e.g., Ctrl+C)
                    }

                    // Process clipboard text if valid and not previously processed
                    if (!string.IsNullOrEmpty(clipboardText) && clipboardText != _lastClipboardText)
                    {
                        // Trigger the PasteButton_Click logic
                        PasteButton_ClickLogic(clipboardText);
                        // Update _lastClipboardText only if diff view was opened successfully
                        if (_diffContext != null)
                        {
                            _lastClipboardText = clipboardText;
                        }
                    }
                }
                catch (Exception ex)
                {
                    WebViewUtilities.Log($"Clipboard change handling error: {ex.Message}");
                }
                finally
                {
                    // Reset _currentClipboardText after processing this burst
                    _currentClipboardText = null;
                }
            });
        }        

        
        private void PasteButton_ClickLogic(string aiCode)
        {
            if(_diffContext != null)
            {
                WebViewUtilities.Log("PasteButton_ClickLogic: Diff window already open or opening. Aborting.");
                return;
            }

            ThreadHelper.Generic.BeginInvoke(() =>
            {
                if (_dte == null)
                {
                    MessageBox.Show("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                if (string.IsNullOrEmpty(aiCode))
                {
                    MessageBox.Show("No code retrieved from clipboard.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var activeDocument = _dte.ActiveDocument;
                if (activeDocument == null)
                {
                    MessageBox.Show("No active document.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                string currentCode = DiffUtility.GetDocumentText(activeDocument);
                string modifiedCode = currentCode;
                ChooseCodeWindow.ReplacementItem selectedItem = null;

                // If in "off" or "manual" mode, we need to bring up the selection window
                if ((bool)AutoDiffToggle.IsChecked)
                {
                    selectedItem = CodeSelectionUtilities.ShowCodeReplacementWindow(_dte, activeDocument);
                    if (selectedItem == null)
                    {
                        //MessageBox.Show("No code selection made.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        _lastClipboardText = null;
                        return;
                    }
                }

                modifiedCode = StringUtil.ReplaceOrAddCode(_dte, modifiedCode, aiCode, activeDocument, selectedItem);

                _diffContext = DiffUtility.OpenDiffView(activeDocument, currentCode, modifiedCode, aiCode);

                //If opening the diff view had a problem, we don't want to show the accept/decline buttons.
                if (_diffContext == null)
                {
                    return;
                }

                UpdateButtonsForDiffView(true);
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

        private async void SplitButton_Click(object sender, RoutedEventArgs e)
        {
            if (_executeCommandOnClick)
            {
                await WebViewUtilities.ExecuteCommandAsync(_selectedOption, _dte, ChatWebView, _webViewDataFolder);
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

        private async void MenuItem_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem menuItem && menuItem.Tag is string option)
            {
                _selectedOption = option;
                VSToAISplitButton.Content = option;
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
                return;
            }

            PasteButton_ClickLogic(aiCode);
        }

        // Integrated AI/ChatWindow.xaml.cs
        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string aiCodeFullFile = null;
            var contextToClose = _diffContext;

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

            //Make sure to use the ActiveDocument from the diffcontext rather than the current active document!
            if (aiCodeFullFile != null && _dte != null)
            {
                if (contextToClose.ActiveDocument != null)
                {
                    DiffUtility.ApplyChanges(_dte, contextToClose.ActiveDocument, aiCodeFullFile);
                }
            }

            // A backup of the solution files are created for every accept button click.
            BackupUtilities.CreateSolutionBackup(_dte, _backupsFolder);
            UpdateButtonsForDiffView(false);
            _lastClipboardText = null;
        }

        private void ChooseButton_Click(object sender, RoutedEventArgs e)
        {
            //First close the existing diff view to make a new one
            DiffUtility.CloseDiffAndReset(_diffContext);

            string currentCode = DiffUtility.GetDocumentText(_diffContext.ActiveDocument);
            string modifiedCode = currentCode;

            //null currentCode must mean the button was clicked without an active document
            if (currentCode == null)
            {
                UpdateButtonsForDiffView(false);
                //MessageBox.Show("No active document or unable to retrieve current code.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                _lastClipboardText = null;
                return;
            }

            //Choose an existing/new function or existing/new file manually
            // Shows the code replacement window and returns the selected item
            var selectedItem = CodeSelectionUtilities.ShowCodeReplacementWindow(_dte, _diffContext.ActiveDocument);
            if (selectedItem == null)
            {
                //MessageBox.Show("No code selection made.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                _lastClipboardText = null;
                return;
            }

            //We can use diffcontext existing items since it still exists for now
            modifiedCode = StringUtil.ReplaceOrAddCode(_dte, modifiedCode, _diffContext.AICodeBlock, _diffContext.ActiveDocument, selectedItem);
            _diffContext = DiffUtility.OpenDiffView(_diffContext.ActiveDocument, currentCode, modifiedCode, _diffContext.AICodeBlock);

            //If opening the diff view had a problem, reset the buttons.
            if (_diffContext == null)
            {
                UpdateButtonsForDiffView(false);
            }

            _lastClipboardText = null;
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_diffContext != null)
            {
                DiffUtility.CloseDiffAndReset(_diffContext);
                _diffContext = null;
            }

            UpdateButtonsForDiffView(false);
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
                ErrorToAISplitButton.Visibility = Visibility.Collapsed;
                PasteButton.Visibility = Visibility.Collapsed;
                AcceptButton.Visibility = Visibility.Visible;
                ChooseButton.Visibility = Visibility.Visible;
                DeclineButton.Visibility = Visibility.Visible;
            }

            else
            {
                VSToAISplitButton.Visibility = Visibility.Visible;
                ErrorToAISplitButton.Visibility = Visibility.Visible;
                PasteButton.Visibility = Visibility.Visible;
                AcceptButton.Visibility = Visibility.Collapsed;
                ChooseButton.Visibility = Visibility.Collapsed;
                DeclineButton.Visibility = Visibility.Collapsed;
            }
        }

        // Integrated AI/ChatWindow.xaml.cs
        private void DebugButton_Click(object sender, RoutedEventArgs e)
        {
            //Kept for future use, maybe to show debug info
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            var solutionBackupsFolder = Path.Combine(_backupsFolder, BackupUtilities.GetUniqueSolutionFolder(_dte));
            var restoreWindow = new RestoreSelectionWindow(solutionBackupsFolder);
            bool? result = restoreWindow.ShowDialog();
            if (result == true && restoreWindow.SelectedBackup != null)
            {
                string solutionDir = Path.GetDirectoryName(_dte.Solution.FullName);
                if (BackupUtilities.RestoreSolution(_dte, restoreWindow.SelectedBackup.FolderPath, solutionDir))
                {
                    System.Windows.MessageBox.Show("Solution restored successfully.", "Success", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
                }
            }
        }
    }
}