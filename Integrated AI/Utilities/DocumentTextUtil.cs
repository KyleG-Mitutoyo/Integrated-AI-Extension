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
using Integrated_AI.Commands;
using Integrated_AI.Utilities;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Remoting.Contexts;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public static class DocumentTextUtil
    {
        public static string CreateDocumentContent(DTE2 dte, System.Windows.Window window, string currentCode, string aiCode, Document activeDoc, ChooseCodeWindow.ReplacementItem chosenItem = null, DiffUtility.DiffContext context = null)
        {
            try
            {
                string result = null;

                // Prioritize replacing selected text if any, but not for certain chosenItem types
                if (chosenItem?.Type != "file" && chosenItem?.Type != "opened_file")
                {
                    // Result might be null after this so return it later
                    result = ReplaceSelectedText(activeDoc, context, window, currentCode, aiCode);
                }

                if (result != null)
                {
                    return result;
                }

                // --- NEW LOGIC START: Check for Diff Blocks (<<<< ==== >>>>) ---
                // If the user hasn't explicitly chosen a type (like new file), check if the AI sent a specific diff block.
                if (chosenItem == null && activeDoc != null)
                {
                    if (TryProcessDiffBlock(window, currentCode, aiCode, context, out string diffModifiedCode))
                    {
                        WebViewUtilities.Log("CreateDocumentContent: Detected and applied Diff Block (Conflict Markers).");
                        return diffModifiedCode;
                    }
                }
                // --- NEW LOGIC END ---

                var (isFunction, functionName, isFullFile) = (false, string.Empty, false);

                // --- SAFEGUARD HERE ---
                // This is where it was crashing. We now handle activeDoc being null.
                string extension = ".txt"; 
                if (activeDoc != null)
                {
                    // We split this up to be safe
                    extension = Path.GetExtension(activeDoc.FullName)?.ToLowerInvariant() ?? ".txt";
                }

                bool isVB = extension == ".vb";

                // Analyze the code block to determine its type, if there is no chosen item provided
                if (chosenItem == null)
                {
                    // SAFETY CHECK: activeDoc might be null here too
                    if (activeDoc != null) 
                    {
                        (isFunction, functionName, isFullFile) = CodeAnalysisUtil.AnalyzeCodeBlock(dte, activeDoc, aiCode);
                    }
                }
                else
                {
                    isFunction = chosenItem?.Type == "function";
                    isFullFile = chosenItem?.Type == "file" || chosenItem?.Type == "opened_file";
                    functionName = chosenItem?.DisplayName ?? string.Empty;
                }

                // Only return the AI code if it's a full file
                if (isFullFile)
                {
                    if (chosenItem == null)
                    {
                        return aiCode; 
                    }
                }

                // SAFETY CHECK: Ensure activeDoc exists before trying to find functions in it
                if (isFunction && activeDoc != null)
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
                            aiCode = StringUtil.RemoveHeaderFooterComments(aiCode);

                            // SAFETY CHECK: Context might be null
                            if (context != null) context.NewCodeStartIndex = startIndex;

                            return ReplaceCodeBlock(window, currentCode, startIndex, startLine, targetFunction.FullCode.Length, aiCode, true);
                        }
                    }
                    else
                    {
                        WebViewUtilities.Log($"Function '{functionName}' not found in the document. It will be added as a snippet.");
                    }
                }

                // Snippets with no selected text or unmatched functions should be inserted at the cursor or appended here to avoid the fallback
                if (isFunction || (chosenItem != null && chosenItem.Type == "snippet"))
                {
                    // For snippet insertions, insert at cursor or append
                    WebViewUtilities.Log("Inserting AI code at cursor position or appending to document.");
                    return InsertAtCursorOrAppend(window, currentCode, aiCode, activeDoc);
                }

                // Fallback: Replace all code in the document with the new AI code
                return aiCode;
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in CreateDocumentContent: {ex.Message}");
                ThemedMessageBox.Show(window, $"Error creating document content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return currentCode; 
            }
        }

        /// <summary>
        /// Attempts to parse code in the format <<<< OLD ==== NEW >>>> and find the OLD code in the current document.
        /// </summary>
        private static bool TryProcessDiffBlock(System.Windows.Window window, string currentCode, string aiCode, DiffUtility.DiffContext context, out string modifiedCode)
        {
            modifiedCode = null;

            // Basic check to see if the markers exist before doing expensive operations
            if (string.IsNullOrEmpty(aiCode) || !aiCode.Contains("<<<<") || !aiCode.Contains("====") || !aiCode.Contains(">>>>"))
            {
                return false;
            }

            try
            {
                var lines = aiCode.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

                // Find markers. We use StartsWith to allow for things like "<<<< filename.cs"
                int startIdx = lines.FindIndex(l => l.TrimStart().StartsWith("<<<<"));
                int midIdx = lines.FindIndex(l => l.TrimStart().StartsWith("===="));
                int endIdx = lines.FindIndex(l => l.TrimStart().StartsWith(">>>>"));

                // Validate structure: Start < Mid < End
                if (startIdx == -1 || midIdx == -1 || endIdx == -1 || midIdx <= startIdx || endIdx <= midIdx)
                {
                    return false;
                }

                // Extract Old Code (between <<<< and ====)
                var oldCodeLines = lines.GetRange(startIdx + 1, midIdx - startIdx - 1);
                // Extract New Code (between ==== and >>>>)
                var newCodeLines = lines.GetRange(midIdx + 1, endIdx - midIdx - 1);

                string oldCode = string.Join(Environment.NewLine, oldCodeLines);
                string newCode = string.Join(Environment.NewLine, newCodeLines);

                // --- SEARCH LOGIC ---
                // We need to find oldCode in currentCode. 
                // Issue: Line endings might differ (AI uses \n, VS uses \r\n).

                int codeIndex = currentCode.IndexOf(oldCode);

                // If not found, try normalizing line endings to \r\n (common in VS) or \n 
                // We assume currentCode usually has consistent endings, but let's try strict matching first.
                if (codeIndex == -1)
                {
                    // Create a version of oldCode that strictly uses \r\n
                    string oldCodeCRLF = string.Join("\r\n", oldCodeLines);
                    codeIndex = currentCode.IndexOf(oldCodeCRLF);

                    if (codeIndex != -1) oldCode = oldCodeCRLF;
                }

                // If still not found, try just \n (less common in VS files on Windows, but possible)
                if (codeIndex == -1)
                {
                    string oldCodeLF = string.Join("\n", oldCodeLines);
                    codeIndex = currentCode.IndexOf(oldCodeLF);

                    if (codeIndex != -1) oldCode = oldCodeLF;
                }

                // If still not found, try trimming the search block (often AI adds/removes surrounding whitespace)
                if (codeIndex == -1)
                {
                    string trimmedOld = oldCode.Trim();
                    if (!string.IsNullOrEmpty(trimmedOld))
                    {
                        codeIndex = currentCode.IndexOf(trimmedOld);
                        if (codeIndex != -1) oldCode = trimmedOld;
                    }
                }

                if (codeIndex != -1)
                {
                    // We found the block!

                    // We need the line number for indentation calculation in ReplaceCodeBlock.
                    // Count newlines up to codeIndex.
                    int startLine = 1; // Visual Studio lines are 1-based
                    for (int i = 0; i < codeIndex; i++)
                    {
                        if (currentCode[i] == '\n') startLine++;
                    }

                    if (context != null) context.NewCodeStartIndex = codeIndex;

                    // Use existing logic to replace and fix indentation
                    modifiedCode = ReplaceCodeBlock(window, currentCode, codeIndex, startLine, oldCode.Length, newCode, false);
                    return true;
                }
                else
                {
                    WebViewUtilities.Log("TryProcessDiffBlock: Markers found, but 'Original Code' block could not be located in the current document.");
                    ThemedMessageBox.Show(window, "The AI response included a diff block, but the original code block could not be found in the current document.", "Diff Block Not Found", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"TryProcessDiffBlock Error: {ex.Message}");
            }

            return false;
        }

        private static string ReplaceSelectedText(Document activeDoc, DiffUtility.DiffContext context, System.Windows.Window window, string currentCode, string aiCode)
        {
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

                    return ReplaceCodeBlock(window, currentCode, startIndex, startLine, length, aiCode, true);
                }
            }

            return null;
        }

        public static string ReplaceCodeBlock(System.Windows.Window window, string documentContent, int startIndex, int startLine, int length, string newCode, bool fixDoubleIndent)
        {
            try
            {
                // Check if the code being replaced ends with a newline. 
                // We use this to decide whether to append a newline to the new code later, 
                // ensuring we don't accidentally merge lines or add extra newlines.
                string replacedText = "";
                if (length > 0 && startIndex + length <= documentContent.Length)
                {
                    replacedText = documentContent.Substring(startIndex, length);
                }
                bool replacedEndsWithNewline = replacedText.EndsWith("\n") || replacedText.EndsWith("\r");

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
                        baseIndentation = StringUtil.GetIndentPosition(lineText);
                    }
                }

                WebViewUtilities.Log($"Base indentation for replacement: {baseIndentation} spaces");

                // Trim trailing newlines from newCode to prevent extra newlines (AI response often has them)
                if (newCode != null)
                {
                    newCode = newCode.TrimEnd('\r', '\n');
                }

                // Split newCode into lines
                string[] newCodeLines = newCode.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

                // Find the minimum indentation in newCode (excluding empty lines) to preserve relative structure
                int minIndent = int.MaxValue;
                foreach (var line in newCodeLines)
                {
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        int indentCount = StringUtil.GetIndentPosition(line);
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

                // If the original text ended with a newline, append one to the new code to preserve structure.
                // Since we trimmed newCode earlier, this prevents doubling it while avoiding merged lines.
                if (replacedEndsWithNewline)
                {
                    adjustedNewCode += Environment.NewLine;
                }

                // Perform the replacement
                return documentContent.Remove(startIndex, length).Insert(startIndex, adjustedNewCode);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in ReplaceCodeBlock: {ex.Message}");
                ThemedMessageBox.Show(window, $"Error replacing code block: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);

                return documentContent; // Return original content if error occurs
            }
        }

        public static string InsertAtCursorOrAppend(System.Windows.Window window, string currentCode, string aiCode, Document activeDoc)
        {
            try
            {
                // Remove trailing newlines from aiCode. 
                // Since we are inserting into a list of lines, a trailing newline in the string creates 
                // an empty string in the split array, which results in an unwanted blank line in the editor.
                if (aiCode != null)
                {
                    aiCode = aiCode.TrimEnd('\r', '\n');
                }

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
                        // If the line where the cursor is currently is empty or contains only whitespace,
                        // remove it before inserting. This prevents the empty line from being pushed down
                        // below the inserted code, which looks like an extra newline.
                        if (lineIndex < lines.Count && string.IsNullOrWhiteSpace(lines[lineIndex]))
                        {
                            lines.RemoveAt(lineIndex);
                        }

                        var aiCodeLines = aiCode.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
                        var indentedAiCodeLines = aiCodeLines.Select(line => indentPrefix + line);

                        // Insert the new indented lines. This pushes the subsequent lines down.
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
                ThemedMessageBox.Show(window, $"Error inserting code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return currentCode; // Return unchanged code if error occurs
            }
        }
    }
}