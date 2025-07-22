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

using Integrated_AI.Utilities;
using System.Windows;

namespace Integrated_AI
{
    public partial class DebugContextWindow : Window
    {
        public DebugContextWindow(string contextText)
        {
            InitializeComponent();
            ContextTextBox.Text = contextText;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        private void ClearButton_Click(object sender, RoutedEventArgs e)
        {
            //It used to clear the textbox
        }

        private void PopulateButton_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show("Just use VS to AI button once or twice to get the contexts nice.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}