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

        public static string InsertAtCursorOrAppend(string currentCode, string aiCode, Document activeDoc)
        {
            try
            {
                var lines = currentCode.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

                if (activeDoc?.Selection is TextSelection selection)
                {
                    var point = selection.ActivePoint;
                    int lineIndex = point.Line - 1; // 1-based to 0-based index

                    // Use VirtualCharOffset to get the visual column, which correctly handles "virtual space".
                    int virtualColumn = point.VirtualCharOffset;

                    // The number of spaces for the indent is the column number minus one.
                    // Ensure it's not negative, just in case.
                    int indentSize = Math.Max(0, virtualColumn - 1);

                    // Create a string of spaces for the indentation prefix.
                    // This ensures visual alignment regardless of the editor's tab/space settings.
                    string indentPrefix = new string(' ', indentSize);

                    // Ensure we don't try to insert outside the bounds of the list.
                    if (lineIndex >= 0 && lineIndex <= lines.Count)
                    {
                        var aiCodeLines = aiCode.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        var indentedAiCodeLines = aiCodeLines.Select(line => indentPrefix + line);

                        // Insert the new indented lines. This pushes the current line and subsequent lines down.
                        lines.InsertRange(lineIndex, indentedAiCodeLines);

                        return string.Join("\n", lines);
                    }
                }

                // Fallback behavior: if we can't determine the cursor or it's in an invalid
                // position, append the code to the end.
                lines.Add(aiCode);
                return string.Join("\n", lines);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in InsertAtCursorOrAppend: {ex.Message}");
                MessageBox.Show($"Error inserting code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return currentCode; // Return unchanged code if error occurs
            }
        }

        public static string ReplaceCodeBlock(string documentContent, int startIndex, int startLine, int length, string newCode, bool fixDoubleIndent)
        {
            try
            {
                // Get the text of the line at startLine to determine base indentation
                int baseIndentation = 0;
                if (startLine > 0)
                {
                    // Find the start of the line by counting line breaks
                    int lineCount = 0;
                    int lineStartIndex = 0;
                    int prevLineStartIndex = 0;
                    for (int i = 0; i < documentContent.Length; i++)
                    {
                        if (documentContent[i] == '\n')
                        {
                            lineCount++;
                            prevLineStartIndex = lineStartIndex;
                            lineStartIndex = i + 1;
                            if (lineCount == startLine)
                            {
                                break;
                            }
                        }
                    }

                    // Extract the line text
                    if (lineCount == startLine && prevLineStartIndex < documentContent.Length)
                    {
                        int lineEndIndex = documentContent.IndexOf('\n', prevLineStartIndex);
                        if (lineEndIndex == -1) lineEndIndex = documentContent.Length;
                        string lineText = documentContent.Substring(prevLineStartIndex, lineEndIndex - prevLineStartIndex).TrimEnd('\r');
                        WebViewUtilities.Log($"Line {startLine} text for indentation: '{lineText}'");
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

                        // Preserve first line's indentation, only if it's an existing function replacement
                        // This does not apply to new functions or snippet/selection replacements
                        // Should technically fix this a different way but whatever
                        if (i == 0 && fixDoubleIndent)
                        {
                            WebViewUtilities.Log("Preserving first line's indentation for function replacement.");
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
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in ReplaceCodeBlock: {ex.Message}");
                MessageBox.Show($"Error replacing code block: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                
                return documentContent; // Return original content if error occurs
            }
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

        public static string CreateDocumentContent(DTE2 dte, string currentCode, string aiCode, Document activeDoc, ChooseCodeWindow.ReplacementItem chosenItem = null, DiffUtility.DiffContext context = null)
        {
            try
            {
                var (isFunction, functionName, isFullFile) = (false, string.Empty, false);

                // Determine the language based on the active document's extension
                string extension = Path.GetExtension(activeDoc.FullName).ToLowerInvariant();
                bool isVB = extension == ".vb";

                // Analyze the code block to determine its type, if there is no chosen item provided
                if (chosenItem == null)
                {
                    (isFunction, functionName, isFullFile) = AnalyzeCodeBlock(dte, activeDoc, aiCode);
                }
                else
                {
                    isFunction = chosenItem?.Type == "function" || chosenItem?.Type == "new_function";
                    isFullFile = chosenItem?.Type == "file" || chosenItem?.Type == "new_file" || chosenItem?.Type == "opened_file";
                    functionName = chosenItem?.DisplayName ?? string.Empty;
                }

                // Only return the AI code if it's a full file that isn't a new file
                if (isFullFile)
                {
                    if (chosenItem == null || chosenItem.Type != "new_file")
                    {
                        //MessageBox.Show("The AI response is a full file replacement. It will replace the entire document.", "Full File Replacement", MessageBoxButton.OK, MessageBoxImage.Information);
                        return aiCode; // Replace entire document for "file" type
                    }
                }

                if (isFunction)
                {
                    // For C# or VB functions, find the function by name (not the FullName)
                    var functions = CodeSelectionUtilities.GetFunctionsFromDocument(activeDoc);
                    var targetFunction = functions.FirstOrDefault(f => f.DisplayName == functionName);

                    if (targetFunction != null)
                    {
                        // Find the function's current code in the document
                        int startIndex = currentCode.IndexOf(targetFunction.FullCode, StringComparison.Ordinal);
                        int startLine = targetFunction.StartPoint.Line;

                        if (startIndex >= 0)
                        {
                            // Remove comments (C# // or VB ' or REM) above the function definition, also remove header/footer
                            aiCode = RemoveHeaderFooterComments(aiCode);
                            context.NewCodeStartIndex = startIndex;
                            return ReplaceCodeBlock(currentCode, startIndex, startLine, targetFunction.FullCode.Length, aiCode, true);
                        }
                    }
                    else
                    {
                        ChooseCodeWindow.ReplacementItem newFunctionItem = new ChooseCodeWindow.ReplacementItem { };
                        chosenItem = newFunctionItem; // Create a new item for the function
                        chosenItem.Type = "new_function"; // If function not found, treat as new function
                        WebViewUtilities.Log($"Function '{functionName}' not found in the document. It will be added as a new function.");
                        //MessageBox.Show($"Function '{functionName}' not found in the document. It will be added as a new function.", "Function Not Found", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }

                // Handle new function or new file cases
                if (chosenItem != null && (chosenItem.Type == "new_function" || chosenItem.Type == "new_file"))
                {
                    if (chosenItem.Type == "new_function")
                    {
                        // Get all functions in the document
                        var functions = CodeSelectionUtilities.GetFunctionsFromDocument(activeDoc);
                        if (functions.Any())
                        {
                            // Find the last function
                            var lastFunction = functions.OrderByDescending(f => f.StartPoint.Line).First();
                            int startIndex = currentCode.IndexOf(lastFunction.FullCode, StringComparison.Ordinal) + lastFunction.FullCode.Length;
                            int startLine = lastFunction.StartPoint.Line;

                            // Ensure two newlines after the last function if needed
                            if (startIndex < currentCode.Length)
                            {
                                currentCode = currentCode.Insert(startIndex, Environment.NewLine);
                                currentCode = currentCode.Insert(startIndex, Environment.NewLine);
                                startIndex += (Environment.NewLine.Length + Environment.NewLine.Length);
                            }

                            // Remove comments above the new function code
                            aiCode = RemoveHeaderFooterComments(aiCode);

                            // Use ReplaceCodeBlock to insert the new function with correct indentation
                            context.NewCodeStartIndex = startIndex;
                            return ReplaceCodeBlock(currentCode, startIndex, startLine, 0, aiCode, false);
                        }
                        else
                        {
                            // No functions in the document, append at the end with default indentation
                            WebViewUtilities.Log("No functions found in the document. Appending new function at the end.");
                            return InsertAtCursorOrAppend(currentCode, aiCode, activeDoc);
                        }
                    }
                    else if (chosenItem.Type == "new_file")
                    {
                        // Prompt user to select a location and file name
                        string newFilePath = FileUtil.PromptForNewFilePath(dte, isVB ? "vb" : "cs");
                        if (string.IsNullOrEmpty(newFilePath))
                        {
                            WebViewUtilities.Log("New file creation cancelled by user.");
                            return currentCode; // Return unchanged if cancelled
                        }

                        // Create and add the new file to the solution
                        FileUtil.CreateNewFileInSolution(dte, newFilePath, aiCode);
                        context.IsNewFile = true; // Indicate that a new file was created

                        // Since it's a new file, we don't modify the current document
                        return currentCode;
                    }
                }

                // For "Selection" or unmatched functions, check for highlighted text
                var selection = activeDoc.Selection as TextSelection;
                if (selection != null && !selection.IsEmpty) // Check if text is highlighted
                {
                    var textDocument = activeDoc.Object("TextDocument") as TextDocument;
                    if (textDocument != null)
                    {
                        var startPoint = textDocument.StartPoint.CreateEditPoint();
                        string textBeforeSelection = startPoint.GetText(selection.TopPoint);
                        int startIndex = textBeforeSelection.Length;
                        int length = selection.Text.Length;

                        // Get the line number for the start of the selection. This is crucial
                        // for allowing ReplaceCodeBlock to calculate the correct base indentation.
                        int startLine = selection.TopPoint.Line;

                        context.NewCodeStartIndex = startIndex;

                        return ReplaceCodeBlock(currentCode, startIndex, startLine, length, aiCode, true);
                    }
                }

                // Fallback: Insert at cursor position or append if selection was empty or other errors
                return StringUtil.InsertAtCursorOrAppend(currentCode, aiCode, activeDoc);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in CreateDocumentContent: {ex.Message}");
                MessageBox.Show($"Error creating document content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return currentCode; // Return unchanged code if error occurs
            }
        }

        // Helper method to remove comments above function definition and header/footer
        private static string RemoveHeaderFooterComments(string code)
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

        #region Code Analysis Regex and Keywords

        // A set of C# keywords that can precede a parenthesis, to avoid matching control flow blocks as functions.
        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "for", "foreach", "while", "do", "switch", "case", "lock", "using", "fixed", "catch", "finally"
        };

        // A set of VB.NET keywords for the same purpose.
        private static readonly HashSet<string> VbKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "If", "For", "ForEach", "While", "Do", "Select", "Case", "SyncLock", "Using", "Catch", "Finally", "With"
        };

        /// <summary>
        /// A robust Regex for C# method detection.
        /// It uses a positive lookahead to find the method's structure (name, parameters, opening brace)
        /// without trying to parse the complex return types and modifiers, which was the point of failure.
        /// It then captures the name for validation.
        /// </summary>
        private static readonly Regex CSharpMethodRegex = new Regex(
            @"
            ^                                       # Start of a line (in Multiline mode)
            \s*                                     # Leading whitespace
            # Lookahead to find a pattern that looks like a method signature on the current line
            (?=
                .*                                  # Allow any modifiers, return type, etc.
                \b(?<MethodName>[\w_][\w\d_]*)\b    # Capture a valid identifier as the method name
                \s*
                (?:<.*?>)?                          # Optional generic parameters on the method itself, e.g., <T>
                \s*
                \(.*?\)                             # The parameter list (non-greedy)
                \s*
                (?:where\s.*?)?                     # Optional 'where' constraints for generics
                \s*
                \{                                  # Must be followed by an opening brace
            )
            ",
            RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

        /// <summary>
        /// A Regex for VB.NET Sub/Function detection.
        /// This is more straightforward than C# because we can anchor the search on the 'Function' or 'Sub' keyword.
        /// </summary>
        private static readonly Regex VbMethodRegex = new Regex(
            @"
            ^                                       # Start of a line (in Multiline mode)
            \s*
            (?:<[\w\s=""',.]*>\s*)*                  # Optional attributes like <TestMethod()>
            (?:(Public|Private|Protected|Friend|Shared|Async|Overrides|Overridable|MustOverride|Iterator)\s+)* # Modifiers
            (?:Function|Sub)\s+                     # The 'Function' or 'Sub' keyword is mandatory
            \b(?<MethodName>[\w_][\w\d_]*)\b        # Capture the method name
            \s*
            \(.*?\)                                 # The parameter list
            ",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

        #endregion


        public static (bool IsFunction, string FunctionName, bool IsFullFile) AnalyzeCodeBlock(DTE2 dte, Document activeDoc, string code)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dte == null || activeDoc == null || string.IsNullOrEmpty(code))
            {
                WebViewUtilities.Log("AnalyzeCodeBlock: Invalid input (DTE, Document, or code is null/empty).");
                return (false, null, false);
            }

            try
            {
                string extension = Path.GetExtension(activeDoc.FullName)?.ToLowerInvariant();
                bool isCSharp = extension == ".cs";
                bool isVB = extension == ".vb";

                if (!isCSharp && !isVB)
                {
                    WebViewUtilities.Log($"AnalyzeCodeBlock: Unsupported file extension: {extension}");
                    return (false, null, false);
                }

                // --- Stricter Full File Detection ---
                string trimmedCode = code.Trim();

                // THE FIX: Use Regex anchored to the start (^) for all top-level keyword checks.
                // This prevents matching keywords that appear inside a method body.
                bool hasImportsOrUsing = isCSharp
                    ? new Regex(@"^\s*using\s+").IsMatch(trimmedCode)
                    : new Regex(@"^\s*Imports\s+", RegexOptions.IgnoreCase).IsMatch(trimmedCode);

                bool hasNamespace = new Regex(@"^\s*namespace\s+").IsMatch(trimmedCode);

                bool hasTopLevelType = false;
                if (isCSharp)
                {
                    hasTopLevelType = new Regex(@"^\s*(public|internal)?\s*(abstract|sealed|static)?\s*(class|struct|interface|enum)\s+").IsMatch(trimmedCode);
                }
                else if (isVB)
                {
                    hasTopLevelType = new Regex(@"^\s*(Public|Friend)?\s*(Shared|MustInherit|NotInheritable)?\s*(Class|Structure|Interface|Enum|Module)\s+", RegexOptions.IgnoreCase).IsMatch(trimmedCode);
                }

                // A full file must have a balanced structure.
                int openBraces = code.Count(c => c == '{');
                int closeBraces = code.Count(c => c == '}');
                // A valid C# file/class block must contain at least one brace pair and be balanced.
                bool bracesBalanced = openBraces > 0 && openBraces == closeBraces;

                bool vbStructureBalanced = true;
                if (isVB)
                {
                    int startCount = Regex.Matches(code, @"\b(Class|Module|Structure|Enum|Interface)\b", RegexOptions.IgnoreCase).Count;
                    int endCount = Regex.Matches(code, @"\bEnd\s+(Class|Module|Structure|Enum|Interface)\b", RegexOptions.IgnoreCase).Count;
                    // A valid VB structure must have at least one container and they must be balanced.
                    vbStructureBalanced = startCount > 0 && startCount == endCount;
                }

                if (((isCSharp && bracesBalanced) || (isVB && vbStructureBalanced)) && (hasImportsOrUsing || hasNamespace || hasTopLevelType))
                {
                    WebViewUtilities.Log("AnalyzeCodeBlock: Detected full file based on structure and keywords.");
                    return (false, null, true);
                }


                // --- Method Detection ---
                // If it's not a full file, check if it's a function.
                Regex regex = isCSharp ? CSharpMethodRegex : VbMethodRegex;
                var matches = regex.Matches(code);

                if (matches.Count > 0)
                {
                    var match = matches[0];
                    string methodName = match.Groups["MethodName"].Value;
                    bool isKeyword = isCSharp ? CSharpKeywords.Contains(methodName) : VbKeywords.Contains(methodName);

                    if (!string.IsNullOrEmpty(methodName) && !isKeyword)
                    {
                        if (isCSharp)
                        {
                            // A single function should also have balanced braces.
                            if (bracesBalanced)
                            {
                                WebViewUtilities.Log($"AnalyzeCodeBlock: Detected C# method: {methodName}");
                                return (true, methodName, false);
                            }
                        }
                        else if (isVB)
                        {
                            if (Regex.IsMatch(code, @"\bEnd\s+(Function|Sub)\b", RegexOptions.IgnoreCase))
                            {
                                WebViewUtilities.Log($"AnalyzeCodeBlock: Detected VB method: {methodName}");
                                return (true, methodName, false);
                            }
                        }
                    }
                }

                WebViewUtilities.Log("AnalyzeCodeBlock: Code is not a full file or a recognized method. Treating as a snippet.");
                return (false, null, false);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Exception in AnalyzeCodeBlock: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return (false, null, false);
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

                var result = new System.Text.StringBuilder();
                foreach (var line in lines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        // Determine the current line's original indentation to decide how to handle it.
                        int currentIndent = 0;
                        int charsToRemove = 0;
                        int spaceCount = 0;
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
                        result.AppendLine(line.Substring(charsToRemove));
                    }
                    else
                    {
                        result.AppendLine(line);
                    }
                }

                return result.ToString();
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in RemoveBaseIndentation: {ex.Message}");
                return codeSnippet; // Return original code if error occurs
            }
        }
    }    
}
