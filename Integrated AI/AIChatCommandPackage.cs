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

using Integrated_AI.Utilities;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;
using System;
using System.ComponentModel.Design;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Integrated_AI.Commands;
using Task = System.Threading.Tasks.Task;
using EnvDTE80; // <<< ADD THIS USING DIRECTIVE

namespace Integrated_AI
{
    [PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
    [ProvideMenuResource("Menus.ctmenu", 1)]
    [Guid(AIChatCommandPackage.PackageGuidString)]
    [SuppressMessage("StyleCop.CSharp.DocumentationRules", "SA1650:ElementDocumentationMustBeSpelledCorrectly", Justification = "pkgdef, VS and vsixmanifest are valid VS terms")]
    [ProvideToolWindow(typeof(ChatToolWindow), Style = VsDockStyle.Tabbed, Orientation = ToolWindowOrientation.Right)]
    public sealed class AIChatCommandPackage : AsyncPackage
    {
        public const string PackageGuidString = "9c8c1990-fd9f-47f4-aa39-3da3fd4b5c39";

        public AIChatCommandPackage()
        {
        }

        #region Package Members

        protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
        {
            await this.JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);

            ThemeUtility.Initialize();

            // --- START OF CHANGES ---

            // 1. GET THE NECESSARY SERVICES
            var dte = await GetServiceAsync(typeof(SDTE)) as DTE2;
            var commandService = await GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;

            // Ensure services were found
            if (dte == null || commandService == null)
            {
                // Handle the error, maybe log it or show a message
                return;
            }

            GeneralCommands.Initialize(this, dte, commandService);
            VsToAiCommands.Initialize(this, dte, commandService);
            AiToVsCommands.Initialize(this, dte, commandService);
        }

        #endregion
    }
}
