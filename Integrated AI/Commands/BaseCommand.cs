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
    internal abstract class BaseCommand
    {
        protected readonly AsyncPackage AsyncPackage;
        protected readonly DTE2 Dte;
        protected readonly OleMenuCommandService CommandService;

        protected BaseCommand(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService)
        {
            AsyncPackage = package ?? throw new ArgumentNullException(nameof(package));
            Dte = dte ?? throw new ArgumentNullException(nameof(dte));
            CommandService = commandService ?? throw new ArgumentNullException(nameof(commandService));
        }

        public static async Task InitializeAsync<T>(AsyncPackage package, DTE2 dte, OleMenuCommandService commandService) where T : BaseCommand, new()
        {
            // This generic InitializeAsync is a concept, but construction with parameters is cleaner.
            // In the package, you will construct each command class directly.
        }

        protected void AddCommand(Guid commandSet, int commandId, EventHandler handler)
        {
            var menuCommandID = new CommandID(commandSet, commandId);
            var menuItem = new MenuCommand(handler, menuCommandID);
            CommandService.AddCommand(menuItem);
        }

        protected void AddOleCommand(Guid commandSet, int commandId, EventHandler handler, EventHandler beforeQueryStatus = null)
        {
            var menuCommandID = new CommandID(commandSet, commandId);
            var menuItem = new OleMenuCommand(handler, menuCommandID);
            if (beforeQueryStatus != null)
            {
                menuItem.BeforeQueryStatus += beforeQueryStatus;
            }
            CommandService.AddCommand(menuItem);
        }
    }
}