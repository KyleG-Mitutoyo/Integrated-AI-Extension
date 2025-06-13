using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EnvDTE;
using EnvDTE80;

namespace Integrated_AI.Utilities
{
    public static class BackupUtilities
    {
        // Creates a backup of the entire solution in a dated folder, within the solution's folder
        public static string CreateSolutionBackup(DTE2 dte, string backupRootPath)
        {
            try
            {
                if (dte.Solution == null || string.IsNullOrEmpty(dte.Solution.FullName))
                {
                    return null;
                }

                string solutionDir = Path.GetDirectoryName(dte.Solution.FullName);
                string uniqueSolutionFolder = GetUniqueSolutionFolder(dte);

                // Create backup folder with unique solution folder and datetime stamp
                string backupFolderName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupPath = Path.Combine(backupRootPath, uniqueSolutionFolder, backupFolderName);
                Directory.CreateDirectory(backupPath);

                // Copy solution file
                string solutionFileName = Path.GetFileName(dte.Solution.FullName);
                File.Copy(dte.Solution.FullName, Path.Combine(backupPath, solutionFileName));

                // Copy all project files and their contents
                foreach (Project project in dte.Solution.Projects)
                {
                    if (!string.IsNullOrEmpty(project.FullName))
                    {
                        string projectDir = Path.GetDirectoryName(project.FullName);
                        string relativePath = projectDir.Substring(solutionDir.Length).Trim(Path.DirectorySeparatorChar);
                        string targetProjectDir = Path.Combine(backupPath, relativePath);
                        Directory.CreateDirectory(targetProjectDir);

                        // Copy project file
                        string projectFileName = Path.GetFileName(project.FullName);
                        File.Copy(project.FullName, Path.Combine(targetProjectDir, projectFileName));

                        // Copy all project items
                        CopyProjectItems(project.ProjectItems, projectDir, targetProjectDir);
                    }
                }

                return backupPath;
            }
            catch (Exception ex)
            {
                // Log error (logging implementation depends on your setup)
                System.Windows.MessageBox.Show($"Backup failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return null;
            }
        }

        // Recursively copies project items
        private static void CopyProjectItems(ProjectItems items, string sourceDir, string targetDir)
        {
            if (items == null) return;

            foreach (ProjectItem item in items)
            {
                if (item.FileNames[0] != null)
                {
                    string relativePath = item.FileNames[0].Substring(sourceDir.Length).Trim(Path.DirectorySeparatorChar);
                    string targetPath = Path.Combine(targetDir, relativePath);
                    string targetItemDir = Path.GetDirectoryName(targetPath);

                    if (!Directory.Exists(targetItemDir))
                        Directory.CreateDirectory(targetItemDir);

                    if (File.Exists(item.FileNames[0]))
                        File.Copy(item.FileNames[0], targetPath, true);
                }

                // Recurse into sub-items
                CopyProjectItems(item.ProjectItems, sourceDir, targetDir);
            }
        }

        // Restores a solution from a backup folder
        public static bool RestoreSolution(DTE dte, string backupPath, string solutionDir)
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
                System.Windows.MessageBox.Show($"Restore failed: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return false;
            }
        }

        // Copies all contents of a directory recursively, skipping unchanged files
        private static void CopyDirectory(string sourceDir, string targetDir)
        {
            Directory.CreateDirectory(targetDir);

            foreach (string file in Directory.GetFiles(sourceDir))
            {
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

        public static string GetUniqueSolutionFolder(DTE2 dte)
        {
            // Get the solution name and parent directory for unique folder naming
            string solutionName = Path.GetFileNameWithoutExtension(dte.Solution.FullName); // e.g., "MySolution"
            string solutionDir = Path.GetDirectoryName(dte.Solution.FullName); // e.g., "C:\Projects\ClientA"
            string parentDir = Path.GetFileName(solutionDir); // e.g., "ClientA"
            string uniqueSolutionFolder = $"{parentDir}_{solutionName}"; // e.g., "ClientA_MySolution"

            return uniqueSolutionFolder;
        }

        // Retrieves a list of available restore points (backup folders) for the current solution
        public static List<string> GetRestorePoints(DTE2 dte, string backupRootPath)
        {
            try
            {
                if (dte.Solution == null || string.IsNullOrEmpty(dte.Solution.FullName))
                {
                    return new List<string>();
                }

                string uniqueSolutionFolder = GetUniqueSolutionFolder(dte);
                string backupPath = Path.Combine(backupRootPath, uniqueSolutionFolder);

                if (!Directory.Exists(backupPath))
                {
                    return new List<string>();
                }

                // Get all dated folders (yyyy-MM-dd_HH-mm-ss format)
                var restorePoints = Directory.GetDirectories(backupPath)
                    .Select(Path.GetFileName)
                    .Where(name => name.Contains("_") && DateTime.TryParseExact(
                        name, 
                        "yyyy-MM-dd_HH-mm-ss", 
                        System.Globalization.CultureInfo.InvariantCulture, 
                        System.Globalization.DateTimeStyles.None, 
                        out _))
                    .OrderByDescending(name => name)
                    .ToList();

                return restorePoints;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error retrieving restore points: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return new List<string>();
            }
        }

        // Retrieves file paths and contents from a specific backup folder
        public static Dictionary<string, string> GetRestoreFiles(DTE2 dte, string backupRootPath, string restorePoint)
        {
            try
            {
                if (dte.Solution == null || string.IsNullOrEmpty(dte.Solution.FullName) || string.IsNullOrEmpty(restorePoint))
                {
                    return null;
                }

                string uniqueSolutionFolder = GetUniqueSolutionFolder(dte);
                string backupPath = Path.Combine(backupRootPath, uniqueSolutionFolder, restorePoint);

                if (!Directory.Exists(backupPath))
                {
                    return null;
                }

                var files = new Dictionary<string, string>();
                string solutionDir = Path.GetDirectoryName(dte.Solution.FullName);

                // Recursively get all files in the backup folder
                CollectFiles(backupPath, solutionDir, files);

                return files;
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error retrieving restore files: {ex.Message}", "Error", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                return null;
            }
        }

        // Recursively collects file paths and contents, mapping to solution-relative paths
        private static void CollectFiles(string sourceDir, string solutionDir, Dictionary<string, string> files)
        {
            foreach (string file in Directory.GetFiles(sourceDir))
            {
                // Compute the solution-relative path
                string relativePath = file.Substring(sourceDir.Length).Trim(Path.DirectorySeparatorChar);
                string solutionRelativePath = Path.Combine(solutionDir, relativePath);

                // Read file content
                string content = File.ReadAllText(file);
                files[solutionRelativePath] = content;
            }

            foreach (string dir in Directory.GetDirectories(sourceDir))
            {
                CollectFiles(dir, solutionDir, files);
            }
        }
    }
}