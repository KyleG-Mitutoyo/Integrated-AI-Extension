using HandyControl.Themes;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public static class ThemeUtility
    {
        public static ApplicationTheme CurrentTheme { get; set; } = ApplicationTheme.Light;

        public static void ApplyTheme(Window window)
        {
            // Determine which specific color scheme to load (Light or Dark)
            string colorThemeSource = "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Light.xaml";
            if (CurrentTheme == ApplicationTheme.Dark)
            {
                colorThemeSource = "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Dark.xaml";
            }

            // 3. Add the selected color scheme resource
            window.Resources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new System.Uri(colorThemeSource)
            });
        }
    }
}