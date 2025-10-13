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
using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public static class StringUtil
    {
        // Custom method to unescape JSON-encoded strings
        public static string UnescapeJsonString(string input)
        {
            try
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
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in UnescapeJsonString: {ex.Message}");
                return input; // Return original input if error occurs
            }
        }

        public static string NormalizeCode(string code)
        {
            if (string.IsNullOrEmpty(code)) return string.Empty;
            // Simple normalization: remove all whitespace characters (spaces, tabs, newlines, etc.)
            // This makes comparisons robust against formatting differences.
            return new string(code.Where(c => !char.IsWhiteSpace(c)).ToArray());
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

        public static bool IsWordCharacter(char c)
        {
            return char.IsLetterOrDigit(c) || c == '_';
        }

        public static bool TryGetWordAtCursor(Document activeDoc, out string word)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            word = null;

            if (!(activeDoc?.Object("TextDocument") is TextDocument textDoc)) return false;

            var cursorPoint = textDoc.Selection.ActivePoint;
            var editPoint = cursorPoint.CreateEditPoint();
            string lineText = editPoint.GetLines(cursorPoint.Line, cursorPoint.Line + 1);

            int cursorIndex = cursorPoint.LineCharOffset - 1;
            if (cursorIndex < 0 || cursorIndex >= lineText.Length || !IsWordCharacter(lineText[cursorIndex]))
            {
                return false;
            }

            int start = cursorIndex;
            while (start > 0 && IsWordCharacter(lineText[start - 1]))
            {
                start--;
            }

            int end = cursorIndex;
            while (end < lineText.Length - 1 && IsWordCharacter(lineText[end + 1]))
            {
                end++;
            }

            word = lineText.Substring(start, end - start + 1);
            return !string.IsNullOrWhiteSpace(word);
        }

        // Helper method to remove comments above function definition and header/footer
        public static string RemoveHeaderFooterComments(string code)
        {
            try
            {
                // Split code into lines
                var lines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
                if (lines.Count == 0) return code;

                // Find the function signature (look for function/sub keywords or access modifiers)
                int functionStartIndex = -1;
                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmedLine = lines[i].TrimStart();
                    // Use case-insensitive comparison for keywords to support both C# and VB
                    if (trimmedLine.StartsWith("public ", StringComparison.OrdinalIgnoreCase) ||
                        trimmedLine.StartsWith("private ", StringComparison.OrdinalIgnoreCase) ||
                        trimmedLine.StartsWith("protected ", StringComparison.OrdinalIgnoreCase) ||
                        trimmedLine.StartsWith("internal ", StringComparison.OrdinalIgnoreCase) ||
                        trimmedLine.StartsWith("Function ", StringComparison.OrdinalIgnoreCase) ||
                        trimmedLine.StartsWith("Sub ", StringComparison.OrdinalIgnoreCase))
                    {
                        functionStartIndex = i;
                        break;
                    }
                }

                // If no function signature found, return original code
                if (functionStartIndex == -1) return code;

                // Remove comments (C# // or VB ' or REM) above the function signature
                var cleanedLines = new List<string>();
                bool inCommentBlock = false;
                for (int i = 0; i < lines.Count; i++)
                {
                    string trimmedLine = lines[i].TrimStart();

                    // Skip if we're before the function and it's a comment
                    if (i < functionStartIndex)
                    {
                        if (trimmedLine.StartsWith("//") || trimmedLine.StartsWith("'") ||
                            trimmedLine.StartsWith("REM ", StringComparison.OrdinalIgnoreCase) ||
                            trimmedLine.StartsWith("---") ||
                            string.IsNullOrWhiteSpace(trimmedLine))
                        {
                            continue; // Skip single-line comments or empty lines
                        }
                        if (trimmedLine.StartsWith("/*"))
                        {
                            inCommentBlock = true;
                            // Continue if the entire block is on one line and ends here
                            if (trimmedLine.Contains("*/"))
                            {
                                inCommentBlock = false;
                            }
                            continue;
                        }
                        if (inCommentBlock)
                        {
                            if (trimmedLine.Contains("*/"))
                            {
                                inCommentBlock = false;
                            }
                            continue;
                        }
                    }

                    // Add non-comment lines or lines at/after the function signature
                    // But also check if we are exiting a comment block on the same line we are adding
                    if (inCommentBlock && trimmedLine.Contains("*/"))
                    {
                        inCommentBlock = false;
                    }

                    // Remove footer comments (e.g., "---") only if they are the last line
                    if (i == lines.Count - 1 && trimmedLine.StartsWith("---"))
                    {
                        continue;
                    }

                    // Only add the line if we are not inside a comment block
                    if (!inCommentBlock)
                    {
                        cleanedLines.Add(lines[i]);
                    }
                }

                // Rejoin lines and return
                return string.Join(Environment.NewLine, cleanedLines);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in RemoveHeaderFooterComments: {ex.Message}");
                return code; // Return original code if error occurs
            }
        }



        // Calculates the indent position of a line based on leading whitespace.
        // Each tab is counted as 4 spaces for consistency.
        // Returns the total indent position as an integer.
        public static int GetIndentPosition(string line)
        {
            if (string.IsNullOrEmpty(line))
            {
                return 0;
            }

            int indent = 0;
            foreach (char c in line)
            {
                if (c == ' ')
                {
                    indent++;
                }
                else if (c == '\t')
                {
                    indent += 4; // Assume tab equals 4 spaces
                }
                else
                {
                    break; // Stop at first non-whitespace character
                }
            }

            return indent;
        }

        public static string RemoveBaseIndentation(string codeSnippet)
        {
            try
            {
                if (string.IsNullOrEmpty(codeSnippet))
                {
                    return codeSnippet;
                }

                var lines = codeSnippet.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
                var minIndent = int.MaxValue;

                // Determine the minimum indentation of non-empty lines
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        var indent = GetIndentPosition(line);
                        if (indent < minIndent)
                        {
                            minIndent = indent;
                        }
                    }
                }

                if (minIndent == int.MaxValue || minIndent == 0)
                {
                    return codeSnippet; // No common indentation found or already at the root
                }

                var processedLines = new List<string>();
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        // Determine the current line's original indentation to decide how to handle it.
                        int currentIndent = 0;
                        int charsToRemove = 0;
                        bool inLeadingWhitespace = true;

                        for (int i = 0; i < line.Length && inLeadingWhitespace; i++)
                        {
                            if (line[i] == ' ')
                            {
                                currentIndent++;
                            }
                            else if (line[i] == '\t')
                            {
                                // Assuming a tab width of 4 for indentation calculation purposes.
                                currentIndent += 4;
                            }
                            else
                            {
                                inLeadingWhitespace = false;
                            }

                            if (inLeadingWhitespace)
                            {
                                // If the running total of whitespace characters is less than or equal to the minimum indent,
                                // we can mark this character for removal.
                                if (currentIndent <= minIndent)
                                {
                                    charsToRemove = i + 1;
                                }
                            }
                        }
                        processedLines.Add(line.Substring(charsToRemove));
                    }
                    else
                    {
                        processedLines.Add(line);
                    }
                }

                return string.Join(Environment.NewLine, processedLines);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in RemoveBaseIndentation: {ex.Message}");
                return codeSnippet; // Return original code if error occurs
            }
        }

        /// <summary>
        /// Fixes extra indentation in code blocks when the first line is correct but all other lines have one extra level of indentation.
        /// </summary>
        /// <param name="code"></param>
        /// <returns></returns>
        public static string FixExtraIndentation(string code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return code;

            var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None).ToList();

            if (lines.Count <= 1)
                return code;

            // Get the indentation of the first line (the function signature)
            string firstLineIndentation = lines[0].Substring(0, lines[0].Length - lines[0].TrimStart().Length);

            // If the second line exists and is not just whitespace, get its indentation
            string secondLine = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l) && l != lines[0]);
            if (secondLine == null) return code; // No other non-empty lines

            string secondLineIndentation = secondLine.Substring(0, secondLine.Length - secondLine.TrimStart().Length);

            // Determine the "extra" indentation that needs to be removed from subsequent lines
            // This happens when the second line's indent is greater than the first's
            if (secondLineIndentation.Length > firstLineIndentation.Length && secondLineIndentation.StartsWith(firstLineIndentation))
            {
                string extraIndentation = secondLineIndentation.Substring(firstLineIndentation.Length);

                // Rebuild the code string, removing the extra indentation from all lines after the first
                var correctedLines = new System.Text.StringBuilder();
                correctedLines.AppendLine(lines[0]); // Add the first line as-is

                for (int i = 1; i < lines.Count; i++)
                {
                    if (lines[i].StartsWith(extraIndentation))
                    {
                        correctedLines.AppendLine(lines[i].Substring(extraIndentation.Length));
                    }
                    else
                    {
                        correctedLines.AppendLine(lines[i]);
                    }
                }
                return correctedLines.ToString().TrimEnd();
            }

            // If the pattern isn't found, return the original code
            return code;
        }
    }    
}