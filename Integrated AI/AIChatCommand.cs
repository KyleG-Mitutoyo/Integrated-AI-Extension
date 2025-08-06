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
using System.ComponentModel.Design;
using System.Diagnostics;
using System.IO;
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
                IVsTaskItem selectedTaskItem = null;

                if (await _package.GetServiceAsync(typeof(SVsErrorList)) is IVsTaskList2 taskList)
                {
                    taskList.EnumSelectedItems(out IVsEnumTaskItems itemsEnum);
                    if (itemsEnum != null)
                    {
                        var taskItems = new IVsTaskItem[1];
                        if (itemsEnum.Next(1, taskItems, null) == VSConstants.S_OK && taskItems[0] != null)
                        {
                            selectedTaskItem = taskItems[0];
                        }
                    }
                }

                if (selectedTaskItem == null)
                {
                    MessageBox.Show("Could not get the selected error from the Error List.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                string itemType = "Error"; // Default to Error.
                string errorCode = string.Empty;
                string itemDescription = string.Empty;

                // --- THE FIX: Use the IVsErrorItem interface for structured data ---
                // This is the correct and reliable way to get details from the Error List.
                if (selectedTaskItem is IVsErrorItem errorItem)
                {
                    // Get Severity (Error, Warning, Message)
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

                if (string.IsNullOrEmpty(itemDescription))
                {
                    selectedTaskItem.get_Text(out itemDescription);
                }

                selectedTaskItem.NavigateTo();

                await Task.Delay(50); // Small delay to ensure navigation completes

                Document activeDoc = dte.ActiveDocument;
                if (activeDoc == null)
                {
                     MessageBox.Show("Could not navigate to the source code for the selected item.", "Navigation Failed", MessageBoxButton.OK, MessageBoxImage.Error);
                     return;
                }

                await SendErrorAsync(activeDoc, itemType, errorCode, itemDescription);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Error in ExecuteSendErrorToAI: {ex.Message}");
                MessageBox.Show($"An unexpected error occurred: {ex.Message}", "Command Failed", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        
        private async Task SendErrorAsync(Document activeDoc, string itemType, string errorCode, string itemDescription)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var textSelection = (TextSelection)activeDoc.Selection;
            string errorFilePath = activeDoc.FullName;
            int errorLine = textSelection.CurrentLine;
            textSelection.SelectLine();
            string lineOfCode = textSelection.Text;
            textSelection.Cancel();

            ExecuteOpenChatWindow(null, null);

            var chatToolWindow = this._package.FindToolWindow(typeof(ChatToolWindow), 0, false) as ChatToolWindow;
            var chatWindow = chatToolWindow?.Content as ChatWindow;

            if (chatWindow == null) return;
            WebView2 webView = chatWindow.ChatWebView;
            Window parentWindow = Window.GetWindow(chatWindow);
            if (webView == null || parentWindow == null) return;

            string solutionPath = Path.GetDirectoryName(dte.Solution.FullName);
            string errorRelativePath = FileUtil.GetRelativePath(solutionPath, errorFilePath);

            string textToInject;
            if (!string.IsNullOrWhiteSpace(errorCode))
            {
                textToInject = $"{itemType} ({errorCode}): {itemDescription}\nCode:\n{lineOfCode.Trim()}";
            }
            else
            {
                textToInject = $"{itemType}: {itemDescription}\nCode:\n{lineOfCode.Trim()}";
            }
            
            string sourceDescription = $"\n---{errorRelativePath} ({itemType.ToLower()} on line {errorLine})---\n{textToInject}\n---End code---\n\n";

            await WebViewUtilities.InjectTextIntoWebViewAsync(webView, parentWindow, sourceDescription, "Error -> AI");
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