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
using HandyControl.Themes;
using Integrated_AI.Utilities;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;

namespace Integrated_AI
{
    public static class CodeSelectionUtilities
    {
        public static List<FunctionSelectionWindow.FunctionItem> GetFunctionsFromDocument(Document activeDoc)
        {
            var functions = new List<FunctionSelectionWindow.FunctionItem>();
            if (activeDoc?.Object("TextDocument") is TextDocument textDocument)
            {
                var codeModel = activeDoc.ProjectItem?.FileCodeModel;
                if (codeModel != null)
                {
                    foreach (CodeElement element in codeModel.CodeElements)
                    {
                        CollectFunctions(element, functions);
                    }
                }
            }
            return functions;
        }

        public static List<ErrorSelectionWindow.ErrorItem> GetErrorsFromDTE(DTE2 dte)
        {
            var errorList = dte.ToolWindows.ErrorList;
            var errors = new List<ErrorSelectionWindow.ErrorItem>();

            for (int i = 1; i <= errorList.ErrorItems.Count; i++)
            {
                var item = errorList.ErrorItems.Item(i);
                if (item.ErrorLevel == vsBuildErrorLevel.vsBuildErrorLevelHigh)
                {
                    errors.Add(new ErrorSelectionWindow.ErrorItem
                    {
                        Description = item.Description,
                        FullFile = item.FileName,
                        File = Path.GetFileName(item.FileName),
                        Line = item.Line,
                        DteErrorItem = item
                    });
                }
            }

            if (!errors.Any())
            {
                return null;
            }

            return errors;
        }

        private static void CollectFunctions(CodeElement element, List<FunctionSelectionWindow.FunctionItem> functions)
        {
            if (element.Kind == vsCMElement.vsCMElementFunction)
            {
                var codeFunction = (CodeFunction)element;
                string functionCode = codeFunction.StartPoint.CreateEditPoint().GetText(codeFunction.EndPoint);
                string displayName = codeFunction.Name;
                string listBoxDisplayName = $"{codeFunction.Name} ({codeFunction.Parameters.Cast<CodeParameter>().Count()} params)";
                string fullName = $"{codeFunction.FullName}";
                functions.Add(new FunctionSelectionWindow.FunctionItem
                {
                    DisplayName = displayName,
                    ListBoxDisplayName = listBoxDisplayName,
                    FullName = fullName,
                    Function = codeFunction,
                    FullCode = functionCode,
                    StartPoint = codeFunction.StartPoint, 
                    EndPoint = codeFunction.EndPoint      
                });
            }

            if (element.Children != null)
            {
                foreach (CodeElement child in element.Children)
                {
                    CollectFunctions(child, functions);
                }
            }
        }

        public static List<FunctionSelectionWindow.FunctionItem> PopulateFunctionList(IEnumerable<FunctionSelectionWindow.FunctionItem> functions, List<string> recentFunctions, string openedFile, bool showNewFunction)
        {
            var functionList = functions.ToList();
            var items = new List<FunctionSelectionWindow.FunctionItem>();

            // New function is optional, added at the very top
            if (showNewFunction)
            {
                // Add "New Function" option at the top
                items.Add(new FunctionSelectionWindow.FunctionItem
                {
                    DisplayName = "New Function",
                    ListBoxDisplayName = "New Function",
                    FullName = "Create a new function in the active document"
                });
            }

            // Add recent functions and their header if any exist
            if (recentFunctions.Any())
            {
                items.Add(new FunctionSelectionWindow.FunctionItem { ListBoxDisplayName = "----- Recent Functions -----", FullName = $"Recent functions used for {openedFile}" });
                foreach (var recent in recentFunctions)
                {
                    var matchingFunction = functionList.FirstOrDefault(f => f.DisplayName == recent);
                    if (matchingFunction != null)
                    {
                        items.Add(matchingFunction);
                        functionList.Remove(matchingFunction); // Remove to avoid duplicates
                    }
                }
            }

            // Always add "All Functions" header
            items.Add(new FunctionSelectionWindow.FunctionItem { ListBoxDisplayName = "----- All Functions -----", FullName = $"All the functions within {openedFile}" });

            // Add remaining functions
            items.AddRange(functionList);
            return items;
        }

        // Populates separate lists for functions and project files
        public static (List<ChooseCodeWindow.ReplacementItem> Functions, List<ChooseCodeWindow.ReplacementItem> Files) 
                    PopulateReplacementLists(DTE2 dte, Document activeDoc, string tempCurrentFile = null, string tempAiFile = null)
        {
            var functions = new List<ChooseCodeWindow.ReplacementItem>();
            var files = new List<ChooseCodeWindow.ReplacementItem>();
            string solutionPath = Path.GetDirectoryName(dte.Solution.FullName);
            string activeFilePath = activeDoc?.FullName;

            // Create a list of files to exclude
            var filesToExclude = new List<string>();
            if (!string.IsNullOrEmpty(tempCurrentFile))
            {
                filesToExclude.Add(tempCurrentFile);
            }
            if (!string.IsNullOrEmpty(tempAiFile))
            {
                filesToExclude.Add(tempAiFile);
            }

            // Add "New Function" option at the top
            functions.Add(new ChooseCodeWindow.ReplacementItem
            {
                DisplayName = "New Function",
                ListBoxDisplayName = "New Function",
                FullName = "Create a new function in the active document",
                Type = "new_function"
            });

            // Insert at cursor/replace selected text option
            functions.Add(new ChooseCodeWindow.ReplacementItem
            {
                DisplayName = "Snippet",
                ListBoxDisplayName = "Insert at cursor or replace selected text",
                FullName = "Replace highlighted text in the code editor or insert at cursor position if no text is highlighted",
                Type = "snippet"
            });

            var recentFunctions = FileUtil.LoadRecentFunctions(FileUtil._recentFunctionsFilePath);
            var activeFunctions = CodeSelectionUtilities.GetFunctionsFromDocument(activeDoc);
            var matchedRecentFunctions = activeFunctions.Where(func => recentFunctions != null && 
                                            recentFunctions.Contains(func.DisplayName)).ToList();

            // only show the recent functions header if there are any recent functions that match the active document's functions
            if (matchedRecentFunctions.Any())
            {
                functions.Add(new ChooseCodeWindow.ReplacementItem
                {
                    ListBoxDisplayName = "----- Recent Functions -----",
                    FullName = "Recently used functions"
                });
            }

            foreach (var func in matchedRecentFunctions)
            {
                functions.Add(new ChooseCodeWindow.ReplacementItem
                {
                    DisplayName = func.DisplayName,
                    ListBoxDisplayName = func.ListBoxDisplayName,
                    FullName = func.FullName,
                    Function = func.Function,
                    FullCode = func.FullCode,
                    Type = "function"
                });
            }

            functions.Add(new ChooseCodeWindow.ReplacementItem
            {
                ListBoxDisplayName = "----- All Functions in Active Document -----",
                FullName = "Functions in the currently active document"
            });

            foreach (var func in activeFunctions)
            {
                functions.Add(new ChooseCodeWindow.ReplacementItem
                {
                    DisplayName = func.DisplayName,
                    ListBoxDisplayName = func.ListBoxDisplayName,
                    FullName = func.FullName,
                    Function = func.Function,
                    FullCode = func.FullCode,
                    Type = "function"
                });
            }

            // Files Section
            // Add "New File" option at the top
            files.Add(new ChooseCodeWindow.ReplacementItem
            {
                DisplayName = "New File",
                ListBoxDisplayName = "New File",
                FullName = "Create a new file in the project",
                Type = "new_file"
            });

            // Add separator for project files
            files.Add(new ChooseCodeWindow.ReplacementItem
            {
                ListBoxDisplayName = "----- Project Files -----",
                FullName = "Files in the solution"
            });

            // Add the opened file right after the separator
            if (!string.IsNullOrEmpty(activeFilePath))
            {
                files.Add(new ChooseCodeWindow.ReplacementItem
                {
                    DisplayName = Path.GetFileName(activeFilePath) + " (opened file)",
                    ListBoxDisplayName = Path.GetFileName(activeFilePath) + " (opened file)",
                    FullName = "Replace contents of the currently opened file",
                    Type = "opened_file",
                    FilePath = activeFilePath
                });
            }

            // Enumerate project files, excluding the active file and any temp files.
            // The Contains check on filesToExclude will now filter the temp files.
            var projectFiles = GetProjectFiles(dte, solutionPath)
                .Where(f => f.FilePath != activeFilePath && !filesToExclude.Contains(f.FilePath))
                .ToList();
            files.AddRange(projectFiles);

            // Sort files, but preserve "New File", separator, and opened file at the top
            var topItems = files.Take(string.IsNullOrEmpty(activeFilePath) ? 2 : 3).ToList(); // Take "New File", separator, and opened file (if exists)
            var sortedFiles = files.Skip(topItems.Count).OrderBy(i => i.FilePath).ToList();
            topItems.AddRange(sortedFiles);

            return (functions, topItems);
        }

        // Retrieves all project files with indented folder structure
        private static List<ChooseCodeWindow.ReplacementItem> GetProjectFiles(DTE2 dte, string solutionPath)
        {
            var items = new List<ChooseCodeWindow.ReplacementItem>();
            foreach (Project project in dte.Solution.Projects)
            {
                CollectProjectItems(project.ProjectItems, solutionPath, items, 0);
            }
            return items;
        }

        private static void CollectProjectItems(ProjectItems projectItems, string solutionPath, List<ChooseCodeWindow.ReplacementItem> items, int indentLevel)
        {
            if (projectItems == null) return;

            foreach (ProjectItem item in projectItems)
            {
                if (item.FileCount > 0)
                {
                    string filePath = item.FileNames[1]; // First file name
                    if (File.Exists(filePath))
                    {
                        string relativePath = FileUtil.GetRelativePath(solutionPath, filePath);
                        string indent = new string(' ', indentLevel * 2);
                        items.Add(new ChooseCodeWindow.ReplacementItem
                        {
                            DisplayName = Path.GetFileName(filePath),
                            ListBoxDisplayName = $"{indent}{Path.GetFileName(filePath)}",
                            FullName = relativePath,
                            FilePath = filePath,
                            Type = "file"
                        });
                    }
                }
                // Recurse into subfolders
                if (item.ProjectItems != null)
                {
                    CollectProjectItems(item.ProjectItems, solutionPath, items, indentLevel + 1);
                }
            }
        }

        public static ChooseCodeWindow.ReplacementItem ShowCodeReplacementWindow(DTE2 dte, Document activeDoc, string tempCurrentFile = null, string tempAiFile = null)
        {
            var window = new ChooseCodeWindow(dte, activeDoc, tempCurrentFile, tempAiFile);
            bool? result = window.ShowDialog();
            return result == true ? window.SelectedItem : null;
        }
    }
}