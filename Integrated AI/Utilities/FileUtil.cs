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
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Threading;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static Integrated_AI.Utilities.DiffUtility;
using MessageBox = HandyControl.Controls.MessageBox;
using Process = System.Diagnostics.Process;

namespace Integrated_AI.Utilities
{
    public static class FileUtil
    {
        //Used with LoadScript to prevent repeated file reads for the same script
        private static readonly Dictionary<string, string> _scriptCache = new Dictionary<string, string>();
        public static string _recentFunctionsFilePath = null;

        public static List<string> LoadRecentFunctions(string recentFunctionsFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(recentFunctionsFilePath))
                {
                    // If the file doesn't exist, return null
                    return null;
                }

                var recentFunctions = new List<string>();
                if (File.Exists(recentFunctionsFilePath))
                {
                    recentFunctions = File.ReadAllLines(recentFunctionsFilePath).Take(3).ToList();
                }
                return recentFunctions;
            }
            catch
            {
                WebViewUtilities.Log("LoadRecentFunctions error");
                return null;
            }
        }

        public static void UpdateRecentFunctions(List<string> recentFunctions, string functionName, string recentFunctionsFilePath)
        {
            try
            {
                if (string.IsNullOrEmpty(functionName) || functionName.StartsWith("-----")) return;

                recentFunctions.Remove(functionName); // Remove if already exists to avoid duplicates
                recentFunctions.Insert(0, functionName); // Add to top
                recentFunctions = recentFunctions.Take(3).ToList(); // Keep only top 3
                File.WriteAllLines(recentFunctionsFilePath, recentFunctions);
            }
            catch
            {
                WebViewUtilities.Log("UpdateRecentFunctions error");
            }
        }

        public static string GetRelativePath(string solutionPath, string filePath)
        {
            if (!string.IsNullOrEmpty(solutionPath) && filePath.StartsWith(solutionPath, StringComparison.OrdinalIgnoreCase))
            {
                return filePath.Substring(solutionPath.Length + 1).Replace("\\", "/");
            }
            return Path.GetFileName(filePath);
        }

        public static void DeleteTempFile(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (IOException ex)
                {
                    WebViewUtilities.Log($"Error deleting temp file {filePath}: {ex.Message}");
                }
            }
        }

        public static DiffContext CleanUpTempFiles(DiffContext context)
        {
            if (context != null)
            {
                DeleteTempFile(context.TempCurrentFile);
                DeleteTempFile(context.TempAiFile);
            }
            return null;
        }

        public static string GetAICode(System.Windows.Window window, string tempAiFile)
        {
            if (string.IsNullOrEmpty(tempAiFile) || !File.Exists(tempAiFile))
            {
                WebViewUtilities.Log($"Invalid AI code file: {tempAiFile ?? "null"}");
                return string.Empty;
            }

            try
            {
                return File.ReadAllText(tempAiFile);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error reading AI code file '{tempAiFile}': {ex.Message}");
                ThemedMessageBox.Show(window, $"Error reading AI code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return string.Empty;
            }
        }
        
        public static string LoadScript(string scriptName)
        {
            if (_scriptCache.TryGetValue(scriptName, out string cachedScript))
            {
                WebViewUtilities.Log($"Retrieved '{scriptName}' from cache.");
                return cachedScript;
            }

            try
            {
                string assemblyLocation = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                if (string.IsNullOrEmpty(assemblyLocation))
                {
                    string errorMsg = $"Critical Error: Could not determine assembly location for loading script '{scriptName}'.";
                    WebViewUtilities.Log(errorMsg);
                    // Return a script that will cause a clear JS error and log to console
                    return $"console.error('LoadScript ERROR: {errorMsg.Replace("'", "\\'")}'); throw new Error('LoadScript ERROR: {errorMsg.Replace("'", "\\'")}');";
                }
                string scriptPath = Path.Combine(assemblyLocation, "Scripts", scriptName);

                WebViewUtilities.Log($"Attempting to load script from: {scriptPath}");

                if (File.Exists(scriptPath))
                {
                    string scriptContent = File.ReadAllText(scriptPath);
                    if (string.IsNullOrWhiteSpace(scriptContent))
                    {
                        string errorMsg = $"Error: Script file '{scriptName}' at '{scriptPath}' is empty or consists only of whitespace.";
                        WebViewUtilities.Log(errorMsg);
                        return $"console.error('LoadScript ERROR: {errorMsg.Replace("'", "\\'")}'); throw new Error('LoadScript ERROR: {errorMsg.Replace("'", "\\'")}');";
                    }
                    _scriptCache[scriptName] = scriptContent;
                    WebViewUtilities.Log($"Successfully loaded script: {scriptName}. Length: {scriptContent.Length}.");
                    return scriptContent;
                }
                else
                {
                    // This case means files are not in bin/Debug/Scripts, which contradicts user observation for debug.
                    // However, this path WOULD be hit if VSIX deployment fails.
                    string errorMsg = $"Error: Script file '{scriptName}' not found at '{scriptPath}'. VSIX packaging issue likely if this happens after deployment.";
                    WebViewUtilities.Log(errorMsg);
                    return $"console.error('LoadScript ERROR: {errorMsg.Replace("'", "\\'")}'); throw new Error('LoadScript ERROR: {errorMsg.Replace("'", "\\'")}');";
                }
            }
            catch (Exception ex)
            {
                string errorMsg = $"Generic error loading script '{scriptName}': {ex.Message}";
                WebViewUtilities.Log(errorMsg);
                return $"console.error('LoadScript ERROR: {errorMsg.Replace("'", "\\'")}'); throw new Error('LoadScript ERROR: {errorMsg.Replace("'", "\\'")}');";
            }
        }

        // Recursively collects file paths and contents, mapping to solution-relative paths
        // Only used for restore compare functionality
        public static void CollectFiles(System.Windows.Window window, string sourceDir, Dictionary<string, string> files)
        {
            try
            {
                foreach (string file in Directory.GetFiles(sourceDir))
                {
                    try
                    {
                        // Calculate the solution-relative path
                        string relativePath = file.Substring(sourceDir.Length).TrimStart(Path.DirectorySeparatorChar);
                        string solutionRelativePath = relativePath; // Use relative path directly


                        // Read file content
                        string content = File.ReadAllText(file);
                        files[solutionRelativePath] = content;

                        //WebViewUtilities.Log($"Collecting file: {solutionRelativePath} with contents: {content}");
                    }
                    catch (Exception ex)
                    {
                        // Log error for specific file but continue processing others
                        WebViewUtilities.Log($"Error reading file {file}: {ex.Message}");
                        ThemedMessageBox.Show(window, $"Error reading file {file}: {ex.Message}", "Warning", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    }
                }

                foreach (string dir in Directory.GetDirectories(sourceDir))
                {
                    CollectFiles(window, dir, files);
                }
            }
            catch (Exception ex)
            {
                // Log directory-level error but continue processing
                WebViewUtilities.Log($"Error processing directory {sourceDir}: {ex.Message}");
                ThemedMessageBox.Show(window, $"Error processing directory {sourceDir}: {ex.Message}", "Warning", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
            }
        }

        public static bool OpenFolder(string path)
        {
            try
            {
                // Validate the path is not null or empty
                if (string.IsNullOrWhiteSpace(path))
                {
                    return false;
                }

                // Normalize the path to handle any invalid characters
                string normalizedPath = Path.GetFullPath(path).Trim();

                // Check if the directory exists
                if (!Directory.Exists(normalizedPath))
                {
                    return false;
                }

                // Start explorer.exe with the normalized folder path
                ProcessStartInfo startInfo = new ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"\"{normalizedPath}\"",
                    UseShellExecute = true, // Use the OS shell to avoid blocking
                    WindowStyle = ProcessWindowStyle.Normal
                };
                
                using (Process process = Process.Start(startInfo))
                {
                    // No need to wait for exit; let Explorer run independently
                    return true;
                }
            }
            catch (Exception)
            {
                // Catch any errors (e.g., invalid path, permission issues)
                return false;
            }
        }

        // Prompt user for a new file path using a dialog
        public static string PromptForNewFilePath(DTE2 dte, string language = "cs")
        {
            // Use the native WPF SaveFileDialog for better integration within a WPF-based VS extension.
            var dialog = new SaveFileDialog();

            if (language == "vb")
            {
                dialog.Filter = "VB Files (*.vb)|*.vb|All Files (*.*)|*.*";
                dialog.DefaultExt = "vb";
            }
            else
            {
                dialog.Filter = "C# Files (*.cs)|*.cs|All Files (*.*)|*.*";
                dialog.DefaultExt = "cs";
            }
            dialog.Title = "Select Location for New File";
            dialog.InitialDirectory = Path.GetDirectoryName(dte.Solution.FullName); // Start in solution directory

            // The WPF ShowDialog returns bool?, so we check for 'true'.
            if (dialog.ShowDialog() == true)
            {
                return dialog.FileName;
            }

            return null; // Return null if cancelled or closed
        }


        // Create a new file in the solution and add AI-generated code
        public static async Task CreateNewFileInSolutionAsync(JoinableTaskFactory joinableTaskFactory, DTE2 dte, string filePath, string aiCode)
        {
            bool fileCreatedSuccessfully = false;
            try
            {
                // --- Part 1: Background Thread Operations ---
                await Task.Run(() =>
                {
                    string directory = Path.GetDirectoryName(filePath);
                    if (!Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }
                    File.WriteAllText(filePath, aiCode);
                    WebViewUtilities.Log("File has been written to disk on a background thread.");
                });

                fileCreatedSuccessfully = true;

                // --- Part 2: Main Thread Operations ---
                await joinableTaskFactory.SwitchToMainThreadAsync();

                try
                {
                    dte.StatusBar.Text = $"Adding new file: {Path.GetFileName(filePath)}...";
                    dte.StatusBar.Animate(true, vsStatusAnimation.vsStatusAnimationGeneral);

                    Project project = FindProjectForActiveDocument(dte);
                    string fileName = Path.GetFileName(filePath);

                    if (project != null)
                    {
                        WebViewUtilities.Log($"Found active project '{project.Name}'. Attempting to add file.");
                        try
                        {
                            // This is the critical step that interacts with the .csproj file.
                            project.ProjectItems.AddFromFile(filePath);
                            WebViewUtilities.Log($"New file '{filePath}' added to project '{project.Name}'.");
                        }
                        catch (Exception ex)
                        {
                            // **NEW: Handle the specific failure of adding the file to the project.**
                            WebViewUtilities.Log($"ERROR: File was created but failed to be added to project '{project.Name}'. Exception: {ex.Message}");
                            ThemedMessageBox.Show(
                                dte as System.Windows.Window,
                                $"The file '{fileName}' was created successfully at:\n\n{filePath}\n\nHowever, an error occurred while adding it to the project '{project.Name}'.\n\nYou may need to add it manually by right-clicking the project and selecting 'Add > Existing Item...'.\n\nError: {ex.Message}",
                                "Failed to Add File to Project",
                                MessageBoxButton.OK,
                                MessageBoxImage.Warning);
                        }
                    }
                    else
                    {
                        // **NEW: Provide clearer feedback when no project is found.**
                        WebViewUtilities.Log($"Could not find an active project. The file will be opened as a miscellaneous file.");
                        dte.StatusBar.Text = $"File created, but no project found. Opening as a miscellaneous file.";
                    }

                    dte.ItemOperations.OpenFile(filePath);
                }
                finally
                {
                    dte.StatusBar.Animate(false, vsStatusAnimation.vsStatusAnimationGeneral);
                    dte.StatusBar.Clear();
                }
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in CreateNewFileInSolutionAsync: {ex.Message}\n{ex.StackTrace}");
                await joinableTaskFactory.SwitchToMainThreadAsync();

                // **NEW: Check if the file was created before the error occurred.**
                if (fileCreatedSuccessfully)
                {
                    ThemedMessageBox.Show(
                        dte as System.Windows.Window,
                        $"The file '{Path.GetFileName(filePath)}' was created successfully at:\n\n{filePath}\n\nHowever, an error occurred while trying to open it or add it to the project.\n\nPlease check the log for details.\n\nError: {ex.Message}",
                        "File Created, But Error Occurred",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }
                else
                {
                    ThemedMessageBox.Show(
                        dte as System.Windows.Window,
                        $"An error occurred while trying to create the file '{Path.GetFileName(filePath)}'.\n\nPlease check the log for details.\n\nError: {ex.Message}",
                        "File Creation Failed",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);
                }

                // Rethrowing is often not needed if you've already handled the error with a message box.
                // Consider if the caller truly needs to handle it further.
                // throw; 
            }
        }


        // Find the appropriate project for the given directory
        public static Project FindProjectForActiveDocument(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            Project project = null;
            try
            {
                if (dte.ActiveDocument != null && dte.ActiveDocument.ProjectItem != null)
                {
                    project = dte.ActiveDocument.ProjectItem.ContainingProject;
                }
            }
            catch (Exception)
            {
                // ActiveDocument might not have a ProjectItem (e.g., a solution-level file).
            }

            if (project == null)
            {
                try
                {
                    Array activeProjects = dte.ActiveSolutionProjects as Array;
                    if (activeProjects != null && activeProjects.Length > 0)
                    {
                        project = activeProjects.GetValue(0) as Project;
                    }
                }
                catch
                {
                    // Ignore
                }
            }

            return project;
        }

    }
}
