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
            DiffUtility.DiffContext contextToClose = _diffContext; // Work with a copy for clarity if needed

            if (contextToClose != null && !string.IsNullOrEmpty(contextToClose.TempAiFile))
            {
                // 1. Get AI code from the temp file BEFORE closing the diff.
                aiCodeToApply = DiffUtility.GetAICode(contextToClose.TempAiFile);

                if (string.IsNullOrEmpty(aiCodeToApply) && contextToClose.TempAiFile != null && System.IO.File.Exists(contextToClose.TempAiFile))
                {
                    // GetAICode logs errors. If it returned empty but file existed, it means content was empty or read failed.
                    ChatWindowUtilities.Log("AcceptButton_Click: AI code retrieved from temp file is empty. Proceeding to apply empty content if intended.");
                    // If empty string is a valid "apply" operation (e.g., to clear the document), this is fine.
                    // If not, you might want to show a message and abort here. For now, we allow applying empty.
                }
            }
            else
            {
                ChatWindowUtilities.Log("AcceptButton_Click: No valid diff context or AI temp file path. Cannot get AI code.");
                // No code to apply, but UI should still be reset.
            }

            // 2. Close the diff window and clean up its resources (this deletes temp files).
            // This should always be done if Accept is clicked and there was a context.
            if (contextToClose != null)
            {
                DiffUtility.CloseDiffAndReset(contextToClose);
                _diffContext = null; // Very important: clear the stored context.
            }

            // 3. Apply changes to the CURRENTLY active document in VS (if AI code was retrieved).
            if (aiCodeToApply != null) // Check for null, empty string is a valid content to apply (clears document)
            {
                // Ensure _dte is available (it might have been null if ChatWindow opened before DTE was fully ready)
                if (_dte == null)
                {
                    // Attempt to re-acquire DTE if necessary (example, adjust per your actual DTE init)
                    // var vsPackage = this.Package as AsyncPackage; // If ChatWindow has access to the package
                    // if (vsPackage != null)
                    // {
                    //    _dte = await vsPackage.GetServiceAsync(typeof(SDTE)) as DTE2;
                    // }

                    if (_dte == null)
                    {
                        MessageBox.Show("Visual Studio services (DTE) are not available. Cannot apply changes.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        ChatWindowUtilities.Log("AcceptButton_Click: DTE service is null. Cannot apply changes.");
                        // Fall through to reset buttons.
                    }
                }

                if (_dte != null) // Proceed if DTE is available
                {
                    // At this point, dte.ActiveDocument should be the user's actual code file,
                    // not one of the (now deleted) temp files.
                    DiffUtility.ApplyChanges(_dte, aiCodeToApply);
                }
            }
            else if (contextToClose != null) // Log only if we expected to get code
            {
                ChatWindowUtilities.Log("AcceptButton_Click: AI code was null (possibly due to read error or no context); skipping ApplyChanges.");
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