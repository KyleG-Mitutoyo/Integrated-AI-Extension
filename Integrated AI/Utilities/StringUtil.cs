using EnvDTE;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public static class StringUtil
    {
        // Custom method to unescape JSON-encoded strings
        public static string UnescapeJsonString(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Remove outer quotes if present
            input = input.Trim('"');

            // Handle common JSON escape sequences
            StringBuilder result = new StringBuilder(input.Length);
            for (int i = 0; i < input.Length; i++)
            {
                if (i < input.Length - 1 && input[i] == '\\')
                {
                    char nextChar = input[i + 1];
                    switch (nextChar)
                    {
                        case '"':
                            result.Append('"');
                            i++;
                            break;
                        case '\\':
                            result.Append('\\');
                            i++;
                            break;
                        case 'n':
                            result.Append('\n');
                            i++;
                            break;
                        case 'r':
                            result.Append('\r');
                            i++;
                            break;
                        case 't':
                            result.Append('\t');
                            i++;
                            break;
                        case 'b':
                            result.Append('\b');
                            i++;
                            break;
                        case 'f':
                            result.Append('\f');
                            i++;
                            break;
                        case 'u': // Handle Unicode escape sequences (e.g., \u0022)
                            if (i + 5 < input.Length)
                            {
                                string hex = input.Substring(i + 2, 4);
                                if (int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out int unicodeChar))
                                {
                                    result.Append((char)unicodeChar);
                                    i += 5;
                                }
                                else
                                {
                                    result.Append('\\');
                                }
                            }
                            else
                            {
                                result.Append('\\');
                            }
                            break;
                        default:
                            result.Append('\\');
                            break;
                    }
                }
                else
                {
                    result.Append(input[i]);
                }
            }

            return result.ToString();
        }

        // Helper method to normalize code (remove whitespace, comments)
        public static string NormalizeCode(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return string.Empty;

            // Split into lines, trim, remove empty lines and comments
            var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Select(line => line.Trim())
                .Where(line => !string.IsNullOrEmpty(line) && !line.StartsWith("'") && !line.StartsWith("//"))
                .ToArray();

            return string.Join(" ", lines).Replace("  ", " ");
        }

        // Helper method to calculate similarity (Levenshtein distance-based)
        public static double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0.0;

            int[,] matrix = new int[source.Length + 1, target.Length + 1];

            for (int i = 0; i <= source.Length; i++)
                matrix[i, 0] = i;
            for (int j = 0; j <= target.Length; j++)
                matrix[0, j] = j;

            for (int i = 1; i <= source.Length; i++)
            {
                for (int j = 1; j <= target.Length; j++)
                {
                    int cost = (source[i - 1] == target[j - 1]) ? 0 : 1;
                    matrix[i, j] = Math.Min(
                        Math.Min(matrix[i - 1, j] + 1, matrix[i, j - 1] + 1),
                        matrix[i - 1, j - 1] + cost);
                }
            }

            int maxLength = Math.Max(source.Length, target.Length);
            return 1.0 - (double)matrix[source.Length, target.Length] / maxLength;
        }

        public static string ExtractFunctionName(string aiResponse)
        {
            // Parse function name from AI response, expecting format like "static void functionname"
            var lines = aiResponse.Split('\n');
            foreach (var line in lines)
            {
                // Ignore comments and empty lines
                if (!string.IsNullOrWhiteSpace(line) && !line.StartsWith("//"))
                {
                    // Split the line by whitespace to handle modifiers like "static void"
                    string[] parts = line.Trim().Split(' ');
                    foreach (string part in parts)
                    {
                        // Look for the part before any parameters, e.g., "functionname" in "functionname(int"
                        int end = part.IndexOf('(');
                        string candidate = end > 0 ? part.Substring(0, end).Trim() : part.Trim();
                        if (!string.IsNullOrEmpty(candidate) && 
                            !candidate.Equals("static", StringComparison.OrdinalIgnoreCase) && 
                            !candidate.Equals("void", StringComparison.OrdinalIgnoreCase) && 
                            !candidate.Equals("public", StringComparison.OrdinalIgnoreCase) && 
                            !candidate.Equals("private", StringComparison.OrdinalIgnoreCase) && 
                            !candidate.Equals("protected", StringComparison.OrdinalIgnoreCase))
                        {
                            return candidate;
                        }
                    }
                }
            }
            return null;
        }

        //Should probably be in a separate document modification utility class, but keeping it here for now
        public static string InsertAtCursorOrAppend(string currentCode, string aiCode, Document activeDoc)
        {
            var lines = currentCode.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

            if (activeDoc != null)
            {
                var selection = activeDoc.Selection as TextSelection;
                int cursorLine = selection?.ActivePoint.Line ?? -1;

                if (cursorLine > 0 && cursorLine <= lines.Count + 1)
                {
                    lines.Insert(cursorLine - 1, aiCode); // Insert at cursor line
                }
                else
                {
                    lines.Add(aiCode); // Append to end
                }
            }
            else
            {
                lines.Add(aiCode); // Append as last resort
            }

            return string.Join("\n", lines);
        }

        public static string ReplaceCodeBlock(string documentContent, int startIndex, int length, string newCode)
        {
            return documentContent.Remove(startIndex, length).Insert(startIndex, newCode);
        }

        public static double CalculateFastSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0.0;
            int[] sourceFreq = new int[128];
            int[] targetFreq = new int[128];
            int commonCount = 0;
            foreach (char c in source)
                if (c < 128) sourceFreq[c]++;
            foreach (char c in target)
                if (c < 128) targetFreq[c]++;
            for (int i = 0; i < 128; i++)
                commonCount += Math.Min(sourceFreq[i], targetFreq[i]);
            return (double)commonCount / Math.Max(source.Length, target.Length);
        }
    }
}
