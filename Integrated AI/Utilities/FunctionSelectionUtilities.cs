using EnvDTE;
using EnvDTE80;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Integrated_AI
{
    public static class FunctionSelectionUtilities
    {
        public static List<FunctionSelectionWindow.FunctionItem> GetFunctionsFromActiveDocument(DTE2 dte)
        {
            var functions = new List<FunctionSelectionWindow.FunctionItem>();
            if (dte.ActiveDocument?.Object("TextDocument") is TextDocument textDocument)
            {
                var codeModel = dte.ActiveDocument.ProjectItem?.FileCodeModel;
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
                    FullCode = functionCode
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
    }
}