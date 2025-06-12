using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
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

        public static string ReplaceOrAddCode(DTE2 dte, string currentCode, string aiCode, Document activeDoc, ChooseCodeWindow.ReplacementItem chosenItem = null)
        {
            var (isFunction, functionName, isFullFile) = (false, string.Empty, false);

            // Analyze the code block to determine its type, if there is no chosen item provided
            if (chosenItem == null)
            {
                (isFunction, functionName, isFullFile) = AnalyzeCodeBlock(dte, activeDoc, aiCode);
            }

            else
            {
                isFunction = chosenItem?.Type == "function" || chosenItem?.Type == "new_function";
                isFullFile = chosenItem?.Type == "file" || chosenItem?.Type == "new_file" || chosenItem?.Type == "opened_file";
                functionName = chosenItem?.Function?.Name ?? string.Empty;
            }

            if (isFullFile)
            {
                return aiCode; // Replace entire document for "file" type
            }

            if (isFunction)
            {
                // For C# functions, find the function by name (not the FullName)
                var functions = CodeSelectionUtilities.GetFunctionsFromDocument(activeDoc);
                var targetFunction = functions.FirstOrDefault(f => f.DisplayName == functionName);

                if (targetFunction != null)
                {
                    // Find the function's current code in the document
                    int startIndex = currentCode.IndexOf(targetFunction.FullCode, StringComparison.Ordinal);
                    if (startIndex >= 0)
                    {
                        return StringUtil.ReplaceCodeBlock(currentCode, startIndex, targetFunction.FullCode.Length, aiCode);
                    }
                }
            }

            // For "Selection" or unmatched functions, check for highlighted text
            //But first it may be a chosenItem that is a new function or new file, which gets special handling
            if (chosenItem != null && (chosenItem.Type == "new_function" || chosenItem.Type == "new_file"))
            {
                //TODO: special handling for new function or file
                //New function will get the last existing function in the activeDoc and insert the new function after it
                //New file will create a new file in a user-selected location within the solution where aiCode will be inserted
            }

            var selection = activeDoc.Selection as TextSelection;
            if (selection != null)
            {
                if (!selection.IsEmpty) // Check if text is highlighted
                {
                    // Get the start and end points of the selection
                    int startIndex = selection.TopPoint.AbsoluteCharOffset;
                    int length = selection.BottomPoint.AbsoluteCharOffset - startIndex;
                    return StringUtil.ReplaceCodeBlock(currentCode, startIndex, length, aiCode);
                }
            }

            // Fallback: Insert at cursor position or append if selection was empty or other errors
            return StringUtil.InsertAtCursorOrAppend(currentCode, aiCode, activeDoc);
        }

        private static readonly string[] CSharpKeywords = 
        {
            "if", "else", "while", "for", "foreach", "switch", "case", "do", 
            "return", "break", "continue", "try", "catch", "finally", "throw", 
            "new", "class", "struct", "interface", "enum", "namespace", "using"
        };

        public static (bool IsFunction, string FunctionName, bool IsFullFile) AnalyzeCodeBlock(DTE2 dte, Document activeDoc, string code)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dte == null || activeDoc == null || string.IsNullOrEmpty(code))
            {
                // Log error for debugging
                System.Diagnostics.Debug.WriteLine("Invalid input: DTE, Document, or code is null/empty.");
                //MessageBox.Show("Invalid input: DTE, Document, or code is null/empty.", "Debug Info", MessageBoxButton.OK, MessageBoxImage.Error);
                return (false, null, false);
            }

            try
            {
                // Determine language based on file extension
                string extension = Path.GetExtension(activeDoc.FullName)?.ToLowerInvariant() ?? ".cs";
                System.Diagnostics.Debug.WriteLine($"Processing file with extension: {extension}");

                if (extension != ".cs")
                {
                    System.Diagnostics.Debug.WriteLine($"Unsupported file extension: {extension}");
                    //MessageBox.Show($"Unsupported file extension: {extension}", "Debug Info", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return (false, null, false);
                }

                // Normalize code for analysis
                string normalizedCode = Regex.Replace(code, @"\s+", " ").Trim();
                System.Diagnostics.Debug.WriteLine($"Normalized code length: {normalizedCode.Length}");

                // Check for full file by detecting class with proper braces
                bool isFullFile = false;
                var classRegex = new Regex(@"\bclass\s+\w+\s*\{([^{}]+|\{[^{}]*\})*\}", RegexOptions.Singleline);
                if (classRegex.IsMatch(code))
                {
                    isFullFile = true;
                    System.Diagnostics.Debug.WriteLine("Detected full file with class definition.");
                }

                // Detect methods (functions in C#)
                var methodRegex = new Regex(
                    @"\b(public|private|protected|internal)?\s*(static)?\s*(\w+\s+|\w+\<\w+\>\s+)?(\w+)\s*\([^)]*\)\s*\{",
                    RegexOptions.Multiline);
                var methodMatches = methodRegex.Matches(code);

                int methodCount = methodMatches.Count;
                System.Diagnostics.Debug.WriteLine($"Found {methodCount} methods in code.");

                // Log analysis results
                string debugInfo = $"File analysis:\n" +
                                 $"Class detected: {isFullFile}\n" +
                                 $"Method count: {methodCount}\n" +
                                 $"Is full file: {isFullFile}";
                System.Diagnostics.Debug.WriteLine(debugInfo);
                //MessageBox.Show(debugInfo, "Debug Info", MessageBoxButton.OK, MessageBoxImage.Information);

                // Handle single method
                if (!isFullFile && methodCount >= 1)
                {
                    var match = methodMatches[0];
                    string methodName = match.Groups[4].Value;

                    // Validate method name is not a keyword
                    if (CSharpKeywords.Contains(methodName, StringComparer.OrdinalIgnoreCase))
                    {
                        System.Diagnostics.Debug.WriteLine($"Invalid method name detected (keyword): {methodName}");
                        //MessageBox.Show($"Invalid method name detected: {methodName}", "Debug Info", MessageBoxButton.OK, MessageBoxImage.Warning);
                        return (false, null, false);
                    }

                    if (methodCount == 1)
                    {
                        System.Diagnostics.Debug.WriteLine($"Single method detected: {methodName}");
                    }
                    else
                    {
                        System.Diagnostics.Debug.WriteLine($"Multiple methods detected, using first: {methodName}");
                        System.Diagnostics.Debug.WriteLine($"List of methods: {string.Join(", ", methodMatches)}");
                    }

                    return (true, methodName, false);
                }

                // Check for full file or snippet. Only gets here if it wasn't a function
                if (isFullFile)
                {
                    return (false, null, isFullFile);
                }
                else
                {
                    return (false, null, false);
                }
            }
            catch (Exception ex)
            {
                // Log other exceptions with stack trace
                System.Diagnostics.Debug.WriteLine($"Exception in AnalyzeCodeBlock: {ex.Message}\nStackTrace: {ex.StackTrace}");
                //MessageBox.Show($"Exception: {ex.Message}", "Debug Info", MessageBoxButton.OK, MessageBoxImage.Error);
                return (false, null, false);
            }
        }

        // Maps language to file extension
        public static string GetFileExtension(string language)
        {
            switch (language)
            {
                case "CSharp": return ".cs";
                case "JavaScript": return ".js";
                case "C/C++": return ".cpp";
                case "Python": return ".py";
                case "XAML": return ".xaml";
                default: return ".txt";
            }
        }
    }
}
