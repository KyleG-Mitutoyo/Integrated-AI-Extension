using Integrated_AI.Utilities;
using System;
using System.Windows;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using Window = HandyControl.Controls.Window;

namespace Integrated_AI
{
    public partial class LogWindow : Window
    {
        public LogWindow(Popup popup)
        {
            InitializeComponent();
            NonClientAreaBackground = Brushes.Transparent;

            // Populate with existing logs and subscribe to new ones
            LogTextBox.Text = LoggingService.GetAllLogs();
            LogTextBox.ScrollToEnd();
            LoggingService.MessageLogged += OnMessageLogged;

            //Close the options menu so the window can focus
            popup.IsOpen = false;

            // Ensure we unsubscribe when the window is closed to prevent memory leaks
            this.Closed += (s, e) => LoggingService.MessageLogged -= OnMessageLogged;
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
                MessageBox.Show("Failed to copy log content to clipboard.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}