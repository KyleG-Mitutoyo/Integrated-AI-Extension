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
            // Use Windows Forms SaveFileDialog for simplicity
            using (var dialog = new System.Windows.Forms.SaveFileDialog())
            {
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

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    return dialog.FileName;
                }
            }
            return null; // Return null if cancelled
        }

        // Create a new file in the solution and add AI-generated code
        public static void CreateNewFileInSolution(DTE2 dte, string filePath, string aiCode)
        {
            try
            {
                // Ensure the directory exists
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write the AI-generated code to the new file
                File.WriteAllText(filePath, aiCode);

                // Add the file to the solution
                Project project = FindProjectForPath(dte, directory);
                if (project != null)
                {
                    project.ProjectItems.AddFromFile(filePath);
                    WebViewUtilities.Log($"New file '{filePath}' added to project '{project.Name}'.");
                }
                else
                {
                    // Fallback: Add to the solution's Miscellaneous Files
                    dte.ItemOperations.OpenFile(filePath);
                    WebViewUtilities.Log($"New file '{filePath}' added as a miscellaneous file.");
                }

                // Open the new file in the editor
                dte.ItemOperations.OpenFile(filePath);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error creating new file: {ex.Message}");
                //MessageBox.Show($"Error creating new file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Find the appropriate project for the given directory
        public static Project FindProjectForPath(DTE2 dte, string directory)
        {
            foreach (Project project in dte.Solution.Projects)
            {
                string projectDir = Path.GetDirectoryName(project.FullName);
                if (directory.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                {
                    return project;
                }
            }
            return null; // No matching project found
        }
    }
}
