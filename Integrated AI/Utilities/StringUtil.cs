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

            // Remove multiline comments (/* ... */)
            code = Regex.Replace(code, @"/\*[^*]*\*+(?:[^/*][^*]*\*+)*/", string.Empty);

            // Split into lines, trim, remove empty lines, single-line comments, and inline comments
            var lines = code.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None)
                .Select(line =>
                {
                    // Remove inline comments (// ...)
                    int commentIndex = line.IndexOf("//");
                    if (commentIndex >= 0)
                        line = line.Substring(0, commentIndex);
                    return line.Trim();
                })
                .Where(line => !string.IsNullOrEmpty(line) && !line.StartsWith("'"))
                .ToArray();

            // Join with newlines to preserve structure
            return string.Join("\n", lines);
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

        public static string ReplaceCodeBlock(string documentContent, int startIndex, int startLine, int length, string newCode)
        {
            // Get the text of the line at startLine to determine base indentation
            int baseIndentation = 0;
            if (startLine > 0)
            {
                // Find the start of the line by counting line breaks
                int lineCount = 0;
                int lineStartIndex = 0;
                for (int i = 0; i < documentContent.Length; i++)
                {
                    if (documentContent[i] == '\n')
                    {
                        lineCount++;
                        if (lineCount == startLine)
                        {
                            lineStartIndex = i + 1;
                            break;
                        }
                    }
                }

                // Extract the line text
                if (lineCount == startLine && lineStartIndex < documentContent.Length)
                {
                    int lineEndIndex = documentContent.IndexOf('\n', lineStartIndex);
                    if (lineEndIndex == -1) lineEndIndex = documentContent.Length;
                    string lineText = documentContent.Substring(lineStartIndex, lineEndIndex - lineStartIndex).TrimEnd('\r');
                    baseIndentation = GetIndentPosition(lineText);
                }
            }

            WebViewUtilities.Log($"Base indentation for replacement: {baseIndentation} spaces");

            // Split newCode into lines
            string[] newCodeLines = newCode.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            // Find the minimum indentation in newCode (excluding empty lines) to preserve relative structure
            int minIndent = int.MaxValue;
            foreach (var line in newCodeLines)
            {
                if (!string.IsNullOrWhiteSpace(line))
                {
                    int indentCount = GetIndentPosition(line);
                    minIndent = Math.Min(minIndent, indentCount);
                }
            }
            if (minIndent == int.MaxValue) minIndent = 0; // Handle case where newCode is empty or all lines are empty

            // Adjust newCode: apply baseIndentation to all lines, preserving relative indentation
            for (int i = 0; i < newCodeLines.Length; i++)
            {
                if (!string.IsNullOrWhiteSpace(newCodeLines[i]))
                {
                    // Remove only the minimum indentation to preserve relative structure
                    string trimmedLine = newCodeLines[i].Substring(Math.Min(minIndent, newCodeLines[i].Length));
                    // Prepend baseIndentation as spaces
                    string indentString = new string(' ', baseIndentation);

                    // Preserve first line's indentation
                    if (i == 0)
                    {
                        indentString = ""; // Keep the first line's indentation as is
                    }

                    newCodeLines[i] = indentString + trimmedLine;
                    // Optional debug: Log indentation for verification (remove in production)
                    // System.Diagnostics.Debug.WriteLine($"Line {i}: OriginalIndent={GetIndentPosition(newCodeLines[i])}, NewIndent={GetIndentPosition(indentString + trimmedLine)}");
                }
                else
                {
                    newCodeLines[i] = ""; // Preserve empty lines without indentation
                }
            }
            string adjustedNewCode = string.Join(Environment.NewLine, newCodeLines);

            // Perform the replacement
            return documentContent.Remove(startIndex, length).Insert(startIndex, adjustedNewCode);
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

        // Integrated AI/Utilities/StringUtil.cs
        public static string ReplaceOrAddCode(DTE2 dte, string currentCode, string aiCode, Document activeDoc, ChooseCodeWindow.ReplacementItem chosenItem = null, DiffUtility.DiffContext context = null)
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
                    int startLine = targetFunction.StartPoint.Line;
                    
                    if (startIndex >= 0)
                    {
                        // Remove comments (C# // or VB ' or REM) above the function definition
                        aiCode = RemoveCommentsAboveFunction(aiCode);
                        context.NewCodeStartIndex = startIndex;
                        return StringUtil.ReplaceCodeBlock(currentCode, startIndex, startLine, targetFunction.FullCode.Length, aiCode);
                    }
                }
            }

            // For "Selection" or unmatched functions, check for highlighted text
            // But first it may be a chosenItem that is a new function or new file, which gets special handling
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
                    // Pass -1 to ignore base indentation, as we are replacing the selected text directly
                    context.NewCodeStartIndex = startIndex;
                    return StringUtil.ReplaceCodeBlock(currentCode, startIndex, -1, length, aiCode);
                }
            }

            // Fallback: Insert at cursor position or append if selection was empty or other errors
            return StringUtil.InsertAtCursorOrAppend(currentCode, aiCode, activeDoc);
        }

        // Helper method to remove comments above function definition
        private static string RemoveCommentsAboveFunction(string code)
        {
            // Split code into lines
            var lines = code.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();
            if (lines.Count == 0) return code;

            // Find the function signature (simplified: look for "function", "sub", or access modifiers like "public", "private")
            int functionStartIndex = -1;
            for (int i = 0; i < lines.Count; i++)
            {
                string trimmedLine = lines[i].TrimStart();
                if (trimmedLine.StartsWith("public ") || trimmedLine.StartsWith("private ") ||
                    trimmedLine.StartsWith("protected ") || trimmedLine.StartsWith("Function ") ||
                    trimmedLine.StartsWith("Sub "))
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
                        string.IsNullOrWhiteSpace(trimmedLine))
                    {
                        continue; // Skip single-line comments or empty lines
                    }
                    if (trimmedLine.StartsWith("/*"))
                    {
                        inCommentBlock = true;
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

                // Add non-comment lines or lines after function signature
                cleanedLines.Add(lines[i]);
            }

            // Rejoin lines and return
            return string.Join(Environment.NewLine, cleanedLines);
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
                System.Diagnostics.Debug.WriteLine("Invalid input: DTE, Document, or code is null/empty.");
                return (false, null, false);
            }

            try
            {
                // Determine language based on file extension
                string extension = Path.GetExtension(activeDoc.FullName)?.ToLowerInvariant() ?? ".cs";
                if (extension != ".cs")
                {
                    System.Diagnostics.Debug.WriteLine($"Unsupported file extension: {extension}");
                    return (false, null, false);
                }

                string normalizedCode = NormalizeCode(code);
                if (string.IsNullOrEmpty(normalizedCode))
                {
                    return (false, null, false);
                }

                // --- Improved Full File Detection ---
                // A full file is likely to have using statements, a namespace, or be a complete class/struct/interface block.
                string trimmedNormalizedCode = normalizedCode.Trim();
                bool hasUsing = trimmedNormalizedCode.StartsWith("using ");
                bool hasNamespace = trimmedNormalizedCode.Contains("namespace "); // Check for "namespace " to avoid false positives
        
                // Check for top-level type definitions
                bool hasTopLevelType = trimmedNormalizedCode.StartsWith("public class") ||
                                        trimmedNormalizedCode.StartsWith("internal class") ||
                                        trimmedNormalizedCode.StartsWith("class ") ||
                                        trimmedNormalizedCode.StartsWith("public struct") ||
                                        trimmedNormalizedCode.StartsWith("public interface");

                // Check for balanced braces to ensure it's a complete block of code
                int braceCount = 0;
                foreach (char c in normalizedCode)
                {
                    if (c == '{') braceCount++;
                    else if (c == '}') braceCount--;
                }
        
                // A full file is a complete block that contains strong indicators like namespace/using or is a top-level type.
                if (braceCount == 0 && (hasNamespace || hasUsing || hasTopLevelType))
                {
                    System.Diagnostics.Debug.WriteLine("Detected full file based on using/namespace/class and balanced braces.");
                    // This is a full file, so it's not a single function. Return immediately.
                    return (false, null, true);
                }

                // --- Method Detection ---
                // If we got here, it's not a full file. Now, check if it's a method.
                // NOTE: Use the original 'code' for regex, as normalization can sometimes affect complex signatures.
                var methodRegex = new Regex(
                    // Matches access modifier, static, return type, method name, and parameters.
                    @"^\s*(public|private|protected|internal|protected internal|private protected)?\s*(static)?\s*([\w\.<>\[\]\?]+)\s+([\w\d_]+)\s*\([^)]*\)\s*\{",
                    RegexOptions.Multiline);
            
                MatchCollection methodMatches = methodRegex.Matches(code);

                if (methodMatches.Count > 0)
                {
                    // We consider it a "function" context if it looks like one or more complete methods.
                    // For simplicity, we'll analyze the first one found.
                    var match = methodMatches[0];
                    string methodName = match.Groups[4].Value;

                    // Simple validation to avoid matching control structures like `if () {`
                    if (!CSharpKeywords.Contains(methodName, StringComparer.OrdinalIgnoreCase))
                    {
                         // Check for balanced braces on the whole snippet to confirm it's likely a complete method
                        int methodBraceCount = 0;
                        foreach (char c in code)
                        {
                            if (c == '{') methodBraceCount++;
                            else if (c == '}') methodBraceCount--;
                        }

                        if (methodBraceCount == 0)
                        {
                            System.Diagnostics.Debug.WriteLine($"Detected single/primary method: {methodName}");
                            return (true, methodName, false);
                        }
                    }
                }

                // If it's neither a full file nor a recognized method, it's a snippet.
                System.Diagnostics.Debug.WriteLine("Code is not a full file or a recognized method. Treating as a snippet.");
                return (false, null, false);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Exception in AnalyzeCodeBlock: {ex.Message}\nStackTrace: {ex.StackTrace}");
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
    }
}
