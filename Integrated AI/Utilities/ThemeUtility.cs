// Integrated AI/Utilities/ThemeUtility.cs
using HandyControl.Themes;
using Integrated_AI.Properties;
using System;
using System.Linq;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public static class ThemeUtility
    {
        public static ApplicationTheme CurrentTheme { get; private set; }

        /// <summary>
        /// Initializes the global theme by loading the user's saved preference.
        /// Call this ONCE from the package's InitializeAsync method.
        /// </summary>
        public static void InitializeApplicationTheme()
        {
            var applicationResources = Application.Current.Resources;
            
            // Prevent re-initialization
            if (applicationResources.MergedDictionaries.Any(d => d.Source?.OriginalString.Contains("HandyControl") == true))
            {
                return;
            }

            // Load base resources
            applicationResources.MergedDictionaries.Add(new HandyControl.Themes.ThemeResources());
            applicationResources.MergedDictionaries.Add(new HandyControl.Themes.Theme());

            // Load the saved theme from settings
            // Default to Dark theme if no setting is found or parsing fails
            ApplicationTheme savedTheme = ApplicationTheme.Dark;

            if (Enum.TryParse(Settings.Default.theme, out ApplicationTheme theme))
            {
                //Update the theme
                savedTheme = theme;
            }

            // Apply the loaded theme
            ChangeTheme(savedTheme, isInitializing: true);
        }

        /// <summary>
        /// Changes the application's current theme and saves the preference.
        /// </summary>
        public static void ChangeTheme(ApplicationTheme theme, bool isInitializing = false)
        {
            if (!isInitializing && theme == CurrentTheme) return;

            var applicationResources = Application.Current.Resources;

            var currentThemeDictionary = applicationResources.MergedDictionaries
                .FirstOrDefault(d => d.Source?.OriginalString.Contains("/Colors/") == true);

            string newThemeSource = (theme == ApplicationTheme.Dark)
                ? "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Dark.xaml"
                : "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Light.xaml";

            if (currentThemeDictionary != null)
            {
                // If a color dictionary already exists, just update its source
                currentThemeDictionary.Source = new Uri(newThemeSource);
            }
            else
            {
                // If this is the first time (during initialization), add the new dictionary
                applicationResources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri(newThemeSource)
                });
            }
            
            CurrentTheme = theme;

            // Save the setting, but only if it's a user-initiated change
            if (!isInitializing)
            {
                Settings.Default.theme = theme.ToString();
                //Saving is handled outside this method
            }
        }
    }
}