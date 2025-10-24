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

using EnvDTE;
using EnvDTE80;
using HandyControl.Controls;
using HandyControl.Themes;
using Integrated_AI.Utilities;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Window = HandyControl.Controls.Window;

namespace Integrated_AI
{
    public partial class ChooseCodeWindow : ThemedWindow
    {
        public enum SelectionMode
        {
            SingleItem,
            MultipleFiles
        }

        public class ReplacementItem
        {
            public string DisplayName { get; set; }
            public string ListBoxDisplayName { get; set; }
            public string FullName { get; set; }
            public CodeFunction Function { get; set; } // Null for files or special options
            public string FilePath { get; set; } // Null for functions or new function
            public string Type { get; set; } // "function", "file", "new_function", "new_file", "opened_file", "snippet", "folder"
            public string FullCode { get; set; } // Function code or empty for files
        }

        public ReplacementItem SelectedItem { get; private set; }
        public List<ReplacementItem> SelectedItems { get; private set; }
        private List<ReplacementItem> _allFiles;
        private readonly DTE2 _dte;

        public ChooseCodeWindow(DTE2 dte, Document activeDoc, string tempCurrentFile = null, string tempAiFile = null)
        {
            InitializeComponent();
            _dte = dte;
            NonClientAreaBackground = Brushes.Transparent;
            SelectedItems = new List<ReplacementItem>();

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                var (functions, files) = CodeSelectionUtilities.PopulateReplacementLists(dte, activeDoc, tempCurrentFile, tempAiFile);
                FunctionListBox.ItemsSource = functions;
                _allFiles = files;
                FileListBox.ItemsSource = _allFiles; // Populate the list
                ApplyFileFilter();                  // Now apply the filter
            });
        }
        
        public ChooseCodeWindow(DTE2 dte, SelectionMode mode)
        {
            InitializeComponent();
            _dte = dte;
            NonClientAreaBackground = Brushes.Transparent;
            SelectedItems = new List<ReplacementItem>();

            if (mode == SelectionMode.MultipleFiles)
            {
                Title = "Select Files to Send to AI";
                FunctionHeader.Visibility = Visibility.Collapsed;
                FunctionListBox.Visibility = Visibility.Collapsed;
                FunctionColumn.Width = new GridLength(0);
                FunctionHeaderColumn.Width = new GridLength(0);

                Grid.SetColumn(FileHeaderGrid, 0);
                Grid.SetColumnSpan(FileHeaderGrid, 2);
                FileHeader.Text = "Select one or more files (Ctrl+Click or Shift+Click):";
                
                Grid.SetColumn(FileListBox, 0);
                Grid.SetColumnSpan(FileListBox, 2);
                FileListBox.Margin = new Thickness(0);
                FileListBox.SelectionMode = System.Windows.Controls.SelectionMode.Extended;
                FileListBox.MouseDoubleClick -= FileListBox_MouseDoubleClick; // Disable double-click for multi-select
            }

            ThreadHelper.JoinableTaskFactory.Run(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                // We only need the files list, but the method returns both.
                var (_, files) = CodeSelectionUtilities.PopulateReplacementLists(dte, dte.ActiveDocument, null, null);
                _allFiles = files;
                FileListBox.ItemsSource = _allFiles; // Populate the list
                ApplyFileFilter();                   // Now apply the filter
            });
        }


        private void SelectButton_Click(object sender, RoutedEventArgs e)
        {
            if (FileListBox.SelectionMode == System.Windows.Controls.SelectionMode.Extended)
            {
                if (FileListBox.SelectedItems.Count > 0)
                {
                    // Filter to ensure only selectable file types are included
                    SelectedItems = FileListBox.SelectedItems.Cast<ReplacementItem>()
                        .Where(i => i.Type == "file" || i.Type == "opened_file")
                        .ToList();
                
                    if (SelectedItems.Any())
                    {
                        DialogResult = true;
                        Close();
                    }
                }
            }
            else // Single selection mode
            {
                ReplacementItem selected = FunctionListBox.SelectedItem as ReplacementItem ?? FileListBox.SelectedItem as ReplacementItem;
                if (selected != null && !selected.ListBoxDisplayName.StartsWith("-----"))
                {
                    SelectedItem = selected;
                    DialogResult = true;
                    Close();
                }
            }
        }

        private void FunctionListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FunctionListBox.SelectedItem is ReplacementItem selected && !selected.ListBoxDisplayName.StartsWith("-----"))
            {
                SelectedItem = selected;
                DialogResult = true;
                Close();
            }
        }

        private void FileListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (FileListBox.SelectedItem is ReplacementItem selected && !selected.ListBoxDisplayName.StartsWith("-----"))
            {
                SelectedItem = selected;
                DialogResult = true;
                Close();
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void FilterRadioButton_Checked(object sender, RoutedEventArgs e)
        {
            ApplyFileFilter();
        }

        private void ApplyFileFilter()
        {
            if (_allFiles == null) return;

            if (CodeFilesRadioButton?.IsChecked == true)
            {
                var commonExtensions = CodeSelectionUtilities.GetCommonCodeFileExtensions();

                // 1. Get a set of all file paths that pass the filter
                var visibleFilePaths = new HashSet<string>(
                    _allFiles.Where(item =>
                        (item.Type == "file" || item.Type == "opened_file") &&
                        !string.IsNullOrEmpty(item.FilePath) &&
                        commonExtensions.Contains(Path.GetExtension(item.FilePath).ToLowerInvariant()))
                    .Select(item => item.FilePath),
                    StringComparer.OrdinalIgnoreCase);

                var filteredList = new List<ReplacementItem>();
                foreach (var item in _allFiles)
                {
                    if (item.Type == "file" || item.Type == "opened_file")
                    {
                        // 2. Add files that are in our visible set
                        if (visibleFilePaths.Contains(item.FilePath))
                        {
                            filteredList.Add(item);
                        }
                    }
                    else if (item.Type == "folder")
                    {
                        // 3. Add folders that contain at least one visible file
                        if (!string.IsNullOrEmpty(item.FilePath) &&
                            visibleFilePaths.Any(path => path.StartsWith(item.FilePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            filteredList.Add(item);
                        }
                    }
                    else
                    {
                        // 4. Always add non-file/non-folder items (headers, "New File", etc.)
                        filteredList.Add(item);
                    }
                }
                FileListBox.ItemsSource = filteredList;
            }
            else if (OpenedFilesRadioButton?.IsChecked == true)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var openDocumentPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                if (_dte?.Documents != null)
                {
                    foreach (Document doc in _dte.Documents)
                    {
                        if (!string.IsNullOrEmpty(doc.FullName) && File.Exists(doc.FullName))
                        {
                            openDocumentPaths.Add(doc.FullName);
                        }
                    }
                }

                var filteredList = new List<ReplacementItem>();
                foreach (var item in _allFiles)
                {
                    if (item.Type == "file" || item.Type == "opened_file")
                    {
                        if (openDocumentPaths.Contains(item.FilePath))
                        {
                            filteredList.Add(item);
                        }
                    }
                    else if (item.Type == "folder")
                    {
                        if (!string.IsNullOrEmpty(item.FilePath) &&
                            openDocumentPaths.Any(path => path.StartsWith(item.FilePath, StringComparison.OrdinalIgnoreCase)))
                        {
                            filteredList.Add(item);
                        }
                    }
                    else if (item.Type == "new_file")
                    {
                        filteredList.Add(item);
                    }
                    // Intentionally skip headers for this specific filter view.
                }

                FileListBox.ItemsSource = filteredList;
            }
            else // AllFilesRadioButton is checked or it's the fallback
            {
                FileListBox.ItemsSource = _allFiles;
            }
        }
    }
}