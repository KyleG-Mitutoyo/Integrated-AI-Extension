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
using HandyControl.Themes;
using Integrated_AI.Utilities;
using System;
using System.Windows;
using Window = HandyControl.Controls.Window;

namespace Integrated_AI
{
    /// <summary>
    /// A base window that manages its own theme resources and responds to global theme changes.
    /// It initializes its theme in the Loaded event to ensure stability when hosted in VS.
    /// </summary>
    public class ThemedWindow : Window
    {
        private ResourceDictionary _colorThemeDictionary;
        private bool _isThemeInitialized = false;

        public ThemedWindow()
        {
            // Do NOT add resources here. Subscribe to events only.
            this.Loaded += ThemedWindow_Loaded;
            this.Unloaded += ThemedWindow_Unloaded;
        }

        private void ThemedWindow_Loaded(object sender, RoutedEventArgs e)
        {
            // The Loaded event is the safest place to manipulate resource dictionaries.
            if (_isThemeInitialized) return;

            // 1. Create and add this window's personal set of dictionaries.
            _colorThemeDictionary = new ResourceDictionary();
            this.Resources.MergedDictionaries.Add(new HandyControl.Themes.ThemeResources());
            this.Resources.MergedDictionaries.Add(new HandyControl.Themes.Theme());
            this.Resources.MergedDictionaries.Add(_colorThemeDictionary);

            // 2. Apply the current theme from the manager.
            UpdateTheme(Utilities.ThemeUtility.CurrentTheme);

            // 3. Subscribe to future theme changes.
            Utilities.ThemeUtility.ThemeChanged += UpdateTheme;

            _isThemeInitialized = true;
        }

        private void ThemedWindow_Unloaded(object sender, RoutedEventArgs e)
        {
            // Unsubscribe to prevent memory leaks.
            Utilities.ThemeUtility.ThemeChanged -= UpdateTheme;
        }

        private void UpdateTheme(ApplicationTheme newTheme)
        {
            string newThemeSource = (newTheme == ApplicationTheme.Dark)
                ? "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Dark.xaml"
                : "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Light.xaml";

            if (_colorThemeDictionary != null)
            {
                _colorThemeDictionary.Source = new Uri(newThemeSource);
            }
        }
    }
}