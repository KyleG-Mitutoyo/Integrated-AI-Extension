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
using Integrated_AI.Utilities;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Web.WebView2.Wpf;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.IO;
using System.IO.Packaging;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using Window = System.Windows.Window;

namespace Integrated_AI.Commands
{
    internal sealed class VsToAiCommands : BaseCommand
    {
        public const int SendErrorToAICmdId = 0x0105;
        public const int SendSnippetToAICmdId = 0x0110;
        public const int SendFunctionToAICmdId = 0x0111;
        public const int SendFileToAICmdId = 0x0112;
        public const int SendMultipleFilesToAICmdId = 0x0113;

        public static readonly Guid CommandSet = new Guid("3e9439f0-9188-4415-b861-b894c074a254");

        private VsToAiCommands(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService)
            : base(package, dte, commandService)
        {
            AddCommand(CommandSet, SendErrorToAICmdId, this.ExecuteSendErrorToAI);
            AddOleCommand(CommandSet, SendSnippetToAICmdId, this.ExecuteSendSnippetToAI, this.BeforeQueryStatusSendSnippet);
            AddOleCommand(CommandSet, SendFunctionToAICmdId, this.ExecuteSendFunctionToAI); // Note: BeforeQueryStatus could be added here too if needed
            AddCommand(CommandSet, SendFileToAICmdId, this.ExecuteSendFileToAI);
            AddCommand(CommandSet, SendMultipleFilesToAICmdId, this.ExecuteSendMultipleFilesToAI);
        }

        public static VsToAiCommands Instance { get; private set; }

        public static void Initialize(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService)
        {
            Instance = new VsToAiCommands(package, dte, commandService);
        }

        #region Command Handlers
        private void BeforeQueryStatusSendSnippet(object sender, EventArgs e)
        {
            var cmd = sender as OleMenuCommand;
            if (cmd != null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var textSelection = (TextSelection)Dte.ActiveDocument?.Selection;
                cmd.Visible = textSelection != null && !string.IsNullOrEmpty(textSelection.Text);
            }
        }

        private async void ExecuteSendSnippetToAI(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var textSelection = (TextSelection)Dte.ActiveDocument.Selection;
            if (string.IsNullOrEmpty(textSelection?.Text)) return;

            string code = StringUtil.RemoveBaseIndentation(textSelection.Text);
            string relativePath = GetRelativePath(Dte.ActiveDocument.FullName);
            string header = $"---{relativePath} (partial code block)---";
            await FormatSendPromptToAIAsync(code, header, "Snippet -> AI");
        }

        // CHANGE: The entire ExecuteSendFunctionToAI method is updated for responsiveness.
        private async void ExecuteSendFunctionToAI(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // STEP 1: Perform the fast, synchronous part first.
            if (!StringUtil.TryGetWordAtCursor(Dte.ActiveDocument, out string functionName))
            {
                ShowFunctionNotFoundMessage();
                return;
            }

            // CHANGE: Get status bar service to provide user feedback.
            IVsStatusbar statusBar = await AsyncPackage.GetServiceAsync(typeof(SVsStatusbar)) as IVsStatusbar;

            try
            {
                // STEP 2: Provide immediate feedback to the user.
                statusBar?.SetText($"Analyzing functions in {Path.GetFileName(Dte.ActiveDocument.Name)}...");

                // STEP 3: Yield control to the UI thread. This is the crucial step.
                // It allows the status bar message to render and keeps VS responsive
                // before we start the slow operation.
                await Task.Yield();

                // STEP 4: Now perform the slow, UI-thread-bound operation.
                var allFunctions = CodeSelectionUtilities.GetFunctionsFromDocument(Dte.ActiveDocument);
                var functionItem = allFunctions.FirstOrDefault(f => f.DisplayName == functionName);

                if (functionItem != null)
                {
                    string code = StringUtil.FixExtraIndentation(functionItem.FullCode);
                    string relativePath = GetRelativePath(Dte.ActiveDocument.FullName);
                    string header = $"---{relativePath} (function: {functionItem.DisplayName})---";
                    await FormatSendPromptToAIAsync(code, header, "Function -> AI");
                }
                else
                {
                    ShowFunctionNotFoundMessage();
                }
            }
            finally
            {
                // STEP 5: Always clear the status bar message.
                statusBar?.Clear();
            }
        }

        private void ExecuteSendFileToAI(object sender, EventArgs e)
        {
            _ = SendFileContentToAIAsync(Dte.ActiveDocument);
        }

        private async void ExecuteSendMultipleFilesToAI(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var selectedFiles = CodeSelectionUtilities.ShowMultiFileSelectionWindow(Dte);
            if (selectedFiles == null || !selectedFiles.Any()) return;

            GeneralCommands.Instance.ExecuteOpenChatWindow(null, null);
            var chatToolWindow = this.AsyncPackage.FindToolWindow(typeof(ChatToolWindow), 0, false) as ChatToolWindow;
            var chatWindow = chatToolWindow?.Content as ChatWindow;
            if (chatWindow == null) return;

            var selectedOption = chatWindow.UrlSelector.SelectedItem as ChatWindow.AIChatOption;
            bool useMarkdown = selectedOption?.UsesMarkdown ?? false;

            var promptBuilder = new StringBuilder();
            //promptBuilder.AppendLine("Here are the contents of multiple files:\n");

            foreach (var fileItem in selectedFiles)
            {
                try
                {
                    string code = File.ReadAllText(fileItem.FilePath);
                    string relativePath = GetRelativePath(fileItem.FilePath);
                    string header = $"---{relativePath} (whole file contents)---";

                    if (useMarkdown)
                    {
                        promptBuilder.Append($"\n{header}\n\n```code\n{code}\n```\n\n---End code---\n\n");
                    }
                    else
                    {
                        promptBuilder.Append($"\n{header}\n{code}\n---End code---\n\n");
                    }
                }
                catch (Exception ex)
                {
                    WebViewUtilities.Log($"Error reading file {fileItem.FilePath}: {ex.Message}");
                }
            }

            if (promptBuilder.Length > "Here are the contents of multiple files:\n".Length)
            {
                await SendTextToAIAsync(promptBuilder.ToString(), "Multiple Files -> AI");
            }
        }

        private async void ExecuteSendErrorToAI(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                if (!(await AsyncPackage.GetServiceAsync(typeof(SVsErrorList)) is IVsTaskList2 taskList)) return;

                taskList.EnumSelectedItems(out IVsEnumTaskItems itemsEnum);
                if (itemsEnum == null) return;

                var selectedItems = new List<IVsTaskItem>();
                var taskItemsBuffer = new IVsTaskItem[1];
                while (itemsEnum.Next(1, taskItemsBuffer, null) == VSConstants.S_OK && taskItemsBuffer[0] != null)
                {
                    selectedItems.Add(taskItemsBuffer[0]);
                }

                if (selectedItems.Count == 0)
                {
                    ThemedMessageBox.Show(null, "No items selected in the Error List.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                if (selectedItems.Count == 1)
                {
                    await HandleSingleErrorAsync(selectedItems[0]);
                }
                else
                {
                    await HandleMultipleErrorsAsync(selectedItems);
                }
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Error in ExecuteSendErrorToAI: {ex.Message}");
                ThemedMessageBox.Show(null, $"An unexpected error occurred: {ex.Message}", "Command Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task HandleSingleErrorAsync(IVsTaskItem selectedTaskItem)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string itemType = "Error";
            string itemDescription = string.Empty;

            if (selectedTaskItem is IVsErrorItem errorItem)
            {
                if (errorItem.GetCategory(out uint severityRaw) == VSConstants.S_OK)
                {
                    var severity = (__VSERRORCATEGORY)severityRaw;
                    switch (severity)
                    {
                        case __VSERRORCATEGORY.EC_ERROR:
                            itemType = "Error";
                            break;
                        case __VSERRORCATEGORY.EC_WARNING:
                            itemType = "Warning";
                            break;
                        case __VSERRORCATEGORY.EC_MESSAGE:
                            itemType = "Message";
                            break;
                    }
                }
            }

            selectedTaskItem.get_Text(out itemDescription);
            selectedTaskItem.NavigateTo();

            await Task.Delay(50);

            Document activeDoc = Dte.ActiveDocument;
            if (activeDoc == null)
            {
                WebViewUtilities.Log("Error to AI: Could not navigate to the source code for the selected item.");
                ThemedMessageBox.Show(null, "Could not navigate to the source code for the selected item.", "Navigation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            var textSelection = (TextSelection)activeDoc.Selection;
            string errorFilePath = activeDoc.FullName;
            int errorLine = textSelection.CurrentLine;
            textSelection.SelectLine();
            string lineOfCode = textSelection.Text.Trim();
            textSelection.Cancel();

            string errorRelativePath = GetRelativePath(errorFilePath);

            string textToInject = $"{itemType}: {itemDescription}\nCode:\n{lineOfCode}";
            string prompt = "This error showed up. How should the code be fixed?";
            string sourceDescription = $"{prompt}\n---{errorRelativePath} ({itemType.ToLower()} on line {errorLine})---\n{textToInject}\n---End code---\n\n";

            await SendTextToAIAsync(sourceDescription, "Error -> AI");
        }

        private async Task HandleMultipleErrorsAsync(List<IVsTaskItem> items)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("Here are multiple issues that are showing up. Can you fix them?\n");

            foreach (var item in items)
            {
                string itemType = "Item";
                if (item is IVsErrorItem errorItem && errorItem.GetCategory(out uint severityRaw) == VSConstants.S_OK)
                {
                    var severity = (__VSERRORCATEGORY)severityRaw;
                    switch (severity)
                    {
                        case __VSERRORCATEGORY.EC_ERROR: itemType = "Error"; break;
                        case __VSERRORCATEGORY.EC_WARNING: itemType = "Warning"; break;
                        case __VSERRORCATEGORY.EC_MESSAGE: itemType = "Message"; break;
                    }
                }

                item.get_Text(out string description);
                promptBuilder.AppendLine($"- **{itemType}:** {description}");
            }

            await SendTextToAIAsync(promptBuilder.ToString(), "Multiple Errors -> AI");
        }

        #endregion

        #region Helpers

        private void ShowFunctionNotFoundMessage()
        {
            ThemedMessageBox.Show(
                owner: null,
                message: "No valid function found at the current cursor position. Please right-click on a function name that is defined within the current document.",
                caption: "Function Not Found",
                button: MessageBoxButton.OK,
                icon: MessageBoxImage.Information);
        }

        private async Task SendFileContentToAIAsync(Document document)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            if (document == null) return;

            string code = DiffUtility.GetDocumentText(document);
            if (string.IsNullOrEmpty(code)) return;

            string relativePath = GetRelativePath(document.FullName);
            string header = $"---{relativePath} (whole file contents)---";

            // Re-implement the check to prevent duplicate file content injection.
            GeneralCommands.Instance.ExecuteOpenChatWindow(null, null);
            var chatToolWindow = this.AsyncPackage.FindToolWindow(typeof(ChatToolWindow), 0, false) as ChatToolWindow;
            var chatWindow = chatToolWindow?.Content as ChatWindow;
            Window parentWindow = chatWindow != null ? Window.GetWindow(chatWindow) : null;

            if (chatWindow != null && chatWindow.ChatWebView != null && parentWindow != null)
            {
                string currentContent = await WebViewUtilities.RetrieveTextFromWebViewAsync(chatWindow.ChatWebView, parentWindow);

                if (!string.IsNullOrEmpty(currentContent) && currentContent.Contains(header))
                {
                    WebViewUtilities.Log("This file's contents have already been injected into the chat.");
                    ThemedMessageBox.Show(parentWindow, "This file's contents have already been injected.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                    return; // Exit without sending duplicate content
                }
            }

            // If check was not performed or passed, continue to send the code.
            await FormatSendPromptToAIAsync(code, header, "File -> AI");
        }

        private async Task FormatSendPromptToAIAsync(string code, string header, string commandTitle)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            GeneralCommands.Instance.ExecuteOpenChatWindow(null, null);
            var chatToolWindow = this.AsyncPackage.FindToolWindow(typeof(ChatToolWindow), 0, false) as ChatToolWindow;
            var chatWindow = chatToolWindow?.Content as ChatWindow;
            if (chatWindow == null)
            {
                WebViewUtilities.Log("Could not find the ChatWindow instance.");
                return;
            }

            var selectedOption = chatWindow.UrlSelector.SelectedItem as ChatWindow.AIChatOption;
            bool useMarkdown = selectedOption?.UsesMarkdown ?? false;

            string sourceDescription;
            if (useMarkdown)
            {
                sourceDescription = $"\n{header}\n\n```code\n{code}\n```\n\n---End code---\n\n";
            }
            else
            {
                sourceDescription = $"\n{header}\n{code}\n---End code---\n\n";
            }

            await SendTextToAIAsync(sourceDescription, commandTitle);
        }

        private string GetRelativePath(string fullPath)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Dte.Solution == null || string.IsNullOrEmpty(Dte.Solution.FullName)) return Path.GetFileName(fullPath);
            string solutionPath = Path.GetDirectoryName(Dte.Solution.FullName);
            return FileUtil.GetRelativePath(solutionPath, fullPath);
        }

        private async Task SendTextToAIAsync(string text, string commandTitle)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            GeneralCommands.Instance.ExecuteOpenChatWindow(null, null);

            var chatToolWindow = this.AsyncPackage.FindToolWindow(typeof(ChatToolWindow), 0, false) as ChatToolWindow;
            var chatWindow = chatToolWindow?.Content as ChatWindow;

            if (chatWindow == null) return;
            WebView2 webView = chatWindow.ChatWebView;
            Window parentWindow = Window.GetWindow(chatWindow);
            if (webView == null || parentWindow == null) return;

            await WebViewUtilities.InjectTextIntoWebViewAsync(webView, parentWindow, text, commandTitle);
        }

        #endregion
    }
}