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
using System.Windows;

namespace Integrated_AI.Utilities
{
    public static class ThemedMessageBox
    {
        private static readonly object _lock = new object();

        public static MessageBoxResult Show(Window owner, string message, string caption, MessageBoxButton button, MessageBoxImage icon)
        {
            // Lock to prevent race conditions if two message boxes are shown at once.
            lock (_lock)
            {
                var appResources = Application.Current.Resources.MergedDictionaries;

                // Define the resources we need to inject.
                // Note: In modern HandyControl, ThemeResources is bundled into Theme.xaml.
                var baseTheme = new ResourceDictionary { Source = new Uri("pack://application:,,,/HandyControl;component/Themes/Theme.xaml") };

                string colorThemeSource = (ThemeUtility.CurrentTheme == ApplicationTheme.Dark)
                    ? "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Dark.xaml"
                    : "pack://application:,,,/HandyControl;component/Themes/Basic/Colors/Light.xaml";
                var colorTheme = new ResourceDictionary { Source = new Uri(colorThemeSource) };

                try
                {
                    // STEP 1: Surgically inject the resources into the global scope.
                    appResources.Add(baseTheme);
                    appResources.Add(colorTheme);

                    // STEP 2: Now that the environment is correct, show the message box.
                    // It will now find the resources it needs.
                    return HandyControl.Controls.MessageBox.Show(owner, message, caption, button, icon);
                }
                finally
                {
                    // STEP 3: CRITICAL! Immediately remove the resources to avoid theming all of VS.
                    // The 'finally' block guarantees this runs even if .Show() throws an exception.
                    appResources.Remove(colorTheme);
                    appResources.Remove(baseTheme);
                }
            }
        }
    }
}