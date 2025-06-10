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

        public static string ExtractFunctionFullName(string aiResponse)
        {
            // Parse function full name from AI response (adjust based on AI format)
            // Example: Extract from method signature line
            var lines = aiResponse.Split('\n');
            foreach (var line in lines)
            {
                if (line.Contains("(") && !line.StartsWith("//"))
                {
                    // Assume AI preserves enough of the signature to match FullName
                    // This is approximate; refine based on your AI's output
                    int start = line.IndexOf(' ') + 1;
                    int end = line.IndexOf('(');
                    if (start > 0 && end > start)
                    {
                        string methodName = line.Substring(start, end - start).Trim();
                        // FullName includes namespace/class, so we need context
                        // Simplistic approach: return method name and rely on context matching
                        return methodName;
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

        // Integrated AI/Utilities/StringUtil.cs
        public static int FindBestMatchStartLine(string documentContent, string targetContent)
        {
            // Log inputs for debugging
            WebViewUtilities.Log($"FindBestMatchStartLine called. Document length: {documentContent?.Length}, Target length: {targetContent?.Length}");

            if (string.IsNullOrEmpty(documentContent) || string.IsNullOrEmpty(targetContent))
            {
                WebViewUtilities.Log("Returning -1 due to null or empty input.");
                return -1;
            }

            // Normalize inputs to reduce noise (e.g., whitespace differences)
            string normalizedDocument = NormalizeCode(documentContent);
            string normalizedTarget = NormalizeCode(targetContent);

            // Log first 100 chars to diagnose exact match issues
            WebViewUtilities.Log($"Document (first 100): {documentContent.Substring(0, Math.Min(100, documentContent.Length))}");
            WebViewUtilities.Log($"Target (first 100): {targetContent.Substring(0, Math.Min(100, targetContent.Length))}");

            // Check for exact match on normalized strings
            int exactIndex = normalizedDocument.IndexOf(normalizedTarget, StringComparison.OrdinalIgnoreCase);
            if (exactIndex >= 0)
            {
                WebViewUtilities.Log($"Exact match found at index: {exactIndex}");
                return exactIndex;
            }
            WebViewUtilities.Log("No exact match found. Proceeding to line-based comparison.");

            // Split document and target into lines
            var documentLines = documentContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var targetLines = targetContent.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            int targetLineCount = targetLines.Length;
            if (targetLineCount == 0 || documentLines.Length < targetLineCount)
            {
                WebViewUtilities.Log($"Invalid line counts. Doc lines: {documentLines.Length}, Target lines: {targetLineCount}. Returning -1.");
                return -1;
            }

            // Cache line offsets, accounting for actual line endings
            int[] lineOffsets = new int[documentLines.Length];
            int offset = 0;
            for (int j = 0; j < documentLines.Length; j++)
            {
                lineOffsets[j] = offset;
                offset += documentLines[j].Length + (j < documentLines.Length - 1 ? Environment.NewLine.Length : 0);
            }

            int bestIndex = -1;
            double bestSimilarity = 0.7; // Minimum similarity threshold
            int maxIterations = Math.Min(1000, documentLines.Length - targetLineCount + 1); // Cap iterations

            WebViewUtilities.Log($"Starting line-based comparison. Document lines: {documentLines.Length}, Target lines: {targetLineCount}, Max iterations: {maxIterations}");

            // Iterate through each line as the starting point
            for (int i = 0; i < maxIterations; i++)
            {
                // Build a window of text with exactly the same number of lines as target
                string[] windowLines = documentLines.Skip(i).Take(targetLineCount).ToArray();
                if (windowLines.Length < targetLineCount)
                    continue;
                string window = string.Join(Environment.NewLine, windowLines);

                // Check for exact line match
                bool exactLineMatch = true;
                for (int j = 0; j < targetLineCount; j++)
                {
                    if (windowLines[j].Trim() != targetLines[j].Trim())
                    {
                        exactLineMatch = false;
                        break;
                    }
                }
                if (exactLineMatch)
                {
                    WebViewUtilities.Log($"Exact line match at line {i}, index {lineOffsets[i]}");
                    return lineOffsets[i];
                }

                // Calculate similarity only for the exact line count
                double similarity = CalculateFastSimilarity(window, targetContent);
                if (i % 100 == 0) // Log periodically to avoid flooding
                {
                    WebViewUtilities.Log($"Line {i}, Similarity: {similarity}, Best index so far: {bestIndex}, Best similarity: {bestSimilarity}");
                }
                if (similarity > bestSimilarity)
                {
                    bestSimilarity = similarity;
                    bestIndex = lineOffsets[i];
                    WebViewUtilities.Log($"New best match at line {i}, index {bestIndex}, similarity {similarity}");
                }
                // Early exit for near-perfect match
                if (similarity > 0.95)
                {
                    WebViewUtilities.Log($"Early exit due to high similarity ({similarity}) at line {i}");
                    break;
                }
            }

            if (bestIndex == -1)
            {
                WebViewUtilities.Log("No suitable match found. Returning -1.");
            }
            else
            {
                WebViewUtilities.Log($"Line-based comparison complete. Best index: {bestIndex}, Best similarity: {bestSimilarity}");
            }
            return bestIndex;
        }

        // Helper method to detect actual line ending
        private static string GetLineEnding(string content, int position)
        {
            if (position + 1 < content.Length && content[position] == '\r' && content[position + 1] == '\n')
                return "\r\n";
            if (position < content.Length && content[position] == '\n')
                return "\n";
            if (position < content.Length && content[position] == '\r')
                return "\r";
            return "";
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
