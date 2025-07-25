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
using EnvDTE80;
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
    public partial class ChooseCodeWindow : ThemedWindow
    {
        public class ReplacementItem
        {
            public string DisplayName { get; set; }
            public string ListBoxDisplayName { get; set; }
            public string FullName { get; set; }
            public CodeFunction Function { get; set; } // Null for files or special options
            public string FilePath { get; set; } // Null for functions or new function
            public string Type { get; set; } // "function", "file", "new_function", "new_file", "opened_file"
            public string FullCode { get; set; } // Function code or empty for files
        }

        public ReplacementItem SelectedItem { get; private set; }

        public ChooseCodeWindow(DTE2 dte, Document activeDoc, string tempCurrentFile = null, string tempAiFile = null)
        {
            InitializeComponent();
            NonClientAreaBackground = Brushes.Transparent;

            var (functions, files) = CodeSelectionUtilities.PopulateReplacementLists(dte, activeDoc, tempCurrentFile, tempAiFile);
            FunctionListBox.ItemsSource = functions;
            FileListBox.ItemsSource = files;
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            ReplacementItem selected = FunctionListBox.SelectedItem as ReplacementItem ?? FileListBox.SelectedItem as ReplacementItem;
            if (selected != null && !selected.ListBoxDisplayName.StartsWith("-----"))
            {
                SelectedItem = selected;
                DialogResult = true;
                Close();
            }
        }

        private void FunctionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FunctionListBox.SelectedItem is ReplacementItem selected && !selected.ListBoxDisplayName.StartsWith("-----"))
            {
                SelectedItem = selected;
                DialogResult = true;
                Close();
            }
        }

        private void FileListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileListBox.SelectedItem is ReplacementItem selected && !selected.ListBoxDisplayName.StartsWith("-----"))
            {
                SelectedItem = selected;
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