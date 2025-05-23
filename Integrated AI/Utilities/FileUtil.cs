using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
    }
}
