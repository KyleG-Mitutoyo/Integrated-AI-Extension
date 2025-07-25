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
using HandyControl.Tools;
using Integrated_AI.Properties;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using HcThemeManager = HandyControl.Themes.ThemeManager;

namespace Integrated_AI.Utilities
{
    public static class ThemeUtility
    {
        private static bool _isInitialized = false;
        public static ApplicationTheme CurrentTheme { get; private set; }
        public static event Action<ApplicationTheme> ThemeChanged;

        public static void Initialize()
        {
            if (_isInitialized) return;

            if (Enum.TryParse(Settings.Default.theme, out ApplicationTheme theme))
            {
                CurrentTheme = theme;
            }
            else
            {
                CurrentTheme = ApplicationTheme.Dark;
            }

            // *** THE REAL FIX: Set the theme for the HandyControl library's internal manager ***
            HcThemeManager.Current.ApplicationTheme = CurrentTheme;

            _isInitialized = true;
        }

        public static void ChangeTheme(ApplicationTheme newTheme)
        {
            if (CurrentTheme == newTheme && _isInitialized) return;

            CurrentTheme = newTheme;
            Settings.Default.theme = newTheme.ToString();
            Settings.Default.Save();

            // *** THE REAL FIX: Update the theme for the HandyControl library's internal manager ***
            HcThemeManager.Current.ApplicationTheme = newTheme;

            // Broadcast the change to our own windows so their content can update.
            ThemeChanged?.Invoke(newTheme);
        }
    }
}