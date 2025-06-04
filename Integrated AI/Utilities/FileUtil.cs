using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using static Integrated_AI.Utilities.DiffUtility;

namespace Integrated_AI.Utilities
{
    public static class FileUtil
    {
        public static List<string> LoadRecentFunctions(string recentFunctionsFilePath)
        {
            var recentFunctions = new List<string>();
            if (File.Exists(recentFunctionsFilePath))
            {
                recentFunctions = File.ReadAllLines(recentFunctionsFilePath).Take(3).ToList();
            }
            return recentFunctions;
        }

        public static void UpdateRecentFunctions(List<string> recentFunctions, string functionName, string recentFunctionsFilePath)
        {
            if (string.IsNullOrEmpty(functionName) || functionName.StartsWith("-----")) return;

            recentFunctions.Remove(functionName); // Remove if already exists to avoid duplicates
            recentFunctions.Insert(0, functionName); // Add to top
            recentFunctions = recentFunctions.Take(3).ToList(); // Keep only top 3
            File.WriteAllLines(recentFunctionsFilePath, recentFunctions);
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
                    Debug.WriteLine($"Error deleting temp file {filePath}: {ex.Message}");
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

        public static string GetAICode(string tempAiFile)
        {
            if (string.IsNullOrEmpty(tempAiFile) || !File.Exists(tempAiFile))
            {
                ChatWindowUtilities.Log($"Invalid AI code file: {tempAiFile ?? "null"}");
                return string.Empty;
            }

            try
            {
                return File.ReadAllText(tempAiFile);
            }
            catch (Exception ex)
            {
                ChatWindowUtilities.Log($"Error reading AI code file '{tempAiFile}': {ex.Message}");
                MessageBox.Show($"Error reading AI code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return string.Empty;
            }
        }
    }
}
