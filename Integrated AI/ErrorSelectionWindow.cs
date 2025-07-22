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

using HandyControl.Controls;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using EnvDTE80;
using Window = HandyControl.Controls.Window;
using MessageBox = HandyControl.Controls.MessageBox;

namespace Integrated_AI
{
    public partial class ErrorSelectionWindow : Window
    {
        public class ErrorItem
        {
            public string Description { get; set; }
            public string FullFile { get; set; }
            public string File { get; set; }
            public int Line { get; set; }
            public EnvDTE80.ErrorItem DteErrorItem { get; set; }
            public string ListBoxDisplayName => $"{File}, Line {Line}: {Description}";
        }

        public ErrorItem SelectedError { get; private set; }

        public ErrorSelectionWindow(List<ErrorSelectionWindow.ErrorItem> dteErrors)
        {
            InitializeComponent();
            NonClientAreaBackground = Brushes.Transparent;
            var errors = dteErrors;

            ErrorListBox.ItemsSource = errors;
        }

        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            SelectItemLogic();
        }

        private void ErrorListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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
            if (ErrorListBox.SelectedItem is ErrorItem selected)
            {
                SelectedError = selected;
                DialogResult = true;
                Close();
            }
        }
    }
}