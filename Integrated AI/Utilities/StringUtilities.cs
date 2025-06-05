using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Integrated_AI.Utilities
{
    public static class StringUtilities
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
    }
}
