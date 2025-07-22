// Integrated AI
// Copyright (C) 2025 Kyle Grubbs

// This program is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any other later version.

// This program is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.

// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <https://www.gnu.org/licenses/>.

using EnvDTE;
using HandyControl.Controls;
using HandyControl.Themes;
using Integrated_AI.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Window = HandyControl.Controls.Window;

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
            public TextPoint StartPoint { get; set; }
            public TextPoint EndPoint { get; set; }

        }

        public FunctionItem SelectedFunction { get; private set; }
        private readonly string _recentFunctionsFilePath;
        private readonly List<string> _recentFunctions;

        public FunctionSelectionWindow(IEnumerable<FunctionItem> functions, string recentFunctionsFilePath, string openedFile, bool showNewFunction)
        {
            InitializeComponent();
            NonClientAreaBackground = Brushes.Transparent;

            _recentFunctionsFilePath = recentFunctionsFilePath;
            _recentFunctions = FileUtil.LoadRecentFunctions(recentFunctionsFilePath);
            FunctionListBox.ItemsSource = CodeSelectionUtilities.PopulateFunctionList(functions, _recentFunctions, openedFile, showNewFunction);
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            SelectItemLogic();
        }

        private void FunctionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            SelectItemLogic();
        }


        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void SelectItemLogic()
        {
            if (FunctionListBox.SelectedItem is FunctionItem selected && !selected.ListBoxDisplayName.StartsWith("-----"))
            {
                SelectedFunction = selected;

                // Only update recent functions if it wasn't a new function
                if (selected.DisplayName != "New Function")
                {
                    FileUtil.UpdateRecentFunctions(_recentFunctions, selected.DisplayName, _recentFunctionsFilePath);
                }
                
                DialogResult = true;
                Close();
            }
        }
    }
}