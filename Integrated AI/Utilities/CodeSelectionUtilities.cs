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
        private static readonly HashSet<string> _commonCodeExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            // .NET Languages
            ".cs", ".vb", ".fs", ".fsi", ".fsx", ".fsscript",
            // C/C++
            ".c", ".cpp", ".cxx", ".h", ".hpp",
            // Web front-end
            ".html", ".htm", ".css", ".scss", ".less", ".js", ".ts", ".jsx", ".tsx",
            // Markup, Data, and Config
            ".json", ".xml", ".xaml", ".yml", ".yaml", ".ini", ".properties", ".config",
            // Scripting
            ".py", ".ps1", ".bat", ".cmd",
            // SQL
            ".sql",
            // ASP.NET
            ".razor", ".cshtml",
            // Markdown
            ".md"
        };

        public static HashSet<string> GetCommonCodeFileExtensions()
        {
            return _commonCodeExtensions;
        }

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
                    FullCode = functionCode, // Use the corrected code here
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
            ThreadHelper.ThrowIfNotOnUIThread();
            var functions = new List<ChooseCodeWindow.ReplacementItem>();
            var files = new List<ChooseCodeWindow.ReplacementItem>();
            string solutionPath = Path.GetDirectoryName(dte.Solution.FullName);
            
            #region Functions List Population
            functions.Add(new ChooseCodeWindow.ReplacementItem
            {
                DisplayName = "Snippet",
                ListBoxDisplayName = "Insert at cursor or replace selected text",
                FullName = "Replace highlighted text in the code editor or insert at cursor position if no text is highlighted",
                Type = "snippet"
            });

            if (activeDoc != null)
            {
                var recentFunctions = FileUtil.LoadRecentFunctions(FileUtil._recentFunctionsFilePath);
                var activeFunctions = CodeSelectionUtilities.GetFunctionsFromDocument(activeDoc);
                var matchedRecentFunctions = activeFunctions.Where(func => recentFunctions != null &&
                                                recentFunctions.Contains(func.DisplayName)).ToList();
                if (matchedRecentFunctions.Any())
                {
                    functions.Add(new ChooseCodeWindow.ReplacementItem { ListBoxDisplayName = "----- Recent Functions -----" });
                    foreach (var func in matchedRecentFunctions)
                    {
                        functions.Add(new ChooseCodeWindow.ReplacementItem
                        {
                            DisplayName = func.DisplayName, ListBoxDisplayName = func.ListBoxDisplayName, FullName = func.FullName,
                            Function = func.Function, FullCode = func.FullCode, Type = "function"
                        });
                    }
                }
                functions.Add(new ChooseCodeWindow.ReplacementItem { ListBoxDisplayName = "----- All Functions in Active Document -----" });
                foreach (var func in activeFunctions)
                {
                    functions.Add(new ChooseCodeWindow.ReplacementItem
                    {
                        DisplayName = func.DisplayName, ListBoxDisplayName = func.ListBoxDisplayName, FullName = func.FullName,
                        Function = func.Function, FullCode = func.FullCode, Type = "function"
                    });
                }
            }
            #endregion

            #region Files List Population

            files.Add(new ChooseCodeWindow.ReplacementItem { ListBoxDisplayName = "----- Project Files -----", FullName = "Files in the solution" });
            
            var excludedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // Add the currently active document right below the header
            if (activeDoc != null && !string.IsNullOrEmpty(activeDoc.FullName) && File.Exists(activeDoc.FullName))
            {
                files.Add(new ChooseCodeWindow.ReplacementItem
                {
                    DisplayName = Path.GetFileName(activeDoc.FullName),
                    ListBoxDisplayName = $"{Path.GetFileName(activeDoc.FullName)} (active file)",
                    FullName = FileUtil.GetRelativePath(solutionPath, activeDoc.FullName),
                    Type = "opened_file",
                    FilePath = activeDoc.FullName
                });
            }
            
            // Add temp files to the exclusion set so they don't appear in the list
            if (!string.IsNullOrEmpty(tempCurrentFile)) excludedPaths.Add(tempCurrentFile);
            if (!string.IsNullOrEmpty(tempAiFile)) excludedPaths.Add(tempAiFile);

            var projectFiles = GetProjectFiles(dte, solutionPath)
                .Where(f => !excludedPaths.Contains(f.FilePath))
                .ToList();
            files.AddRange(projectFiles);
            #endregion

            return (functions, files);
        }


        // Retrieves all project files with indented folder structure
        private static List<ChooseCodeWindow.ReplacementItem> GetProjectFiles(DTE2 dte, string solutionPath)
        {
            var items = new List<ChooseCodeWindow.ReplacementItem>();
            var processedFilePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase); // Track processed files to prevent duplicates
            if (dte.Solution?.Projects != null)
            {
                foreach (Project project in dte.Solution.Projects)
                {
                    CollectProjectItems(project.ProjectItems, solutionPath, items, 0, processedFilePaths);
                }
            }
            return items;
        }

        private static void CollectProjectItems(ProjectItems projectItems, string solutionPath, List<ChooseCodeWindow.ReplacementItem> items, int indentLevel, HashSet<string> processedFilePaths)
        {
            if (projectItems == null) return;
        
            var sortedItems = projectItems.Cast<ProjectItem>()
                .OrderBy(p => p.Kind != Constants.vsProjectItemKindPhysicalFolder) 
                .ThenBy(p => p.Name)
                .ToList();

            foreach (ProjectItem item in sortedItems)
            {
                try
                {
                    string indent = new string(' ', indentLevel * 2);

                    // Step 1: Add the item itself (if it's a file or a folder)
                    bool isFolder = item.Kind == Constants.vsProjectItemKindPhysicalFolder;
                    bool isFile = item.FileCount > 0;

                    if (isFolder)
                    {
                        string folderPath = "";
                        try { folderPath = item.Properties.Item("FullPath").Value.ToString(); }
                        catch { /* some project items might not have this property */ }
                        items.Add(new ChooseCodeWindow.ReplacementItem
                        {
                            DisplayName = item.Name,
                            ListBoxDisplayName = $"{indent}📁 {item.Name}",
                            Type = "folder",
                            FilePath = folderPath 
                        });
                    }
                    else if (isFile)
                    {
                        string filePath = item.FileNames[1];
                        // Use the HashSet to prevent adding duplicate file paths
                        if (File.Exists(filePath) && processedFilePaths.Add(filePath))
                        {
                            items.Add(new ChooseCodeWindow.ReplacementItem
                            {
                                DisplayName = Path.GetFileName(filePath),
                                ListBoxDisplayName = $"{indent}{Path.GetFileName(filePath)}",
                                FullName = FileUtil.GetRelativePath(solutionPath, filePath),
                                FilePath = filePath,
                                Type = "file"
                            });
                        }
                    }

                    // Step 2: ALWAYS check for children and recurse. This handles children of folders
                    // AND dependent files (like .xaml.cs under .xaml).
                    if (item.ProjectItems != null && item.ProjectItems.Count > 0)
                    {
                        CollectProjectItems(item.ProjectItems, solutionPath, items, indentLevel + 1, processedFilePaths);
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }
        }


        public static ChooseCodeWindow.ReplacementItem ShowCodeReplacementWindow(DTE2 dte, Document activeDoc, string tempCurrentFile = null, string tempAiFile = null)
        {
            var window = new ChooseCodeWindow(dte, activeDoc, tempCurrentFile, tempAiFile);
            bool? result = window.ShowDialog();
            return result == true ? window.SelectedItem : null;
        }

        public static List<ChooseCodeWindow.ReplacementItem> ShowMultiFileSelectionWindow(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            var window = new ChooseCodeWindow(dte, ChooseCodeWindow.SelectionMode.MultipleFiles);
            bool? result = window.ShowDialog();
            return result == true ? window.SelectedItems : null;
        }
    }
}