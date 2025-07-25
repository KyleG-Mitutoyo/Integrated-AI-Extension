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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using EnvDTE;
using EnvDTE80;
using MessageBox = HandyControl.Controls.MessageBox;
using Newtonsoft.Json;
using Microsoft.Web.WebView2.Wpf;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;

namespace Integrated_AI.Utilities
{
    public static class BackupUtilities
    {
        // Creates a backup of the entire solution in a dated folder, within the solution's folder
        public static string CreateSolutionBackup(DTE2 dte, System.Windows.Window window, string backupRootPath, string aiCode, string aiChat, string url)
        {
            string backupPath = null; 

            try
            {
                if (dte.Solution == null || string.IsNullOrEmpty(dte.Solution.FullName))
                {
                    return null;
                }

                string solutionDir = Path.GetDirectoryName(dte.Solution.FullName);
                Uri solutionUri = new Uri(solutionDir + Path.DirectorySeparatorChar); // Important to end with a slash
                string uniqueSolutionFolder = GetUniqueSolutionFolder(dte);

                // Create backup folder with unique solution folder and datetime stamp
                string backupFolderName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                
                backupPath = Path.Combine(backupRootPath, uniqueSolutionFolder, backupFolderName);
                Directory.CreateDirectory(backupPath);

                // Save AI code and chat metadata to a JSON file
                string metadataPath = Path.Combine(backupPath, "backup_metadata.json");
                var metadata = new
                {
                    AICode = aiCode,
                    AIChat = aiChat,
                    Url = url,
                    BackupTime = backupFolderName
                };
                File.WriteAllText(metadataPath, JsonConvert.SerializeObject(metadata, Formatting.Indented));

                // Copy solution file
                string solutionFileName = Path.GetFileName(dte.Solution.FullName);
                File.Copy(dte.Solution.FullName, Path.Combine(backupPath, solutionFileName));

                // Copy all project files and their contents
                foreach (Project project in dte.Solution.Projects)
                {
                    // First, copy the project file itself
                    if (!string.IsNullOrEmpty(project.FullName) && File.Exists(project.FullName))
                    {
                        CopyItem(project.FullName, solutionUri, backupPath);
                    }

                    // Then, recursively copy all items within that project
                    CopyProjectItems(project.ProjectItems, solutionUri, backupPath);
                }

                return backupPath;
            }
            catch (Exception ex)
            {
                // If the backup path was determined and the directory was created, attempt to delete it.
                if (!string.IsNullOrEmpty(backupPath) && Directory.Exists(backupPath))
                {
                    try
                    {
                        // Delete the incomplete backup folder and all its contents.
                        Directory.Delete(backupPath, true); 
                    }
                    catch (Exception deleteEx)
                    {
                        // If cleanup also fails, inform the user to manually remove it.
                        WebViewUtilities.Log($"Failed to clean up incomplete backup folder '{backupPath}': {deleteEx.Message}");
                        ThemedMessageBox.Show(window, $"Backup failed: {ex.Message}\n\nAdditionally, failed to clean up the incomplete backup folder '{backupPath}'. Please remove it manually.\nCleanup error: {deleteEx.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                        return null;
                    }
                }

                WebViewUtilities.Log($"Backup failed: {ex.Message}");
                ThemedMessageBox.Show(window, $"Backup failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return null;
            }
        }

        // Recursively copies project items
        private static void CopyProjectItems(ProjectItems items, Uri solutionUri, string backupRootPath)
        {
            try
            {
                if (items == null) return;

                foreach (ProjectItem item in items)
                {
                    // A ProjectItem can represent multiple files (e.g., for forms). FileNames are 1-indexed.
                    for (short i = 1; i <= item.FileCount; i++)
                    {
                        CopyItem(item.FileNames[i], solutionUri, backupRootPath);
                    }

                    // Recurse into sub-items or nested projects
                    if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                    {
                        CopyProjectItems(item.ProjectItems, solutionUri, backupRootPath);
                    }
                }
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log("CopyProjectItems error");
                return;
            }
        }

        // Restores a solution from a backup folder
        public static bool RestoreSolution(DTE dte, System.Windows.Window window, string backupPath, string solutionDir)
        {
            try
            {
                if (!Directory.Exists(backupPath))
                    return false;

                // Close current solution if open
                if (dte.Solution != null && !string.IsNullOrEmpty(dte.Solution.FullName))
                    dte.Solution.Close();

                // Copy all files from backup to solution directory
                CopyDirectory(backupPath, solutionDir);

                // Reopen the solution
                string solutionFile = Directory.GetFiles(solutionDir, "*.sln").FirstOrDefault();
                if (solutionFile != null)
                {
                    dte.Solution.Open(solutionFile);
                    return true;
                }

                return false;
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"RestoreSolution error: {ex.Message}");
                ThemedMessageBox.Show(window, $"Restore failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        // Copies all contents of a directory recursively, skipping unchanged files
        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            try
            {
                Directory.CreateDirectory(targetDir);

                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    // Skip metadata file during restore
                    if (Path.GetFileName(file) == "backup_metadata.json")
                        continue;

                    string targetFile = Path.Combine(targetDir, Path.GetFileName(file));
                    bool shouldCopy = true;

                    // Check if target file exists and is identical
                    if (File.Exists(targetFile))
                    {
                        var sourceInfo = new FileInfo(file);
                        var targetInfo = new FileInfo(targetFile);
                        // Skip if file size and last write time are the same
                        if (sourceInfo.Length == targetInfo.Length &&
                            sourceInfo.LastWriteTimeUtc == targetInfo.LastWriteTimeUtc)
                        {
                            shouldCopy = false;
                        }
                    }

                    if (shouldCopy)
                    {
                        File.Copy(file, targetFile, true);
                    }
                }

                foreach (string dir in Directory.GetDirectories(sourceDir))
                {
                    string targetSubDir = Path.Combine(targetDir, Path.GetFileName(dir));
                    CopyDirectory(dir, targetSubDir);
                }
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log("CopyDirectory error");
            }
        }

        public static string GetUniqueSolutionFolder(DTE2 dte)
        {
            try
            {
                // Get the solution name and parent directory for unique folder naming
                string solutionName = Path.GetFileNameWithoutExtension(dte.Solution.FullName); // e.g., "MySolution"
                string solutionDir = Path.GetDirectoryName(dte.Solution.FullName); // e.g., "C:\Projects\ClientA"
                string parentDir = Path.GetFileName(solutionDir); // e.g., "ClientA"
                string uniqueSolutionFolder = $"{parentDir}_{solutionName}"; // e.g., "ClientA_MySolution"

                return uniqueSolutionFolder;
            }
            catch
            {
                WebViewUtilities.Log("GetUniqueSolutionFolder error");
                return null;
            }
            
        }

        // Retrieves file paths and contents from a specific backup folder
        public static Dictionary<string, string> GetRestoreFiles(DTE2 dte, System.Windows.Window window, string backupRootPath, string restorePoint)
        {
            try
            {
                if (dte.Solution == null || string.IsNullOrEmpty(dte.Solution.FullName) || string.IsNullOrEmpty(restorePoint))
                {
                    WebViewUtilities.Log("Invalid input: Solution or restore point is missing.");
                    ThemedMessageBox.Show(window, "Invalid input: Solution or restore point is missing.", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return new Dictionary<string, string>(); // Return empty dictionary instead of null
                }

                string backupPath = Path.Combine(backupRootPath, restorePoint);

                if (!Directory.Exists(backupPath))
                {
                    WebViewUtilities.Log($"Backup directory does not exist: {backupPath}");
                    ThemedMessageBox.Show(window, $"Backup directory does not exist: {backupPath}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                    return new Dictionary<string, string>(); // Return empty dictionary instead of null
                }

                var files = new Dictionary<string, string>();

                // Recursively get all files in the backup folder
                FileUtil.CollectFiles(window, backupPath, files);

                if (files.Count == 0)
                {
                    WebViewUtilities.Log($"No files found in backup directory: {backupPath}");
                    ThemedMessageBox.Show(window, $"No files found in backup directory: {backupPath}", "Warning", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                }

                return files;
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error retrieving restore files: {ex.Message}");
                ThemedMessageBox.Show(window, $"Error retrieving restore files: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return new Dictionary<string, string>(); // Return empty dictionary instead of null
            }
        }

        // Retrieves AI code and chat metadata from a specific backup folder
        public static (string aiCode, string aiChat, string url) 
            GetBackupMetadata(System.Windows.Window window, string solutionBackupRootPath, string restorePoint)
        {
            try
            {
                string metadataPath = Path.Combine(solutionBackupRootPath, restorePoint, "backup_metadata.json");
                if (!File.Exists(metadataPath))
                {
                    //MessageBox.Show($"Metadata file not found: {metadataPath}", "Warning", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    WebViewUtilities.Log($"Metadata file not found: {metadataPath}");
                    return (null, null, null);
                }

                string jsonContent = File.ReadAllText(metadataPath);
                var metadata = JsonConvert.DeserializeObject<dynamic>(jsonContent);
                if (metadata.Scroll == null)
                {
                    metadata.Scroll = 0; // Default scroll position if not present
                }
                return (metadata.AICode?.ToString(), metadata.AIChat?.ToString(), metadata.Url?.ToString());
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error retrieving backup metadata: {ex.Message}");
                ThemedMessageBox.Show(window, $"Error retrieving backup metadata: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return (null, null, null);
            }
        }

        private static void CopyItem(string sourcePath, Uri solutionUri, string backupRootPath)
        {
            try
            {
                if (string.IsNullOrEmpty(sourcePath) || !File.Exists(sourcePath)) return;

                Uri sourceUri = new Uri(sourcePath);

                // Get the file's path relative to the solution root.
                string relativePath = Uri.UnescapeDataString(solutionUri.MakeRelativeUri(sourceUri).ToString().Replace('/', Path.DirectorySeparatorChar));


                // The target path is simply the backup root plus the item's relative path.
                string targetPath = Path.Combine(backupRootPath, relativePath);
                string targetItemDir = Path.GetDirectoryName(targetPath);

                if (!Directory.Exists(targetItemDir))
                    Directory.CreateDirectory(targetItemDir);

                File.Copy(sourcePath, targetPath, true);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log("CopyItem error");
                return;
            }

            
        }
    }
}