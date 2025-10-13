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

using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;

namespace Integrated_AI.Commands
{
    internal sealed class GeneralCommands : BaseCommand
    {
        public const int OpenChatWindowCmdId = 0x0100;
        public static readonly Guid CommandSet = new Guid("3e9439f0-9188-4415-b861-b894c074a254");

        private GeneralCommands(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService)
            : base(package, dte, commandService)
        {
            AddCommand(CommandSet, OpenChatWindowCmdId, this.ExecuteOpenChatWindow);
        }

        public static GeneralCommands Instance { get; private set; }

        public static void Initialize(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService)
        {
            Instance = new GeneralCommands(package, dte, commandService);
        }

        public void ExecuteOpenChatWindow(object sender, EventArgs e)
        {
            // Logic from your original AIChatCommand.cs
            ThreadHelper.ThrowIfNotOnUIThread();
            ToolWindowPane window = this.AsyncPackage.FindToolWindow(typeof(ChatToolWindow), 0, true);
            if (window?.Frame == null)
            {
                throw new NotSupportedException("Cannot create AI chat tool window");
            }
            IVsWindowFrame windowFrame = (IVsWindowFrame)window.Frame;
            Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(windowFrame.Show());
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