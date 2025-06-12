using System;
using System.IO;
using System.Linq;
using EnvDTE;

namespace Integrated_AI.Utilities
{
    public static class BackupUtilities
    {
        // Creates a backup of the entire solution in a dated folder
        public static string CreateSolutionBackup(DTE dte, string backupRootPath)
        {
            try
            {
                if (dte.Solution == null || string.IsNullOrEmpty(dte.Solution.FullName))
                    return null;

                // Create backup folder with datetime stamp
                string backupFolderName = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                string backupPath = Path.Combine(backupRootPath, backupFolderName);
                Directory.CreateDirectory(backupPath);

                // Copy solution file
                string solutionDir = Path.GetDirectoryName(dte.Solution.FullName);
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
    }
}