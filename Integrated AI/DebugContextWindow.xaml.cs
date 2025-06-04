// Integrated AI/DebugContextWindow.xaml.cs
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
    }
}