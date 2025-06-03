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

        public static string GetEditedAiCode(DiffContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (context?.RightTextBuffer == null)
            {
                ChatWindowUtilities.Log("GetEditedAiCode: Right text buffer is null.");
                // Fallback to file read if buffer is unavailable
                if (context?.TempAiFile != null && File.Exists(context.TempAiFile))
                {
                    try
                    {
                        return File.ReadAllText(context.TempAiFile);
                    }
                    catch (Exception ex)
                    {
                        ChatWindowUtilities.Log($"GetEditedAiCode: Error reading AI file: {ex}");
                        return null;
                    }
                }
                return null;
            }

            try
            {
                // Force save to ensure buffer changes are written to the file
                if (context.RightTextBuffer is IVsPersistDocData persistDocData)
                {
                    persistDocData.SaveDocData(VSSAVEFLAGS.VSSAVE_SilentSave, out string newMoniker, out int canceled);
                    if (canceled != 0)
                    {
                        ChatWindowUtilities.Log("GetEditedAiCode: Save operation was canceled.");
                        return null;
                    }
                    if (!string.IsNullOrEmpty(newMoniker) && newMoniker != context.TempAiFile)
                    {
                        ChatWindowUtilities.Log($"GetEditedAiCode: Document moniker changed to {newMoniker}. Updating context.");
                        context.TempAiFile = newMoniker; // Update file path if renamed
                    }
                }

                // Get text from the buffer
                context.RightTextBuffer.GetLineCount(out int lineCount);
                context.RightTextBuffer.GetLengthOfLine(lineCount - 1, out int lastLineLength);
                context.RightTextBuffer.GetLineText(0, 0, lineCount - 1, lastLineLength, out string text);
                return text;
            }
            catch (Exception ex)
            {
                ChatWindowUtilities.Log($"GetEditedAiCode: Error reading from text buffer: {ex}");
                // Fallback to file read
                if (context?.TempAiFile != null && File.Exists(context.TempAiFile))
                {
                    try
                    {
                        return File.ReadAllText(context.TempAiFile);
                    }
                    catch (Exception ex2)
                    {
                        ChatWindowUtilities.Log($"GetEditedAiCode: Fallback file read failed: {ex2}");
                        return null;
                    }
                }
                return null;
            }
        }
    }
}
