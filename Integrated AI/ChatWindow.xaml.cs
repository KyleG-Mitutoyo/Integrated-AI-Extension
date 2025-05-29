using EnvDTE;
using EnvDTE80;
using HandyControl.Controls;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Integrated_AI.Utilities;
using MessageBox = System.Windows.MessageBox;

namespace Integrated_AI
{
    public partial class ChatWindow : UserControl
    {
        public List<UrlOption> _urlOptions = new List<UrlOption>
        {
            new UrlOption { DisplayName = "Grok", Url = "https://grok.com" },
            new UrlOption { DisplayName = "Google AI Studio", Url = "https://aistudio.google.com" },
            new UrlOption { DisplayName = "ChatGPT", Url = "https://chatgpt.com" }
        };

        private readonly string _userDataFolder;
        private string _selectedOption = "Code -> AI";
        private bool _executeCommandOnClick = true;
        private readonly DTE2 _dte; // Renamed to _dte for consistency
        private DiffUtility.DiffContext _diffContext;

        public ChatWindow()
        {
            InitializeComponent();
            var dummy = typeof(HandyControl.Controls.Window); // Required for HandyControl XAML compilation
            UrlSelector.ItemsSource = _urlOptions;
            _dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            _diffContext = null;

            // Initialize user data folder
            _userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIChatExtension",
                Environment.UserName);
            Directory.CreateDirectory(_userDataFolder);

            // Initialize WebView2
            ChatWindowUtilities.InitializeWebView2Async(ChatWebView, _userDataFolder, _urlOptions, UrlSelector);
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

            // Get relative path
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
                    return; // User cancelled selection
                }
            }

            if (textToInject != null)
            {
                await ChatWindowUtilities.InjectTextIntoWebViewAsync(ChatWebView, sourceDescription, option);
            }
        }

        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            if (_dte == null)
            {
                MessageBox.Show("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string aiCode = GetAICodeFromWebView();
            string currentCode = DiffUtility.GetActiveDocumentText(_dte);
            _diffContext = DiffUtility.OpenDiffView(_dte, currentCode, aiCode);
            
            PasteButton.Visibility = Visibility.Collapsed;
            AcceptButton.Visibility = Visibility.Visible;
            DeclineButton.Visibility = Visibility.Visible;
        }

        private async void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string aiCodeToApply = null;
            var contextToClose = _diffContext;

            // 1. Retrieve AI code from temp file if available
            if (contextToClose?.TempAiFile != null && File.Exists(contextToClose.TempAiFile))
            {
                aiCodeToApply = FileUtil.GetAICode(contextToClose.TempAiFile);
                if (string.IsNullOrEmpty(aiCodeToApply))
                {
                    ChatWindowUtilities.Log("AcceptButton_Click: AI code is empty, proceeding to apply empty content.");
                }
            }
            else
            {
                ChatWindowUtilities.Log("AcceptButton_Click: No valid diff context or AI temp file.");
            }

            // 2. Close diff window and clean up
            if (contextToClose != null)
            {
                DiffUtility.CloseDiffAndReset(contextToClose);
                _diffContext = null;
            }

            // 3. Apply AI code to active document
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

            // 4. Reset button visibility
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

        private string GetAICodeFromWebView()
        {
            // TODO: Implement WebView2 JavaScript execution to extract AI-generated code
            return "/* Sample AI-generated code */\npublic void Example() {\n    Console.WriteLine(\"Hello, AI!\");\n}";
        }

        private void ErrortoAISplitButton_Click(object sender, RoutedEventArgs e)
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