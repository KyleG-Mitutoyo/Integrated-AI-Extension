using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using EnvDTE;
using HandyControl.Controls;
using Integrated_AI.Utilities;
using Window = System.Windows.Window;

namespace Integrated_AI
    {
        public partial class FunctionSelectionWindow : Window
        {
            public class FunctionItem
            {
                public string DisplayName { get; set; }
                public string ListBoxDisplayName { get; set; }
                public string FullName { get; set; }
                public CodeFunction Function { get; set; }
                public string FullCode { get; set; }
            }

            public FunctionItem SelectedFunction { get; private set; }
            private readonly string _recentFunctionsFilePath;
            private readonly List<string> _recentFunctions;

            public FunctionSelectionWindow(IEnumerable<FunctionItem> functions, string recentFunctionsFilePath, string openedFile)
            {
                InitializeComponent();
                var dummy = typeof(HandyControl.Controls.Window); // Required for HandyControl XAML compilation
                _recentFunctionsFilePath = recentFunctionsFilePath;
                _recentFunctions = FileUtil.LoadRecentFunctions(recentFunctionsFilePath);
                FunctionListBox.ItemsSource = CodeSelectionUtilities.PopulateFunctionList(functions, _recentFunctions, openedFile);
            }

            private void SelectButton_Click(object sender, RoutedEventArgs e)
            {
                if (FunctionListBox.SelectedItem is FunctionItem selected && !selected.ListBoxDisplayName.StartsWith("-----"))
                {
                    SelectedFunction = selected;
                    FileUtil.UpdateRecentFunctions(_recentFunctions, selected.DisplayName, _recentFunctionsFilePath);
                    DialogResult = true;
                    Close();
                }
            }

            private void FunctionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
            {
                if (FunctionListBox.SelectedItem is FunctionItem selected && !selected.ListBoxDisplayName.StartsWith("-----"))
                {
                    SelectedFunction = selected;
                    FileUtil.UpdateRecentFunctions(_recentFunctions, selected.DisplayName, _recentFunctionsFilePath);
                    DialogResult = true;
                    Close();
                }
            }

            private void CancelButton_Click(object sender, RoutedEventArgs e)
            {
                DialogResult = false;
                Close();
            }
        }
    }