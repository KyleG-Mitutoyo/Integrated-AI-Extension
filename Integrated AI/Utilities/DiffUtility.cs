// Integrated AI/Utilities/DiffUtility.cs
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.TextManager.Interop;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Windows;
using MessageBox = HandyControl.Controls.MessageBox;

namespace Integrated_AI.Utilities
{
    public static class DiffUtility
    {
        public class DiffContext
        {
            public string TempCurrentFile { get; set; }
            public string TempAiFile { get; set; }
            public string AICodeBlock { get; set; }
            public IVsWindowFrame DiffFrame { get; set; }
            public string ActiveDocumentPath { get; set; }
            public int NewCodeStartIndex { get; set; } = -1;
            public bool IsNewFile { get; set; } = false;
        }

        public static DiffContext OpenDiffView(Document activeDoc, string currentCode, string aiCodeFullFileContents, string aiCode, DiffContext existingContext = null, bool compareMode = false)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (activeDoc == null || currentCode == null || aiCodeFullFileContents == null)
            {
                MessageBox.Show("Invalid input: DTE active document or code strings are null.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            string extension = Path.GetExtension(activeDoc.FullName) ?? ".txt";
            var context = new DiffContext { };

            // Use the existing context if provided, otherwise create a new one
            if (existingContext == null)
            {
                context = new DiffContext();
            }
            else
            {
                context = existingContext;

                if (context.IsNewFile)
                {
                    WebViewUtilities.Log("OpenDiffView: Not showing diff view for new file");
                    return null;
                }
            }

            context.TempCurrentFile = Path.Combine(Path.GetTempPath(), $"Current_{Guid.NewGuid()}{extension}");
            context.TempAiFile = Path.Combine(Path.GetTempPath(), $"AI_{Guid.NewGuid()}{extension}");
            context.AICodeBlock = aiCode;
            context.ActiveDocumentPath = activeDoc.FullName;

            try
            {
                File.WriteAllText(context.TempCurrentFile, currentCode);
                File.WriteAllText(context.TempAiFile, aiCodeFullFileContents);
                WebViewUtilities.Log($"OpenDiffView: Created temp files - Current: {context.TempCurrentFile} (length: {currentCode.Length}), AI: {context.TempAiFile} (length: {aiCodeFullFileContents.Length}), ActiveDoc: {activeDoc.FullName}");

                var diffService = Package.GetGlobalService(typeof(SVsDifferenceService)) as IVsDifferenceService;
                if (diffService == null)
                {
                    MessageBox.Show("Visual Studio Difference Service unavailable.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    WebViewUtilities.Log("OpenDiffView: Diff service unavailable.");
                    return FileUtil.CleanUpTempFiles(context);
                }

                uint grfDiffOptions = (uint)(__VSDIFFSERVICEOPTIONS.VSDIFFOPT_LeftFileIsTemporary |
                                            __VSDIFFSERVICEOPTIONS.VSDIFFOPT_RightFileIsTemporary |
                                            __VSDIFFSERVICEOPTIONS.VSDIFFOPT_DoNotShow);

                string leftLabel = "Current Document";
                string rightLabel = "Document with AI Edits";

                // If in compare mode, adjust labels accordingly
                if (compareMode)
                {
                    leftLabel = "Old Document from Restore Point";
                    rightLabel = "Current Document";
                }

                WebViewUtilities.Log($"OpenDiffView: Calling OpenComparisonWindow2 for {activeDoc.Name}, grfDiffOptions: {grfDiffOptions}");
                context.DiffFrame = diffService.OpenComparisonWindow2(
                    leftFileMoniker: context.TempCurrentFile,
                    rightFileMoniker: context.TempAiFile,
                    caption: $"{activeDoc.Name} compare",
                    Tooltip: $"Changes to {activeDoc.Name}",
                    leftLabel: leftLabel,
                    rightLabel: rightLabel,
                    inlineLabel: "",
                    roles: "",
                    grfDiffOptions: grfDiffOptions);

                if (context.DiffFrame == null)
                {
                    MessageBox.Show("Failed to create diff window.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    WebViewUtilities.Log("OpenDiffView: Failed to create diff window.");
                    return FileUtil.CleanUpTempFiles(context);
                }

                WebViewUtilities.Log($"OpenDiffView: Diff frame created, showing window for {activeDoc.Name}.");
                ErrorHandler.ThrowOnFailure(context.DiffFrame.Show());
                WebViewUtilities.Log($"OpenDiffView: Diff window shown successfully for {activeDoc.Name}.");
                return context;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening diff view: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                WebViewUtilities.Log($"OpenDiffView: Exception - {ex.Message}, StackTrace: {ex.StackTrace}");
                return FileUtil.CleanUpTempFiles(context);
            }
        }

        // Opens diff views for multiple files in a restore compared to current project files
        public static List<DiffContext> OpenMultiFileDiffView(DTE2 dte, Dictionary<string, string> restoreFiles)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte == null || restoreFiles == null || restoreFiles.Count == 0)
            {
                MessageBox.Show("Invalid input: DTE or restore files are null or empty.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            var diffContexts = new List<DiffContext>();
    
            // Log restoreFiles content for debugging
            WebViewUtilities.Log($"OpenMultiFileDiffView: Processing {restoreFiles.Count} files.");

            // Get solution directory for path normalization (if needed)
            string solutionDir = System.IO.Path.GetDirectoryName(dte.Solution.FullName);
            if (string.IsNullOrEmpty(solutionDir))
            {
                WebViewUtilities.Log("OpenMultiFileDiffView: Could not determine solution directory.");
                MessageBox.Show("Unable to determine solution directory.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return null;
            }

            foreach (var restoreFile in restoreFiles)
            {
                string filePath = restoreFile.Key;
                string restoreContent = restoreFile.Value;

                // Use solution-relative path
                string normalizedPath = filePath;

                // Check if file exists in the solution
                ProjectItem projectItem = null;
                try
                {
                    projectItem = dte.Solution.FindProjectItem(normalizedPath);
                    if (projectItem == null)
                    {
                        // Try absolute path as fallback
                        normalizedPath = System.IO.Path.Combine(solutionDir, filePath);
                        normalizedPath = System.IO.Path.GetFullPath(normalizedPath);
                        projectItem = dte.Solution.FindProjectItem(normalizedPath);
                    }
                }
                catch (Exception ex)
                {
                    WebViewUtilities.Log($"OpenMultiFileDiffView: Error finding project item for {normalizedPath}: {ex.Message}");
                    continue;
                }

                if (projectItem == null)
                {
                    WebViewUtilities.Log($"OpenMultiFileDiffView: File {normalizedPath} not found in solution.");
                    continue;
                }

                // Open the document to get its current content
                Document doc = null;
                try
                {
                    projectItem.Open(EnvDTE.Constants.vsViewKindCode);
                    doc = projectItem.Document;
                    if (doc == null)
                    {
                        WebViewUtilities.Log($"OpenMultiFileDiffView: Could not open document for {normalizedPath}.");
                        continue;
                    }
                }
                catch (Exception ex)
                {
                    WebViewUtilities.Log($"OpenMultiFileDiffView: Exception opening document for {normalizedPath}: {ex.Message}");
                    continue;
                }

                // Get current content
                string currentContent = null;
                try
                {
                    var textDocument = (TextDocument)doc.Object("TextDocument");
                    currentContent = textDocument.StartPoint.CreateEditPoint().GetText(textDocument.EndPoint);
                }
                catch (Exception ex)
                {
                    WebViewUtilities.Log($"OpenMultiFileDiffView: Error reading content for {normalizedPath}: {ex.Message}");
                    continue;
                }

                // Compare only if content has changed
                if (currentContent == restoreContent)
                {
                    WebViewUtilities.Log($"OpenMultiFileDiffView: No changes detected for {normalizedPath}. Skipping diff.");
                    continue;
                }

                // Open diff view for this file
                //MessageBox.Show($"Opening diff view for {normalizedPath}.\n\nCurrent content length: {currentContent.Length}\nRestore content length: {restoreContent.Length}", "Diff View", MessageBoxButton.OK, MessageBoxImage.Information);
                var context = OpenDiffView(doc, restoreContent, currentContent, restoreContent, null, true);
                if (context != null)
                {
                    diffContexts.Add(context);
                    WebViewUtilities.Log($"OpenMultiFileDiffView: Diff view opened for {normalizedPath}.");
                }
                else
                {
                    WebViewUtilities.Log($"OpenMultiFileDiffView: Failed to open diff view for {normalizedPath}.");
                }
            }

            if (diffContexts.Count == 0)
            {
                MessageBox.Show("No differences found or unable to open diff views for the selected restore.", "Information", MessageBoxButton.OK, MessageBoxImage.Information);
                return null;
            }

            return diffContexts;
        }

        
        public static void ApplyChanges(DTE2 dte, Document activeDoc, string aiCode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte == null || activeDoc == null)
            {
                MessageBox.Show("No active document or DTE unavailable.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                WebViewUtilities.Log("ApplyChanges: DTE or active document is null.");
                return;
            }

            if (aiCode == null)
            {
                MessageBox.Show("AI code is null.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                WebViewUtilities.Log("ApplyChanges: AI code is null.");
                return;
            }

            var textDoc = activeDoc.Object("TextDocument") as TextDocument;
            if (textDoc == null)
            {
                MessageBox.Show($"Document '{activeDoc.Name}' is not editable.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                WebViewUtilities.Log($"ApplyChanges: '{activeDoc.FullName}' is not a text document.");
                return;
            }

            if (activeDoc.ReadOnly)
            {
                MessageBox.Show($"Document '{activeDoc.Name}' is read-only.", "Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                WebViewUtilities.Log($"ApplyChanges: '{activeDoc.FullName}' is read-only.");
                return;
            }

            try
            {
                var startPoint = textDoc.StartPoint.CreateEditPoint();
                var endPoint = textDoc.EndPoint.CreateEditPoint();
                startPoint.Delete(endPoint);
                startPoint.Insert(aiCode);
                WebViewUtilities.Log($"ApplyChanges: Successfully applied changes to '{activeDoc.FullName}'.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying changes to '{activeDoc.Name}': {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                WebViewUtilities.Log($"ApplyChanges: Exception for '{activeDoc.FullName}': {ex}");
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
                    WebViewUtilities.Log("CloseDiffAndReset: Diff frame closed successfully.");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error closing diff frame: {ex.Message}");
                    WebViewUtilities.Log($"CloseDiffAndReset: Exception closing diff frame - {ex.Message}");
                }
                context.DiffFrame = null;
            }

            FileUtil.CleanUpTempFiles(context);
            context.TempCurrentFile = null;
            context.TempAiFile = null;
        }

        public static string GetDocumentText(Document activeDoc)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (activeDoc == null) return string.Empty;

            var textDoc = activeDoc.Object("TextDocument") as TextDocument;
            return textDoc?.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint) ?? string.Empty;
        }
    }
}