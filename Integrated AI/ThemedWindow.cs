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

using HandyControl.Themes;
using System;
using System.Linq; 
using System.Windows;
using Window = HandyControl.Controls.Window;

namespace Integrated_AI
{
    public class ThemedWindow : Window
    {
        private bool _isThemeInitialized = false;

        public ThemedWindow()
        {
            this.Loaded += ThemedWindow_Loaded;
            // The Unloaded handler will be added dynamically in the Loaded event.
        }

        private void ThemedWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (_isThemeInitialized) return;

            // 1. Add the base theme dictionaries LOCALLY to this window.
            this.Resources.MergedDictionaries.Add(new HandyControl.Themes.ThemeResources());
            this.Resources.MergedDictionaries.Add(new HandyControl.Themes.Theme());

            // 2. Apply the current theme correctly for the first time.
            // This fixes the race condition because ThemeUtility is already initialized.
            UpdateTheme(Utilities.ThemeUtility.CurrentTheme);

            // 3. Subscribe to future theme changes.
            Utilities.ThemeUtility.ThemeChanged += UpdateTheme;

            // 4. Important: Unsubscribe when the window is unloaded to prevent memory leaks.
            this.Unloaded += (s, ev) => Utilities.ThemeUtility.ThemeChanged -= UpdateTheme;

            _isThemeInitialized = true;
        }

        private void UpdateTheme(ApplicationTheme newTheme)
        {
            var dictionaries = this.Resources.MergedDictionaries;

            // Find and remove the old color theme dictionary if it exists.
            var oldThemeDictionary = dictionaries
                .FirstOrDefault(d => d.Source != null && d.Source.ToString().Contains("/Colors/"));
            
            if (oldThemeDictionary != null)
            {
                dictionaries.Remove(oldThemeDictionary);
            }

            // Define the URI for the new color theme.
            string newThemeSource = (newTheme == ApplicationTheme.Dark)
                ? "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Dark.xaml"
                : "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Light.xaml";

            // Add the new color theme dictionary.
            dictionaries.Add(new ResourceDictionary { Source = new Uri(newThemeSource) });
        }
    }
}