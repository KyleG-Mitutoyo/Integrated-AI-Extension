﻿// Integrated AI
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Integrated_AI.Utilities;

namespace Integrated_AI.Utilities
{
    public class DocumentContentResult
    {
        public string ModifiedCode { get; set; }
        public bool IsNewFileCreationRequired { get; set; } = false;
        public string NewFilePath { get; set; }
        public string NewFileContent { get; set; }
    }

    public static class DocumentTextUtil
    {
        public static DocumentContentResult CreateDocumentContent(DTE2 dte, System.Windows.Window window, string currentCode, string aiCode, Document activeDoc, ChooseCodeWindow.ReplacementItem chosenItem = null, DiffUtility.DiffContext context = null)
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
                    (isFunction, functionName, isFullFile) = CodeAnalysisUtil.AnalyzeCodeBlock(dte, activeDoc, aiCode);
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
                        return new DocumentContentResult { ModifiedCode = aiCode }; // Replace entire document for "file" type
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
                            aiCode = StringUtil.RemoveHeaderFooterComments(aiCode);
                            context.NewCodeStartIndex = startIndex;
                            return new DocumentContentResult { ModifiedCode = ReplaceCodeBlock(window, currentCode, startIndex, startLine, targetFunction.FullCode.Length, aiCode, true) };
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
                            aiCode = StringUtil.RemoveHeaderFooterComments(aiCode);

                            // Use ReplaceCodeBlock to insert the new function with correct indentation
                            context.NewCodeStartIndex = startIndex;
                            return new DocumentContentResult { ModifiedCode = ReplaceCodeBlock(window, currentCode, startIndex, startLine, 0, aiCode, false) };
                        }
                        else
                        {
                            // No functions in the document, append at the end with default indentation
                            WebViewUtilities.Log("No functions found in the document. Appending new function at the end.");
                            return new DocumentContentResult { ModifiedCode = InsertAtCursorOrAppend(window, currentCode, aiCode, activeDoc) };
                        }
                    }
                    else if (chosenItem.Type == "new_file")
                    {
                        // Prompt for the file path on the UI thread. This is quick.
                        string newFilePath = FileUtil.PromptForNewFilePath(dte, isVB ? "vb" : "cs");
                        if (string.IsNullOrEmpty(newFilePath))
                        {
                            WebViewUtilities.Log("New file creation cancelled by user.");
                            context.IsNewFile = true; // To prevent diff view from opening
                            return new DocumentContentResult { ModifiedCode = currentCode };
                        }

                        // **THE KEY CHANGE**: Instead of creating the file here,
                        // we package up the necessary information and return it.
                        context.IsNewFile = true; // Still useful for the caller
                        return new DocumentContentResult
                        {
                            IsNewFileCreationRequired = true,
                            NewFilePath = newFilePath,
                            NewFileContent = aiCode,
                            ModifiedCode = currentCode // The original document's code remains unchanged
                        };
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

                        return new DocumentContentResult { ModifiedCode = ReplaceCodeBlock(window, currentCode, startIndex, startLine, length, aiCode, true) };
                    }
                }

                // Fallback: Replace all code in the document with the new AI code
                return new DocumentContentResult { ModifiedCode = aiCode };
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in CreateDocumentContent: {ex.Message}");
                ThemedMessageBox.Show(window, $"Error creating document content: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return new DocumentContentResult { ModifiedCode = currentCode }; // Return unchanged code if error occurs
            }
        }

        public static string ReplaceCodeBlock(System.Windows.Window window, string documentContent, int startIndex, int startLine, int length, string newCode, bool fixDoubleIndent)
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
                        baseIndentation = StringUtil.GetIndentPosition(lineText);
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
                ThemedMessageBox.Show(window, $"Error inserting code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return currentCode; // Return unchanged code if error occurs
            }
        }
    }
}
