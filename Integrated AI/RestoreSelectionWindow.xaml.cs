using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HandyControl.Controls;
using Integrated_AI.Utilities;
using MessageBox = System.Windows.MessageBox;
using Window = System.Windows.Window;

namespace Integrated_AI
{
    public partial class RestoreSelectionWindow : Window
    {
        public class BackupItem
        {
            public string DisplayName { get; set; }
            public string FolderPath { get; set; }
            public DateTime BackupTime { get; set; }
        }

        public BackupItem SelectedBackup { get; private set; }
        private readonly string _backupRootPath;

        public RestoreSelectionWindow(string backupRootPath)
        {
            InitializeComponent();
            var dummy = typeof(HandyControl.Controls.Window); // Required for HandyControl XAML compilation
            _backupRootPath = backupRootPath;
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
                        backups.Add(new BackupItem
                        {
                            DisplayName = backupTime.ToString("yyyy-MM-dd hh:mm:ss tt"),
                            FolderPath = dir.FullName,
                            BackupTime = backupTime
                        });
                    }
                }
            }
            BackupListBox.ItemsSource = backups;
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

        private void BackupListBox_MouseDoubleClick(object sender, MouseButtonEventArgs e)
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
            var result = MessageBox.Show("Are you sure you want to delete all backup folders?", 
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
    }
}