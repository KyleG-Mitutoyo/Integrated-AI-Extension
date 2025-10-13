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
using System;
using System.ComponentModel.Design;
using System.Threading.Tasks;

namespace Integrated_AI.Commands
{
    /// <summary>
    /// Handles commands for sending code from the AI chat back to the Visual Studio editor.
    /// This is a placeholder for future implementation.
    /// </summary>
    internal sealed class AiToVsCommands : BaseCommand
    {
        // Define new command IDs for the "AI to VS" operations.
        // Using a new number range (e.g., 0x012x) is good practice.
        public const int ApplyAsSnippetFromAICmdId = 0x0120;
        public const int ApplyAsFunctionFromAICmdId = 0x0121;
        public const int ApplyAsNewFileFromAICmdId  = 0x0122; // Based on your README's "new file option"

        private AiToVsCommands(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService)
            : base(package, dte, commandService)
        {
            // When you are ready to implement these, you will:
            // 1. Add these commands to your .vsct file.
            // 2. Uncomment the registration lines below.
            // 3. Implement the Execute... methods.

            // AddCommand(PackageIds.CommandSet, ApplyAsSnippetFromAICmdId, this.ExecuteApplyAsSnippetFromAI);
            // AddCommand(PackageIds.CommandSet, ApplyAsFunctionFromAICmdId, this.ExecuteApplyAsFunctionFromAI);
            // AddCommand(PackageIds.CommandSet, ApplyAsNewFileFromAICmdId, this.ExecuteApplyAsNewFileFromAI);
        }

        public static AiToVsCommands Instance { get; private set; }

        public static void Initialize(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService)
        {
            Instance = new AiToVsCommands(package, dte, commandService);
        }

        #region Placeholder Command Handlers

        // private void ExecuteApplyAsSnippetFromAI(object sender, EventArgs e)
        // {
        //     ThreadHelper.ThrowIfNotOnUIThread();
        //     // TODO: Get highlighted text from WebView
        //     // TODO: Get selected text in the active document
        //     // TODO: Show Diff view
        //     // TODO: Replace text on accept
        // }

        // private void ExecuteApplyAsFunctionFromAI(object sender, EventArgs e)
        // {
        //     ThreadHelper.ThrowIfNotOnUIThread();
        //     // TODO: Get highlighted text from WebView
        //     // TODO: Parse function name from AI code
        //     // TODO: Find matching function in active document or insert new
        //     // TODO: Show Diff view
        // }

        // private void ExecuteApplyAsNewFileFromAI(object sender, EventArgs e)
        // {
        //     ThreadHelper.ThrowIfNotOnUIThread();
        //     // TODO: Get highlighted text from WebView
        //     // TODO: Prompt for filename and location
        //     // TODO: Create new file and add it to the project
        // }

        #endregion
    }
}