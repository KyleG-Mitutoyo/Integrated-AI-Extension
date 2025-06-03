using EnvDTE;
using EnvDTE80;
using HandyControl.Controls;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using Integrated_AI.Utilities;
using MessageBox = System.Windows.MessageBox;
using HandyControl.Tools.Extension;

namespace Integrated_AI
{
    public partial class ChatWindow : UserControl
    {
        // Existing fields...
        public List<UrlOption> _urlOptions = new List<UrlOption>
        {
            new UrlOption { DisplayName = "Grok", Url = "https://grok.com" },
            new UrlOption { DisplayName = "Google AI Studio", Url = "https://aistudio.google.com" },
            new UrlOption { DisplayName = "ChatGPT", Url = "https://chatgpt.com" }
        };

        private readonly string _userDataFolder;
        private string _selectedOption = "Code -> AI";
        private bool _executeCommandOnClick = true;
        private readonly DTE2 _dte;
        private DiffUtility.DiffContext _diffContext;
        private static bool _isWebViewInFocus;
        private IntPtr _hwndSource; // Handle for the window
        private bool _isClipboardListenerRegistered;

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

            _userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIChatExtension",
                Environment.UserName);
            Directory.CreateDirectory(_userDataFolder);

            // Initialize WebView2
            ChatWindowUtilities.InitializeWebView2Async(ChatWebView, _userDataFolder, _urlOptions, UrlSelector);

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
                    ChatWindowUtilities.Log("Failed to register clipboard listener.");
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
                    // Check if WebView is focused and clipboard write was programmatic
                    if (!_isWebViewInFocus || !await ChatWindowUtilities.IsProgrammaticCopyAsync(ChatWebView))
                    {
                        //MessageBox.Show("Clipboard change ignored: WebView not focused or not a programmatic copy.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        return; // Ignore if not focused or not a programmatic copy (e.g., Ctrl+C)
                    }

                    // Get clipboard text
                    if (Clipboard.ContainsText())
                    {
                        string clipboardText = Clipboard.GetText();
                        if (!string.IsNullOrEmpty(clipboardText))
                        {
                            // Trigger the PasteButton_Click logic
                            await PasteButton_ClickLogic(clipboardText);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ChatWindowUtilities.Log($"Clipboard change handling error: {ex.Message}");
                }
            });
        }

        private async Task PasteButton_ClickLogic(string aiCode)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

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

            string currentCode = DiffUtility.GetActiveDocumentText(_dte);
            _diffContext = DiffUtility.OpenDiffView(_dte, currentCode, aiCode);

            PasteButton.Visibility = Visibility.Collapsed;
            AcceptButton.Visibility = Visibility.Visible;
            DeclineButton.Visibility = Visibility.Visible;
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
                await ExecuteCommandAsync(_selectedOption);
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
                await ExecuteCommandAsync(option);
            }
        }

        private async Task ExecuteCommandAsync(string option)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_dte == null)
            {
                MessageBox.Show("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (_dte.ActiveDocument == null)
            {
                MessageBox.Show("No active document in Visual Studio.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string solutionPath = Path.GetDirectoryName(_dte.Solution.FullName);
            string filePath = _dte.ActiveDocument.FullName;
            string relativePath = FileUtil.GetRelativePath(solutionPath, filePath);

            string textToInject = null;
            string sourceDescription = "";

            if (option == "Code -> AI")
            {
                var textSelection = (TextSelection)_dte.ActiveDocument.Selection;
                if (string.IsNullOrEmpty(textSelection?.Text))
                {
                    MessageBox.Show("No text selected in Visual Studio.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                textToInject = textSelection.Text;
                sourceDescription = $"---{relativePath} (partial code block)---\n{textToInject}\n---End code---\n\n";
            }
            else if (option == "File -> AI")
            {
                var text = DiffUtility.GetActiveDocumentText(_dte);
                if (string.IsNullOrEmpty(text))
                {
                    MessageBox.Show("The active document is empty.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                string currentContent = await ChatWindowUtilities.RetrieveTextFromWebViewAsync(ChatWebView);
                if (currentContent != null && currentContent.Contains($"---{relativePath} (whole file contents)---"))
                {
                    MessageBox.Show("This file's contents have already been injected.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }
                textToInject = text;
                sourceDescription = $"---{relativePath} (whole file contents)---\n{textToInject}\n---End code---\n\n";
            }
            else if (option == "Function -> AI")
            {
                var functions = FunctionSelectionUtilities.GetFunctionsFromActiveDocument(_dte);
                if (!functions.Any())
                {
                    MessageBox.Show("No functions found in the active document.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                string recentFunctionsFilePath = Path.Combine(_userDataFolder, "recent_functions.txt");
                var functionSelectionWindow = new FunctionSelectionWindow(functions, recentFunctionsFilePath, relativePath);
                if (functionSelectionWindow.ShowDialog() == true && functionSelectionWindow.SelectedFunction != null)
                {
                    textToInject = functionSelectionWindow.SelectedFunction.FullCode;
                    sourceDescription = $"---{relativePath} (function: {functionSelectionWindow.SelectedFunction.DisplayName})---\n{textToInject}\n---End code---\n\n";
                }
                else
                {
                    return;
                }
            }

            if (textToInject != null)
            {
                await ChatWindowUtilities.InjectTextIntoWebViewAsync(ChatWebView, sourceDescription, option);
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

            string aiCode = await ChatWindowUtilities.RetrieveSelectedTextFromWebViewAsync(ChatWebView);
            if (aiCode == null || aiCode == "null" || string.IsNullOrEmpty(aiCode))
            {
                return;
            }

            await PasteButton_ClickLogic(aiCode);
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            string aiCodeToApply = null;
            var contextToClose = _diffContext;

            if (contextToClose?.TempAiFile != null && File.Exists(contextToClose.TempAiFile))
            {
                aiCodeToApply = FileUtil.GetEditedAiCode(contextToClose);
                if (string.IsNullOrEmpty(aiCodeToApply))
                {
                    ChatWindowUtilities.Log("AcceptButton_Click: AI code is empty, proceeding to apply empty content.");
                }
            }
            else
            {
                ChatWindowUtilities.Log("AcceptButton_Click: No valid diff context or AI temp file.");
            }

            if (contextToClose != null)
            {
                DiffUtility.CloseDiffAndReset(contextToClose);
                _diffContext = null;
            }

            if (aiCodeToApply != null && _dte != null)
            {
                DiffUtility.ApplyChanges(_dte, aiCodeToApply);
            }
            else if (aiCodeToApply == null && contextToClose != null)
            {
                ChatWindowUtilities.Log("AcceptButton_Click: No AI code to apply.");
            }
            else if (_dte == null)
            {
                MessageBox.Show("Visual Studio services (DTE) unavailable. Cannot apply changes.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log("AcceptButton_Click: DTE service is null.");
            }

            PasteButton.Visibility = Visibility.Visible;
            AcceptButton.Visibility = Visibility.Collapsed;
            DeclineButton.Visibility = Visibility.Collapsed;
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_diffContext != null)
            {
                DiffUtility.CloseDiffAndReset(_diffContext);
                _diffContext = null;
            }

            PasteButton.Visibility = Visibility.Visible;
            AcceptButton.Visibility = Visibility.Collapsed;
            DeclineButton.Visibility = Visibility.Collapsed;
        }

        private void ErrorToAISplitButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for future implementation
        }

        public class UrlOption
        {
            public string DisplayName { get; set; }
            public string Url { get; set; }
        }
    }
}