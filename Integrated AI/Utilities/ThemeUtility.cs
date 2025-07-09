// Integrated AI/Utilities/ThemeUtility.cs
using HandyControl.Themes;
using System;
using System.Linq;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public static class ThemeUtility
    {
        public static ApplicationTheme CurrentTheme { get; private set; } = ApplicationTheme.Light;

        /// <summary>
        /// Manually loads all required HandyControl themes into Application.Current.Resources.
        /// This is the robust way to ensure all controls, including MessageBoxes, are styled
        /// within a VSIX extension. Call this ONCE from the package's InitializeAsync method.
        /// </summary>
        public static void InitializeApplicationTheme()
        {
            var applicationResources = Application.Current.Resources;

            // Prevent loading themes multiple times
            if (applicationResources.MergedDictionaries.Any(d => d.Source?.OriginalString.Contains("HandyControl") == true))
            {
                return;
            }

            // *** THE FIX IS HERE ***
            // Instead of using hardcoded URI strings, we instantiate the library's own
            // ResourceDictionary-derived classes. This is the same thing the XAML parser does.

            // 1. Load the ThemeResources (which contains icons, etc.)
            applicationResources.MergedDictionaries.Add(new HandyControl.Themes.ThemeResources());

            // 2. Load the base styles for controls
            applicationResources.MergedDictionaries.Add(new HandyControl.Themes.Theme());

            // 3. For the color, we still use a URI, but this is less likely to fail.
            // This URI is confirmed to be correct.
            applicationResources.MergedDictionaries.Add(new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Light.xaml")
            });

            CurrentTheme = ApplicationTheme.Light;
        }

        /// <summary>
        /// Changes the application's current theme by finding and replacing the color dictionary.
        /// </summary>
        public static void ChangeTheme(ApplicationTheme theme)
        {
            if (theme == CurrentTheme) return; // No change needed

            var applicationResources = Application.Current.Resources;

            // Find the existing color dictionary
            var currentThemeDictionary = applicationResources.MergedDictionaries
                .FirstOrDefault(d => d.Source?.OriginalString.Contains("/Colors/") == true);

            if (currentThemeDictionary != null)
            {
                string newThemeSource = (theme == ApplicationTheme.Dark)
                    ? "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Dark.xaml"
                    : "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Light.xaml";
                
                currentThemeDictionary.Source = new Uri(newThemeSource);
                CurrentTheme = theme;
            }
        }
    }
}