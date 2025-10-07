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
using Microsoft.Web.WebView2.Wpf;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using Task = System.Threading.Tasks.Task;
using Window = System.Windows.Window;
using Microsoft.VisualStudio;
using System.Runtime.InteropServices;

namespace Integrated_AI
{
    internal sealed class AIChatCommand
    {
        public const int OpenChatWindowCmdId = 0x0100;
        public const int SendErrorToAICmdId = 0x0105;

        public static readonly Guid CommandSet = new Guid("3e9439f0-9188-4415-b861-b894c074a254");
        private readonly AsyncPackage _package;
        private readonly DTE2 dte;

        private AIChatCommand(AsyncPackage package, OleMenuCommandService commandService, DTE2 dte)
        {
            this._package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
            this.dte = dte ?? throw new ArgumentNullException(nameof(dte));

            var openWindowId = new CommandID(CommandSet, OpenChatWindowCmdId);
            var openWindowItem = new MenuCommand(this.ExecuteOpenChatWindow, openWindowId);
            commandService.AddCommand(openWindowItem);
            
            var sendErrorId = new CommandID(CommandSet, SendErrorToAICmdId);
            var sendErrorItem = new MenuCommand(this.ExecuteSendErrorToAI, sendErrorId);
            commandService.AddCommand(sendErrorItem);
        }

        public static AIChatCommand Instance { get; private set; }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);
            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            DTE2 dte = await package.GetServiceAsync(typeof(SDTE)) as DTE2;
            Instance = new AIChatCommand(package, commandService, dte);
        }

        private void ExecuteOpenChatWindow(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            ToolWindowPane window = this._package.FindToolWindow(typeof(ChatToolWindow), 0, true);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create AI chat tool window");
            }
            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            ErrorHandler.ThrowOnFailure(windowFrame.Show());
        }

        private async void ExecuteSendErrorToAI(object sender, EventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            try
            {
                if (!(await _package.GetServiceAsync(typeof(SVsErrorList)) is IVsTaskList2 taskList)) return;

                taskList.EnumSelectedItems(out IVsEnumTaskItems itemsEnum);
                if (itemsEnum == null) return;
                
                // --- NEW: Collect all items first to get a count ---
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

                // --- NEW: Branch logic based on selection count ---
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

        // --- Handles the original case for a single selected error ---
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
                    // --- FIX: C# 7.3 compatible switch statement ---
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

            await Task.Delay(50); // Small delay to ensure navigation completes

            Document activeDoc = dte.ActiveDocument;
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

            string solutionPath = Path.GetDirectoryName(dte.Solution.FullName);
            string errorRelativePath = FileUtil.GetRelativePath(solutionPath, errorFilePath);

            string textToInject = $"{itemType}: {itemDescription}\nCode:\n{lineOfCode}";
            string sourceDescription = $"\n---{errorRelativePath} ({itemType.ToLower()} on line {errorLine})---\n{textToInject}\n---End code---\n\n";

            await SendTextToAIAsync(sourceDescription, "Error -> AI");
        }

        // --- NEW: Handles multiple selections by sending descriptions only ---
        private async Task HandleMultipleErrorsAsync(List<IVsTaskItem> items)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var promptBuilder = new StringBuilder();
            promptBuilder.AppendLine("Here are multiple issues from my project. Can you analyze them?\n");

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
        
        private async Task SendTextToAIAsync(string text, string commandTitle)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
            ExecuteOpenChatWindow(null, null);

            var chatToolWindow = this._package.FindToolWindow(typeof(ChatToolWindow), 0, false) as ChatToolWindow;
            var chatWindow = chatToolWindow?.Content as ChatWindow;

            if (chatWindow == null) return;
            WebView2 webView = chatWindow.ChatWebView;
            Window parentWindow = Window.GetWindow(chatWindow);
            if (webView == null || parentWindow == null) return;

            await WebViewUtilities.InjectTextIntoWebViewAsync(webView, parentWindow, text, commandTitle);
        }
    }

    [Guid("7513ff4e-9271-4984-bbcd-46ff9e185399")]
    public class ChatToolWindow : ToolWindowPane
    {
        public ChatToolWindow() : base(null)
        {
            this.Caption = "AI Chat";
            this.Content = new ChatWindow();
        }
    }
}