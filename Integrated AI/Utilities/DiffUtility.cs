using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Diagnostics;
using System.IO;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public static class DiffUtility
    {
        public class DiffContext
        {
            public string TempCurrentFile { get; set; }
            public string TempAiFile { get; set; }
            public IVsWindowFrame DiffFrame { get; set; }
            public IVsTextLines RightTextBuffer { get; set; } // Store the right-hand buffer
        }

        public static DiffContext OpenDiffView(DTE2 dte, string currentCode, string aiCode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte == null || currentCode == null || aiCode == null)
            {
                MessageBox.Show("Invalid input: DTE or code strings are null.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            string extension = Path.GetExtension(dte.ActiveDocument.FullName) ?? ".txt";

            var context = new DiffContext
            {
                TempCurrentFile = Path.Combine(Path.GetTempPath(), $"Current_{Guid.NewGuid()}{extension}"),
                TempAiFile = Path.Combine(Path.GetTempPath(), $"AI_{Guid.NewGuid()}{extension}")
            };

            try
            {
                File.WriteAllText(context.TempCurrentFile, currentCode);
                File.SetAttributes(context.TempCurrentFile, FileAttributes.Normal);
                File.WriteAllText(context.TempAiFile, aiCode);
                File.SetAttributes(context.TempAiFile, FileAttributes.Normal);

                var diffService = Package.GetGlobalService(typeof(SVsDifferenceService)) as IVsDifferenceService;
                if (diffService == null)
                {
                    MessageBox.Show("Visual Studio Difference Service unavailable.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return FileUtil.CleanUpTempFiles(context);
                }

                context.DiffFrame = diffService.OpenComparisonWindow2(
                    leftFileMoniker: context.TempCurrentFile,
                    rightFileMoniker: context.TempAiFile,
                    caption: $"{dte.ActiveDocument.Name} compare",
                    Tooltip: $"Changes to {dte.ActiveDocument.Name}",
                    leftLabel: "Current Document",
                    rightLabel: "AI-Generated Code (Editable)",
                    inlineLabel: "",
                    roles: "",
                    grfDiffOptions: 0); // Remove LeftFileIsTemporary

                if (context.DiffFrame == null)
                {
                    MessageBox.Show("Failed to create diff window.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return FileUtil.CleanUpTempFiles(context);
                }

                // Get the right-hand text buffer for editable AI code
                if (context.DiffFrame.GetProperty((int)__VSFPROPID.VSFPROPID_DocData, out object docData) == VSConstants.S_OK)
                {
                    context.RightTextBuffer = docData as IVsTextLines;
                }

                ErrorHandler.ThrowOnFailure(context.DiffFrame.Show());
                return context;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening diff view: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return FileUtil.CleanUpTempFiles(context);
            }
        }

        public static void ApplyChanges(DTE2 dte, string aiCode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte == null || dte.ActiveDocument == null)
            {
                MessageBox.Show("No active document or DTE unavailable.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log("ApplyChanges: DTE or active document is null.");
                return;
            }

            if (aiCode == null)
            {
                MessageBox.Show("AI code is null.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log("ApplyChanges: AI code is null.");
                return;
            }

            var activeDoc = dte.ActiveDocument;
            var textDoc = activeDoc.Object("TextDocument") as TextDocument;
            if (textDoc == null)
            {
                MessageBox.Show($"Document '{activeDoc.Name}' is not editable.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log($"ApplyChanges: '{activeDoc.FullName}' is not a text document.");
                return;
            }

            if (activeDoc.ReadOnly)
            {
                MessageBox.Show($"Document '{activeDoc.Name}' is read-only.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                ChatWindowUtilities.Log($"ApplyChanges: '{activeDoc.FullName}' is read-only.");
                return;
            }

            try
            {
                var startPoint = textDoc.StartPoint.CreateEditPoint();
                var endPoint = textDoc.EndPoint.CreateEditPoint();
                startPoint.Delete(endPoint);
                startPoint.Insert(aiCode);
                ChatWindowUtilities.Log($"ApplyChanges: Successfully applied changes to '{activeDoc.FullName}'.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying changes to '{activeDoc.Name}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log($"ApplyChanges: Exception for '{activeDoc.FullName}': {ex}");
            }
        }

        public static void CloseDiffAndReset(DiffContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (context == null) return;

            if (context.DiffFrame != null)
            {
                try
                {
                    ErrorHandler.ThrowOnFailure(context.DiffFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error closing diff frame: {ex.Message}");
                }
                context.DiffFrame = null;
            }

            context.RightTextBuffer = null;
            FileUtil.CleanUpTempFiles(context);
            context.TempCurrentFile = null;
            context.TempAiFile = null;
        }

        public static string GetActiveDocumentText(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte?.ActiveDocument == null) return string.Empty;

            var textDoc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
            return textDoc?.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint) ?? string.Empty;
        }
    }
}