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
using Microsoft.VisualStudio.Shell;
using System;
using System.ComponentModel.Design;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace Integrated_AI.Commands
{
    /// <summary>
    /// Handles commands for applying code from the AI chat back to the Visual Studio editor.
    /// </summary>
    internal sealed class AiToVsCommands : BaseCommand
    {
        // Use a new range of command IDs
        public const int ReplaceSnippetWithAICmdId = 0x0130;
        public const int ReplaceFunctionWithAICmdId = 0x0131;
        public const int ReplaceFileWithAICmdId = 0x0132;
        public const int CreateNewFileWithAICmdId = 0x0133;

        // Use the same CommandSet GUID as VsToAiCommands
        public static readonly Guid CommandSet = new Guid("3e9439f0-9188-4415-b861-b894c074a254");

        private AiToVsCommands(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService)
            : base(package, dte, commandService)
        {
            // Register all the new commands
            AddOleCommand(CommandSet, ReplaceSnippetWithAICmdId, this.ExecuteReplaceSnippet, this.BeforeQueryStatusReplaceSnippet);
            AddOleCommand(CommandSet, ReplaceFunctionWithAICmdId, this.ExecuteReplaceFunction, this.BeforeQueryStatusActiveDocument);
            AddOleCommand(CommandSet, ReplaceFileWithAICmdId, this.ExecuteReplaceFile, this.BeforeQueryStatusActiveDocument);
            AddOleCommand(CommandSet, CreateNewFileWithAICmdId, this.ExecuteCreateNewFile, this.BeforeQueryStatusAlwaysVisible);
        }

        public static AiToVsCommands Instance { get; private set; }

        public static void Initialize(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService)
        {
            Instance = new AiToVsCommands(package, dte, commandService);
        }

        #region Visibility Handlers (BeforeQueryStatus)

        private void BeforeQueryStatusAlwaysVisible(object sender, EventArgs e)
        {
            var cmd = sender as OleMenuCommand;
            if (cmd != null)
            {
                cmd.Visible = true;
            }
        }

        private void BeforeQueryStatusActiveDocument(object sender, EventArgs e)
        {
            var cmd = sender as OleMenuCommand;
            if (cmd != null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                cmd.Visible = Dte.ActiveDocument != null;
            }
        }

        private void BeforeQueryStatusReplaceSnippet(object sender, EventArgs e)
        {
            var cmd = sender as OleMenuCommand;
            if (cmd != null)
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                var textSelection = (TextSelection)Dte.ActiveDocument?.Selection;
                // This command should only be visible if there's text selected in the editor.
                cmd.Visible = textSelection != null && !string.IsNullOrEmpty(textSelection.Text);
            }
        }

        #endregion

        #region Command Handlers

        private async void ExecuteReplaceSnippet(object sender, EventArgs e) => await ExecuteAiToVsCommandAsync("snippet");
        private async void ExecuteReplaceFunction(object sender, EventArgs e) => await ExecuteAiToVsCommandAsync("function");
        private async void ExecuteReplaceFile(object sender, EventArgs e) => await ExecuteAiToVsCommandAsync("file");
        private async void ExecuteCreateNewFile(object sender, EventArgs e) => await ExecuteAiToVsCommandAsync("new_file");

        #endregion

        #region Core Logic

        /// <summary>
        /// Centralized logic for all AI-to-VS commands.
        /// </summary>
        private async Task ExecuteAiToVsCommandAsync(string applyType)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Step 1: Get the ChatWindow instance.
            var chatToolWindow = this.AsyncPackage.FindToolWindow(typeof(ChatToolWindow), 0, true) as ChatToolWindow;
            var chatWindow = chatToolWindow?.Content as ChatWindow;
            if (chatWindow == null)
            {
                // This uses the static ThemedMessageBox since we don't have a window instance
                ThemedMessageBox.Show(null, "The AI Chat window must be open to use this command.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            using (var cts = new CancellationTokenSource())
            {
                try
                {
                    // Step 2: Get AI code from the window's public API.
                    string aiCode = await chatWindow.GetAiCodeAsync();
                    if (string.IsNullOrEmpty(aiCode))
                    {
                        chatWindow.ShowThemedMessageBox("No code was found highlighted in the AI chat or in the clipboard.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                        return;
                    }

                    // Step 3: Pre-flight checks.
                    if (Dte.ActiveDocument == null && applyType != "new_file")
                    {
                        chatWindow.ShowThemedMessageBox("An active document is required for this operation.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }

                    // Step 4: Prepare parameters for the final action.
                    string pasteTypeForLogic = applyType;
                    string functionName = null;

                    if (applyType == "function")
                    {
                        if (!StringUtil.IsWordAtCursor(Dte.ActiveDocument, out string targetFunctionName))
                        {
                            chatWindow.ShowThemedMessageBox("Please place your cursor on the name of the function you wish to replace.", "Function Not Selected", MessageBoxButton.OK, MessageBoxImage.Information);
                            return;
                        }
                        functionName = targetFunctionName;
                    }

                    // Step 5: Call the window's public API to perform the diff/file creation.
                    await chatWindow.CreateDiffOrNewFileAsync(aiCode, cts.Token, pasteTypeForLogic, functionName);
                }
                catch (Exception ex)
                {
                    WebViewUtilities.Log($"Error executing AI to VS command ('{applyType}'): {ex.Message}\n{ex.StackTrace}");
                    chatWindow.ShowThemedMessageBox($"An unexpected error occurred: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        #endregion
    }
}