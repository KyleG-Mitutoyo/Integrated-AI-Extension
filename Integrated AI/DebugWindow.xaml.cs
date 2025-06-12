// Integrated AI/DebugContextWindow.xaml.cs
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