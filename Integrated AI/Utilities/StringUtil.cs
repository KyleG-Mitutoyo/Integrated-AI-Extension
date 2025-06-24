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

                    // Preserve first line's indentation, only if it's an existing function replacement
                    // Should technically fix function replacement start index instead of this
                    if (i == 0 && length > 1)
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

        public static string ReplaceOrAddCode(DTE2 dte, string currentCode, string aiCode, Document activeDoc, ChooseCodeWindow.ReplacementItem chosenItem = null, DiffUtility.DiffContext context = null)
        {
            var (isFunction, functionName, isFullFile) = (false, string.Empty, false);

            // Determine the language based on the active document's extension
            string extension = Path.GetExtension(activeDoc.FullName).ToLowerInvariant();
            bool isVB = extension == ".vb";
            bool isCS = extension == ".cs";

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

            if (chosenItem == null && isFullFile)
            {
                return aiCode; // Replace entire document for "file" type
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
                        // Remove comments (C# // or VB ' or REM) above the function definition
                        aiCode = RemoveHeaderFooterComments(aiCode);
                        context.NewCodeStartIndex = startIndex;
                        return ReplaceCodeBlock(currentCode, startIndex, startLine, targetFunction.FullCode.Length, aiCode);
                    }
                }
                else
                {
                    ChooseCodeWindow.ReplacementItem newFunctionItem = new ChooseCodeWindow.ReplacementItem {};
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
                        return ReplaceCodeBlock(currentCode, startIndex, startLine, 1, aiCode);
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
                    string newFilePath = PromptForNewFilePath(dte, isVB ? "vb" : "cs");
                    if (string.IsNullOrEmpty(newFilePath))
                    {
                        WebViewUtilities.Log("New file creation cancelled by user.");
                        return currentCode; // Return unchanged if cancelled
                    }

                    // Create and add the new file to the solution
                    CreateNewFileInSolution(dte, newFilePath, aiCode);
                    context.IsNewFile = true; // Indicate that a new file was created

                    // Since it's a new file, we don't modify the current document
                    return currentCode;
                }
            }

            // For "Selection" or unmatched functions, check for highlighted text
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
                    return ReplaceCodeBlock(currentCode, startIndex, -1, length, aiCode);
                }
            }

            // Fallback: Insert at cursor position or append if selection was empty or other errors
            return StringUtil.InsertAtCursorOrAppend(currentCode, aiCode, activeDoc);
        }

        // Helper method to remove comments above function definition and header/footer
        private static string RemoveHeaderFooterComments(string code)
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

        // Prompt user for a new file path using a dialog
        private static string PromptForNewFilePath(DTE2 dte, string language = "cs")
        {
            // Use Windows Forms SaveFileDialog for simplicity
            using (var dialog = new System.Windows.Forms.SaveFileDialog())
            {
                if (language == "vb")
                {
                    dialog.Filter = "VB Files (*.vb)|*.vb|All Files (*.*)|*.*";
                    dialog.DefaultExt = "vb";
                }
                else
                {
                    dialog.Filter = "C# Files (*.cs)|*.cs|All Files (*.*)|*.*";
                    dialog.DefaultExt = "cs";
                }
                dialog.Title = "Select Location for New File";
                dialog.InitialDirectory = Path.GetDirectoryName(dte.Solution.FullName); // Start in solution directory

                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    return dialog.FileName;
                }
            }
            return null; // Return null if cancelled
        }

        // Create a new file in the solution and add AI-generated code
        private static void CreateNewFileInSolution(DTE2 dte, string filePath, string aiCode)
        {
            try
            {
                // Ensure the directory exists
                string directory = Path.GetDirectoryName(filePath);
                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                // Write the AI-generated code to the new file
                File.WriteAllText(filePath, aiCode);

                // Add the file to the solution
                Project project = FindProjectForPath(dte, directory);
                if (project != null)
                {
                    project.ProjectItems.AddFromFile(filePath);
                    WebViewUtilities.Log($"New file '{filePath}' added to project '{project.Name}'.");
                }
                else
                {
                    // Fallback: Add to the solution's Miscellaneous Files
                    dte.ItemOperations.OpenFile(filePath);
                    WebViewUtilities.Log($"New file '{filePath}' added as a miscellaneous file.");
                }

                // Open the new file in the editor
                dte.ItemOperations.OpenFile(filePath);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error creating new file: {ex.Message}");
                //MessageBox.Show($"Error creating new file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Find the appropriate project for the given directory
        private static Project FindProjectForPath(DTE2 dte, string directory)
        {
            foreach (Project project in dte.Solution.Projects)
            {
                string projectDir = Path.GetDirectoryName(project.FullName);
                if (directory.StartsWith(projectDir, StringComparison.OrdinalIgnoreCase))
                {
                    return project;
                }
            }
            return null; // No matching project found
        }
    }
}
