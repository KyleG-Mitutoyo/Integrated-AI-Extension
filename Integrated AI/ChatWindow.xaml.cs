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

        public ChatWindow()
        {
            InitializeComponent();
            var dummy = typeof(HandyControl.Controls.Window); // Required for HandyControl XAML compilation
            UrlSelector.ItemsSource = _urlOptions;

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
            var dte = await ServiceProvider.GetGlobalServiceAsync(typeof(DTE)) as DTE2;
            if (dte == null)
            {
                MessageBox.Show("DTE service not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            if (dte.ActiveDocument == null)
            {
                MessageBox.Show("No active document in Visual Studio.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Get relative path
            string solutionPath = Path.GetDirectoryName(dte.Solution.FullName);
            string filePath = dte.ActiveDocument.FullName;
            string relativePath = FileUtil.GetRelativePath(solutionPath, filePath);

            string textToInject = null;
            string sourceDescription = "";

            if (option == "Code -> AI")
            {
                var textSelection = (TextSelection)dte.ActiveDocument.Selection;
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
                if (dte.ActiveDocument.Object("TextDocument") is TextDocument textDocument)
                {
                    var text = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
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
                else
                {
                    MessageBox.Show("Could not get text document from active document.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }
            else if (option == "Function -> AI")
            {
                var functions = FunctionSelectionUtilities.GetFunctionsFromActiveDocument(dte);
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