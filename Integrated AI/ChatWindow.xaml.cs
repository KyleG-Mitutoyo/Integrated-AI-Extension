using EnvDTE;
using EnvDTE80;
using HandyControl.Controls;
using Integrated_AI.Models;
using Integrated_AI.Utilities;
using Microsoft.VisualStudio.Shell;
using Microsoft.Web.WebView2.Core;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using MessageBox = System.Windows.MessageBox;

namespace Integrated_AI
{
    public partial class ChatWindow : UserControl
    {
        private readonly List<UrlOption> _urlOptions = new List<UrlOption>
        {
            new UrlOption { DisplayName = "Grok", Url = "https://grok.com" },
            new UrlOption { DisplayName = "Google AI Studio", Url = "https://aistudio.google.com" },
            new UrlOption { DisplayName = "ChatGPT", Url = "https://chatgpt.com" }
        };

        private readonly WebViewUtility _webViewUtility;
        private readonly CodeExtractionUtility _codeExtractionUtility;
        private readonly DiffUtility _diffUtility;
        private readonly string _userDataFolder;
        private readonly DTE2 dte;
        private string _selectedOption = "Code -> AI";
        private bool _executeCommandOnClick = true;

        public ChatWindow()
        {
            InitializeComponent();
            dte = (DTE2)Package.GetGlobalService(typeof(DTE));
            _userDataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "AIChatExtension",
                Environment.UserName);

            _webViewUtility = new WebViewUtility(ChatWebView, _userDataFolder, _urlOptions);
            _codeExtractionUtility = new CodeExtractionUtility(dte, _userDataFolder);
            _diffUtility = new DiffUtility(dte);

            UrlSelector.ItemsSource = _urlOptions;
            InitializeWebView2Async();
        }

        private async void InitializeWebView2Async()
        {
            await _webViewUtility.InitializeAsync();
            ChatWebView.CoreWebView2InitializationCompleted += _webViewUtility.HandleInitializationCompleted;
            ChatWebView.NavigationCompleted += _webViewUtility.HandleNavigationCompleted;

            if (_urlOptions.Any())
            {
                UrlSelector.SelectedIndex = 0;
            }
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
            string relativePath = _codeExtractionUtility.GetRelativePath();
            var result = await _codeExtractionUtility.ExtractCodeAsync(option, relativePath, LoggingUtility.Log);
            if (result.HasValue)
            {
                await _webViewUtility.InjectTextAsync(result.Value.Text, result.Value.SourceDescription, LoggingUtility.Log);
            }
        }

        private void PasteButton_Click(object sender, RoutedEventArgs e)
        {
            string aiCode = GetAICodeFromWebView();
            string currentCode = _diffUtility.GetActiveDocumentText();
            _diffUtility.OpenDiffView(currentCode, aiCode);

            PasteButton.Visibility = Visibility.Collapsed;
            AcceptButton.Visibility = Visibility.Visible;
            DeclineButton.Visibility = Visibility.Visible;
        }

        private void AcceptButton_Click(object sender, RoutedEventArgs e)
        {
            string aiCode = _diffUtility.GetAICode();
            if (!string.IsNullOrEmpty(aiCode))
            {
                _diffUtility.ApplyChanges(aiCode);
            }
            PasteButton.Visibility = Visibility.Visible;
            AcceptButton.Visibility = Visibility.Collapsed;
            DeclineButton.Visibility = Visibility.Collapsed;
        }

        private void DeclineButton_Click(object sender, RoutedEventArgs e)
        {
            _diffUtility.CloseDiffAndReset();
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
            // TODO: Implement error handling
        }
    }
}