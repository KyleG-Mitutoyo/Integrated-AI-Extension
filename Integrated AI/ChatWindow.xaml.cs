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
using static Integrated_AI.ChatWindowUtilities;
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
                            PasteButton_ClickLogic(clipboardText);
                        }
                    }
                }
                catch (Exception ex)
                {
                    ChatWindowUtilities.Log($"Clipboard change handling error: {ex.Message}");
                }
            });
        }

        
        private void PasteButton_ClickLogic(string aiCode)
        {
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

                var contexts = ChatWindowUtilities.GetContextsForFile(activeDocument.FullName);
                if (!contexts.Any())
                {
                    //Warning: No contexts found for the active document. Using null for the context.
                }

                string currentCode = DiffUtility.GetActiveDocumentText(_dte);
                string modifiedCode = currentCode;

                var context = FindMatchingContext(contexts, aiCode);
                modifiedCode = ReplaceCodeInDocument(modifiedCode, context, aiCode, _dte);

                _diffContext = DiffUtility.OpenDiffView(_dte, currentCode, modifiedCode, aiCode);

                //If opening the diff view had a problem, we don't want to show the accept/decline buttons.
                if (_diffContext == null)
                {
                    return;
                }

                UpdateDiffButtons(true);
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
                await ChatWindowUtilities.ExecuteCommandAsync(_selectedOption, _dte, ChatWebView, _userDataFolder);
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
                await ChatWindowUtilities.ExecuteCommandAsync(option, _dte, ChatWebView, _userDataFolder);
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
                    ChatWindowUtilities.Log("AcceptButton_Click: AI code is empty.");
                }
            }
            else
            {
                ChatWindowUtilities.Log("AcceptButton_Click: No valid diff context or temp file.");
            }

            if (contextToClose != null)
            {
                DiffUtility.CloseDiffAndReset(contextToClose);
                _diffContext = null;
            }

            // Update context and apply changes
            if (aiCodeFullFile != null && _dte != null)
            {
                var activeDocument = _dte.ActiveDocument;
                if (activeDocument != null)
                {
                    var contexts = ChatWindowUtilities.GetContextsForFile(activeDocument.FullName);
                    var context = FindMatchingContext(contexts, contextToClose.AICodeBlock);
                    if (context != null)
                    {
                        // Calculate new line numbers based on the single code block
                        int newStartLine = context.StartLine;
                        int lineCount = contextToClose.AICodeBlock.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).Length;
                        int newEndLine = newStartLine + lineCount - 1;

                        ChatWindowUtilities.UpdateContextData(context, newStartLine, newEndLine, contextToClose.AICodeBlock);
                        ChatWindowUtilities.IncrementContextAges(activeDocument.FullName, context);
                        ChatWindowUtilities.Log($"Updated context: Type={context.Type}, File={context.FilePath}, Lines={newStartLine}-{newEndLine}, AgeCounter={context.AgeCounter}");
                    }
                    else
                    {
                        ChatWindowUtilities.Log("No matching context found for AI code; applied at cursor position.");
                    }

                    DiffUtility.ApplyChanges(_dte, aiCodeFullFile);
                }
            }

            UpdateDiffButtons(false);
        }

        private void ChooseButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Coming soon, be patient!");
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            if (_diffContext != null)
            {
                DiffUtility.CloseDiffAndReset(_diffContext);
                _diffContext = null;
            }

            UpdateDiffButtons(false);
        }

        private void ErrorToAISplitButton_Click(object sender, RoutedEventArgs e)
        {
            // Placeholder for future implementation
        }

        private void UpdateDiffButtons(bool showDiffButons)
        {
            if (showDiffButons)
            {
                PasteButton.Visibility = Visibility.Collapsed;
                AcceptButton.Visibility = Visibility.Visible;
                ChooseButton.Visibility = Visibility.Visible;
                DeclineButton.Visibility = Visibility.Visible;
            }

            else
            {
                PasteButton.Visibility = Visibility.Visible;
                AcceptButton.Visibility = Visibility.Collapsed;
                ChooseButton.Visibility = Visibility.Collapsed;
                DeclineButton.Visibility = Visibility.Collapsed;
            }
        }

        // Integrated AI/ChatWindow.xaml.cs
        private void DebugButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.Generic.BeginInvoke(() =>
            {
                // Get all contexts (not just for the active document)
                var allContexts = ChatWindowUtilities.GetAllSentContexts(); // New method needed
                var contextText = FormatContextList(allContexts);

                var debugWindow = new DebugContextWindow(contextText)
                {
                    //Owner = this // Center relative to ChatWindow
                };
                debugWindow.ShowDialog();
            });
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Coming soon!");
        }
    }
}