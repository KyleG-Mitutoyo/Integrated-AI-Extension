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
using MessageBox = HandyControl.Controls.MessageBox;
using Window = HandyControl.Controls.Window;

namespace Integrated_AI
{
    public partial class RestoreSelectionWindow : Window
    {
        public class BackupItem
        {
            public string DisplayName { get; set; }
            public string FolderPath { get; set; }
            public DateTime BackupTime { get; set; }
            public string AIChatTag { get; set; } // New property for AI chat tag
            public string AICode { get; set; }    // New property for AI code
        }

        public BackupItem SelectedBackup { get; private set; }
        private readonly string _backupRootPath;
        private DTE2 _dte;

        public RestoreSelectionWindow(DTE2 dte, string backupRootPath)
        {
            InitializeComponent();
            NonClientAreaBackground = Brushes.Transparent;

            _backupRootPath = backupRootPath;
            _dte = dte;
            PopulateBackupList();
        }

        private void PopulateBackupList()
        {
            var backups = new List<BackupItem>();
            if (Directory.Exists(_backupRootPath))
            {
                var directories = Directory.GetDirectories(_backupRootPath)
                    .Select(dir => new DirectoryInfo(dir))
                    .OrderByDescending(di => di.CreationTime);

                foreach (var dir in directories)
                {
                    if (DateTime.TryParseExact(dir.Name, "yyyy-MM-dd_HH-mm-ss", null, System.Globalization.DateTimeStyles.None, out var backupTime))
                    {
                        // Retrieve AI chat and AI code from metadata
                        var (aiCode, aiChat) = BackupUtilities.GetBackupMetadata(_backupRootPath, dir.Name);
                        string aiChatTag = string.IsNullOrEmpty(aiChat) ? "n/a" : aiChat.Length > 30 ? aiChat.Substring(0, 27) + "..." : aiChat;

                        backups.Add(new BackupItem
                        {
                            DisplayName = backupTime.ToString("MM-dd-yyyy hh:mm:ss tt"),
                            FolderPath = dir.FullName,
                            BackupTime = backupTime,
                            AIChatTag = aiChatTag,
                            AICode = string.IsNullOrEmpty(aiCode) ? "No AI Code" : aiCode
                        });
                    }
                }
            }
            BackupListBox.ItemsSource = backups;

            // Select the first item by default to show AI code
            if (backups.Count > 0)
            {
                BackupListBox.SelectedIndex = 0;
            }
        }

        private void BackupListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (BackupListBox.SelectedItem is BackupItem selected)
            {
                AICodeTextBox.Text = selected.AICode;
            }
            else
            {
                AICodeTextBox.Text = string.Empty;
            }
        }

        private void RestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (BackupListBox.SelectedItem is BackupItem selected)
            {
                SelectedBackup = selected;
                DialogResult = true;
                Close();
            }
        }

        private void DeleteButton_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show("Are you sure you want to delete all backups for the current solution?",
                "Confirm Delete All", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (result == MessageBoxResult.Yes)
            {
                try
                {
                    if (Directory.Exists(_backupRootPath))
                    {
                        foreach (var dir in Directory.GetDirectories(_backupRootPath))
                        {
                            Directory.Delete(dir, true);
                        }
                    }
                    PopulateBackupList(); // Refresh list after deletion
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Error deleting backups: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void CompareButton_Click(object sender, RoutedEventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (BackupListBox.SelectedItem == null)
            {
                MessageBox.Show(this, "Please select a restore point.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            try
            {
                // Get selected restore point (yyyy-MM-dd_HH-mm-ss)
                if (BackupListBox.SelectedItem is BackupItem selected)
                {
                    string selectedRestore = selected.BackupTime.ToString("yyyy-MM-dd_HH-mm-ss");

                    // Retrieve restore files (Dictionary<string, string> of file paths and contents)
                    var restoreFiles = BackupUtilities.GetRestoreFiles(_dte, _backupRootPath, selectedRestore);
                    if (restoreFiles == null || restoreFiles.Count == 0)
                    {
                        MessageBox.Show("No files found for the selected restore point.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Open diff views for all changed files
                    var diffContexts = DiffUtility.OpenMultiFileDiffView(_dte, restoreFiles);
                    if (diffContexts == null || diffContexts.Count == 0)
                    {
                        // Message already shown in OpenMultiFileDiffView
                        return;
                    }

                    // Close the window after opening diff views
                    Close();
                }
                else
                {
                    MessageBox.Show("Invalid restore point selected.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening diff views: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                WebViewUtilities.Log($"RestoreSelectionWindow.CompareButton_Click: Exception - {ex.Message}, StackTrace: {ex.StackTrace}");
            }
        }

        private void OpenBackups_Click(object sender, RoutedEventArgs e)
        {
            if (!FileUtil.OpenFolder(_backupRootPath))
            {
                MessageBox.Show("Failed to open the backup folder.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}