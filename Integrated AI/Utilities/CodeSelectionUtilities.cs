using EnvDTE;
using EnvDTE80;
using Integrated_AI.Utilities;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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

        public static List<FunctionSelectionWindow.FunctionItem> PopulateFunctionList(IEnumerable<FunctionSelectionWindow.FunctionItem> functions, List<string> recentFunctions, string openedFile)
        {
            var functionList = functions.ToList();
            var items = new List<FunctionSelectionWindow.FunctionItem>();

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
            PopulateReplacementLists(DTE2 dte, Document activeDoc)
        {
            var functions = new List<ChooseCodeWindow.ReplacementItem>();
            var files = new List<ChooseCodeWindow.ReplacementItem>();
            string solutionPath = Path.GetDirectoryName(dte.Solution.FullName);
            string activeFilePath = activeDoc?.FullName;

            // Add "New Function" option at the top
            functions.Add(new ChooseCodeWindow.ReplacementItem
            {
                DisplayName = "New Function",
                ListBoxDisplayName = "New Function",
                FullName = "Create a new function in the active document",
                Type = "new_function"
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

            files.Add(new ChooseCodeWindow.ReplacementItem
            {
                ListBoxDisplayName = "----- Project Files -----",
                FullName = "Files in the solution"
            });


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

            // Enumerate project files and exclude the active file
            var projectFiles = GetProjectFiles(dte, solutionPath)
                .Where(f => f.FilePath != activeFilePath)
                .ToList();
            files.AddRange(projectFiles);

            return (functions, files.OrderBy(i => i.FilePath).ToList());
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

        public static ChooseCodeWindow.ReplacementItem ShowCodeReplacementWindow(DTE2 dte, Document activeDoc)
        {
            var window = new ChooseCodeWindow(dte, activeDoc);
            bool? result = window.ShowDialog();
            return result == true ? window.SelectedItem : null;
        }
    }
}