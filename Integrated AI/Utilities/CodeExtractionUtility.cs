using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public class CodeExtractionUtility
    {
        private readonly DTE2 _dte;
        private readonly string _userDataFolder;

        public CodeExtractionUtility(DTE2 dte, string userDataFolder)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
            _userDataFolder = userDataFolder ?? throw new ArgumentNullException(nameof(userDataFolder));
        }

        public async Task<(string Text, string SourceDescription)?> ExtractCodeAsync(string option, string relativePath, Action<string> log)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (_dte.ActiveDocument == null)
            {
                MessageBox.Show("No active document in Visual Studio.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                return null;
            }

            string textToInject = null;
            string sourceDescription = "";
            string solutionPath = Path.GetDirectoryName(_dte.Solution.FullName);

            if (option == "Code -> AI")
            {
                var textSelection = (TextSelection)_dte.ActiveDocument.Selection;
                if (string.IsNullOrEmpty(textSelection?.Text))
                {
                    MessageBox.Show("No text selected in Visual Studio.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return null;
                }
                textToInject = textSelection.Text;
                sourceDescription = $"---{relativePath} (partial code block)---\n{textToInject}\n---End code---\n\n";
            }
            else if (option == "File -> AI")
            {
                if (_dte.ActiveDocument.Object("TextDocument") is TextDocument textDocument)
                {
                    var text = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
                    if (string.IsNullOrEmpty(text))
                    {
                        MessageBox.Show("The active document is empty.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        return null;
                    }
                    textToInject = text;
                    sourceDescription = $"---{relativePath} (whole file contents)---\n{textToInject}\n---End code---\n\n";
                }
                else
                {
                    MessageBox.Show("Could not get text document from active document.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return null;
                }
            }
            else if (option == "Function -> AI")
            {
                var functions = GetFunctionsFromActiveDocument();
                if (!functions.Any())
                {
                    MessageBox.Show("No functions found in the active document.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return null;
                }

                string recentFunctionsFilePath = Path.Combine(_userDataFolder, "recent_functions.txt");
                var functionSelectionWindow = new FunctionSelectionWindow(functions, recentFunctionsFilePath, relativePath);
                if (functionSelectionWindow.ShowDialog() == true && functionSelectionWindow.SelectedFunction != null)
                {
                    textToInject = functionSelectionWindow.SelectedFunction.FullCode;
                    sourceDescription = $"---{relativePath} (function: {functionSelectionWindow.SelectedFunction.DisplayName})---\n{textToInject}\n---End code---\n\n";
                }
                else
                {
                    return null;
                }
            }

            return (textToInject, sourceDescription);
        }

        public List<FunctionSelectionWindow.FunctionItem> GetFunctionsFromActiveDocument()
        {
            var functions = new List<FunctionSelectionWindow.FunctionItem>();
            if (_dte.ActiveDocument?.Object("TextDocument") is TextDocument textDocument)
            {
                var codeModel = _dte.ActiveDocument.ProjectItem?.FileCodeModel;
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

        private void CollectFunctions(CodeElement element, List<FunctionSelectionWindow.FunctionItem> functions)
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

        public string GetRelativePath()
        {
            string solutionPath = Path.GetDirectoryName(_dte.Solution.FullName);
            string filePath = _dte.ActiveDocument.FullName;
            if (!string.IsNullOrEmpty(solutionPath) && filePath.StartsWith(solutionPath, StringComparison.OrdinalIgnoreCase))
            {
                return filePath.Substring(solutionPath.Length + 1).Replace("\\", "/");
            }
            return Path.GetFileName(filePath);
        }
    }
}