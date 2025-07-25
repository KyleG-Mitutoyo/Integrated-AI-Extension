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
using System;
using System.Windows;
using System.Windows.Controls; // Updated to include TextBox
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Window = HandyControl.Controls.Window;

namespace Integrated_AI
{
    public partial class LogWindow : ThemedWindow
    {
        public LogWindow(Popup popup)
        {
            InitializeComponent();
            NonClientAreaBackground = Brushes.Transparent;

            // Populate with existing logs and subscribe to new ones
            LogTextBox.Text = LoggingService.GetAllLogs();
            // Subscribe to the Loaded event to scroll to end after UI is ready
            LogTextBox.Loaded += LogTextBox_Loaded;
            LoggingService.MessageLogged += OnMessageLogged;

            //Close the options menu so the window can focus
            popup.IsOpen = false;

            // Ensure we unsubscribe when the window is closed to prevent memory leaks
            this.Closed += (s, e) =>
            {
                LoggingService.MessageLogged -= OnMessageLogged;
                LogTextBox.Loaded -= LogTextBox_Loaded; // Unsubscribe from Loaded event
            };
        }

        // Event handler for when LogTextBox is fully loaded
        private void LogTextBox_Loaded(object sender, RoutedEventArgs e)
        {
            LogTextBox.ScrollToEnd();
        }

        private void OnMessageLogged(object sender, LogEventArgs e)
        {
            // This event can be fired from any thread.
            // We must use the Dispatcher to update the UI on the UI thread.
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => OnMessageLogged(sender, e));
                return;
            }

            LogTextBox.AppendText(Environment.NewLine + e.NewMessage);
            LogTextBox.ScrollToEnd();
        }

        private void CopyButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Clipboard.SetText(LogTextBox.Text);
                LoggingService.Log("Log content copied to clipboard.");
            }
            catch (Exception ex)
            {
                LoggingService.Log($"Error copying log content: {ex.Message}");
                ThemedMessageBox.Show(this, "Failed to copy log content to clipboard.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}