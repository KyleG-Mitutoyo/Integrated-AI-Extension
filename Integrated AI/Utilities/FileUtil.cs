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

namespace Integrated_AI.Utilities
{
    public static class FileUtil
    {
        //Used with LoadScript to prevent repeated file reads for the same script
        private static readonly Dictionary<string, string> _scriptCache = new Dictionary<string, string>();

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
                MessageBox.Show($"Error reading AI code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return string.Empty;
            }
        }
        //t
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
                    WebViewUtilities.Log($"Successfully loaded script: {scriptName}. Length: {scriptContent.Length}. First 100 chars: {scriptContent.Substring(0, Math.Min(100, scriptContent.Length))}");
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
    }
}
