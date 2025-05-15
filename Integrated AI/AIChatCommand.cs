using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.ComponentModel.Design;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

namespace Integrated_AI
{
    internal sealed class AIChatCommand
    {
        public const int CommandId = 0x0100;
        public static readonly Guid CommandSet = new Guid("3e9439f0-9188-4415-b861-b894c074a254");
        private readonly AsyncPackage package;

        private AIChatCommand(AsyncPackage package, OleMenuCommandService commandService)
        {
            this.package = package ?? throw new ArgumentNullException(nameof(package));
            commandService = commandService ?? throw new ArgumentNullException(nameof(commandService));

            var menuCommandID = new CommandID(CommandSet, CommandId);
            var menuItem = new MenuCommand(this.Execute, menuCommandID);
            commandService.AddCommand(menuItem);
        }

        public static AIChatCommand Instance { get; private set; }

        private Microsoft.VisualStudio.Shell.IAsyncServiceProvider ServiceProvider
        {
            get { return this.package; }
        }

        public static async Task InitializeAsync(AsyncPackage package)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(package.DisposalToken);

            OleMenuCommandService commandService = await package.GetServiceAsync(typeof(IMenuCommandService)) as OleMenuCommandService;
            Instance = new AIChatCommand(package, commandService);
        }

        private void Execute(object sender, EventArgs e)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Find or create the tool window
            ToolWindowPane window = this.package.FindToolWindow(typeof(ChatToolWindow), 0, true);
            if (window == null || window.Frame == null)
            {
                throw new NotSupportedException("Cannot create AI chat tool window");
            }

            // Show the tool window (docking is handled by ProvideToolWindow)
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
            this.Content = new ChatWindow(); // Assumes ChatWindow.xaml exists
        }
    }
}