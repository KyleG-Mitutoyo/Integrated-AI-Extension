using EnvDTE;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Window = System.Windows.Window;
using HandyControl.Controls;

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
        private List<string> _recentFunctions;

        public FunctionSelectionWindow(IEnumerable<FunctionItem> functions, string recentFunctionsFilePath)
        {
            InitializeComponent();
            var dummy = typeof(HandyControl.Controls.Window); // Required for HandyControl XAML compilation
            _recentFunctionsFilePath = recentFunctionsFilePath;
            LoadRecentFunctions();
            PopulateFunctionList(functions);
        }

        private void LoadRecentFunctions()
        {
            _recentFunctions = new List<string>();
            if (File.Exists(_recentFunctionsFilePath))
            {
                _recentFunctions = File.ReadAllLines(_recentFunctionsFilePath).Take(3).ToList();
            }
        }

        private void PopulateFunctionList(IEnumerable<FunctionItem> functions)
        {
            var functionList = functions.ToList();
            var items = new List<FunctionItem>();

            // Add recent functions first, if they still exist in the current document
            foreach (var recent in _recentFunctions)
            {
                var matchingFunction = functionList.FirstOrDefault(f => f.DisplayName == recent);
                if (matchingFunction != null)
                {
                    items.Add(matchingFunction);
                    functionList.Remove(matchingFunction); // Remove to avoid duplicates
                }
            }

            // Add separator if there are recent functions
            if (items.Any())
            {
                items.Add(new FunctionItem { ListBoxDisplayName = "----- All Functions -----", FullName = "All the functions within the current file" });
            }

            // Add remaining functions
            items.AddRange(functionList);
            FunctionListBox.ItemsSource = items;
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (FunctionListBox.SelectedItem is FunctionItem selected && !selected.DisplayName.StartsWith("-----"))
            {
                SelectedFunction = selected;
                UpdateRecentFunctions(selected.DisplayName);
                DialogResult = true;
                Close();
            }
        }

        private void FunctionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FunctionListBox.SelectedItem is FunctionItem selected && !selected.DisplayName.StartsWith("-----"))
            {
                SelectedFunction = selected;
                UpdateRecentFunctions(selected.DisplayName);
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void UpdateRecentFunctions(string functionName)
        {
            if (string.IsNullOrEmpty(functionName) || functionName.StartsWith("-----")) return;

            _recentFunctions.Remove(functionName); // Remove if already exists to avoid duplicates
            _recentFunctions.Insert(0, functionName); // Add to top
            _recentFunctions = _recentFunctions.Take(3).ToList(); // Keep only top 3
            File.WriteAllLines(_recentFunctionsFilePath, _recentFunctions);
        }
    }
}