// In Integrated AI/Utilities/DiffUtility.cs
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.IO;
using System.Windows;
using System.Diagnostics; // For Debug.WriteLine

namespace Integrated_AI.Utilities
{
    public static class DiffUtility
    {
        public class DiffContext
        {
            public string TempCurrentFile { get; set; }
            public string TempAiFile { get; set; }
            public IVsWindowFrame DiffFrame { get; set; }
        }

        public static DiffContext OpenDiffView(DTE2 dte, string currentCode, string aiCode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            DiffContext context = new DiffContext();
            bool success = false;

            try
            {
                if (dte == null)
                {
                    MessageBox.Show("DTE2 instance is not available. Cannot open diff view.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                // Original code checked for null/empty. If comparing with empty is valid, adjust this.
                // For robustness, handle potential nulls passed for code strings.
                if (currentCode == null || aiCode == null)
                {
                    MessageBox.Show("Code to compare cannot be null. One or both inputs are null.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null;
                }

                context.TempCurrentFile = Path.GetTempFileName();
                context.TempAiFile = Path.GetTempFileName();
                File.WriteAllText(context.TempCurrentFile, currentCode);
                File.WriteAllText(context.TempAiFile, aiCode);

                IVsDifferenceService diffService = Package.GetGlobalService(typeof(SVsDifferenceService)) as IVsDifferenceService;
                if (diffService == null)
                {
                    MessageBox.Show("Visual Studio Difference Service is not available.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null; // Triggers finally block for cleanup
                }

                context.DiffFrame = diffService.OpenComparisonWindow2(
                    leftFileMoniker: context.TempCurrentFile,
                    rightFileMoniker: context.TempAiFile,
                    caption: "AI Code vs Current Document",
                    Tooltip: "AI Code vs Current Document",
                    leftLabel: "Current Document",
                    rightLabel: "AI-Generated Code",
                    inlineLabel: "",
                    roles: "",
                    grfDiffOptions: (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_DoNotShow | // Prepares frame but doesn't show it.
                                    (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_LeftFileIsTemporary |
                                    (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_RightFileIsTemporary);

                if (context.DiffFrame == null)
                {
                    MessageBox.Show("Failed to create the diff window frame.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return null; // Triggers finally block for cleanup
                }

                // Explicitly show the frame since VSDIFFOPT_DoNotShow was used.
                ErrorHandler.ThrowOnFailure(context.DiffFrame.Show());

                success = true;
                return context;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening diff view: {ex.Message}", "Error Opening Diff", MessageBoxButton.OK, MessageBoxImage.Error);
                return null; // Triggers finally block for cleanup
            }
            finally
            {
                if (!success && context != null)
                {
                    // Clean up temp files if operation failed before returning context
                    if (!string.IsNullOrEmpty(context.TempCurrentFile) && File.Exists(context.TempCurrentFile))
                    {
                        try { File.Delete(context.TempCurrentFile); }
                        catch (Exception ex) { Debug.WriteLine($"Failed to delete temp file {context.TempCurrentFile}: {ex.Message}"); }
                    }
                    if (!string.IsNullOrEmpty(context.TempAiFile) && File.Exists(context.TempAiFile))
                    {
                        try { File.Delete(context.TempAiFile); }
                        catch (Exception ex) { Debug.WriteLine($"Failed to delete temp file {context.TempAiFile}: {ex.Message}"); }
                    }
                }
            }
        }

        public static string GetAICode(string tempAiFile)
        {
            if (string.IsNullOrEmpty(tempAiFile))
            {
                ChatWindowUtilities.Log("AI code temporary file path is null or empty.");
                return string.Empty;
            }
            if (!File.Exists(tempAiFile))
            {
                ChatWindowUtilities.Log($"AI code temporary file does not exist: {tempAiFile}");
                // Consider showing a MessageBox here if this is an unexpected state for the user
                // MessageBox.Show($"Temporary file for AI code not found: {tempAiFile}", "File Not Found", MessageBoxButton.OK, MessageBoxImage.Error);
                return string.Empty;
            }

            try
            {
                return File.ReadAllText(tempAiFile);
            }
            catch (Exception ex)
            {
                ChatWindowUtilities.Log($"Error reading AI code file '{tempAiFile}': {ex.Message}");
                MessageBox.Show($"Error reading AI code from temp file: {ex.Message}", "File Read Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return string.Empty;
            }
        }

        public static void ApplyChanges(DTE2 dte, string aiCode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dte == null)
            {
                MessageBox.Show("DTE2 instance is not available. Cannot apply changes.", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log("ApplyChanges: DTE2 instance is null.");
                return;
            }

            // It's okay to apply an empty string (clears the document)
            // but aiCode itself should not be null if we proceed.
            if (aiCode == null)
            {
                MessageBox.Show("AI code is null. Cannot apply changes.", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log("ApplyChanges: aiCode is null.");
                return;
            }

            EnvDTE.Document activeDoc = null;
            try
            {
                activeDoc = dte.ActiveDocument;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error accessing active document: {ex.Message}", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log($"ApplyChanges: Exception accessing dte.ActiveDocument: {ex.ToString()}");
                return;
            }

            if (activeDoc == null)
            {
                MessageBox.Show("No active document to apply changes to.", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log("ApplyChanges: dte.ActiveDocument is null.");
                return;
            }

            TextDocument textDoc = null;
            try
            {
                textDoc = activeDoc.Object("TextDocument") as TextDocument;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not get TextDocument from active document '{activeDoc.Name}': {ex.Message}. Is it a text-editable file?", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log($"ApplyChanges: Exception getting TextDocument from activeDoc.Object for '{activeDoc.FullName}': {ex.ToString()}");
                return;
            }

            if (textDoc == null)
            {
                MessageBox.Show($"Active document '{activeDoc.Name}' is not a text document or cannot be edited.", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log($"ApplyChanges: Active document '{activeDoc.FullName}' did not yield a TextDocument object.");
                return;
            }

            try
            {
                if (activeDoc.ReadOnly)
                {
                    MessageBox.Show($"The document '{activeDoc.Name}' is read-only. Please make it writable to apply changes.", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    ChatWindowUtilities.Log($"ApplyChanges: Document '{activeDoc.FullName}' is read-only.");
                    return;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error checking if document '{activeDoc.Name}' is read-only: {ex.Message}", "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log($"ApplyChanges: Exception checking ReadOnly status for '{activeDoc.FullName}': {ex.ToString()}");
                return;
            }

            UndoContext undoContext = null;
            try
            {
                // Check if an undo context is already open by this extension, or generally.
                // DTE can be sensitive to nested or improperly managed undo contexts.
                if (dte.UndoContext.IsOpen)
                {
                    ChatWindowUtilities.Log($"ApplyChanges: DTE UndoContext was already open before attempting to apply AI changes. Name: '{dte.UndoContext}'. Proceeding to open a new one specific to this operation.");
                    // Depending on how your extension manages undo, you might need to close an existing one
                    // or ensure this nesting is safe. For now, we'll proceed.
                }

                //undoContext = dte.UndoContext.Open($"Apply AI suggestions to {activeDoc.Name}", false);

                EditPoint startPoint = textDoc.StartPoint.CreateEditPoint();
                EditPoint endPoint = textDoc.EndPoint.CreateEditPoint();

                startPoint.Delete(endPoint);
                startPoint.Insert(aiCode);

                undoContext = null;
                ChatWindowUtilities.Log($"ApplyChanges: Successfully applied changes to '{activeDoc.FullName}'.");
            }
            catch (Exception ex)
            {
                string errorMessage = $"Error applying changes to '{activeDoc.Name}': {ex.Message}";
                if (ex.HResult != 0)
                {
                    errorMessage += $"\n(HRESULT: 0x{ex.HResult:X8})";
                }
                MessageBox.Show(errorMessage, "Apply Error", MessageBoxButton.OK, MessageBoxImage.Error);
                ChatWindowUtilities.Log($"ApplyChanges: Exception during text modification for '{activeDoc.FullName}': {ex.ToString()}");

                if (undoContext != null && undoContext.IsOpen)
                {
                    try
                    {
                        undoContext.SetAborted();
                        ChatWindowUtilities.Log($"ApplyChanges: Aborted undo context for '{activeDoc.FullName}' due to error.");
                    }
                    catch (Exception abortEx)
                    {
                        ChatWindowUtilities.Log($"ApplyChanges: Exception while aborting undo context for '{activeDoc.FullName}': {abortEx.ToString()}");
                    }
                }
            }
            finally
            {
                if (undoContext != null && undoContext.IsOpen)
                {
                    try
                    {
                        undoContext.SetAborted();
                        ChatWindowUtilities.Log($"ApplyChanges (finally): Aborted lingering undo context for '{activeDoc.FullName}'.");
                    }
                    catch (Exception finalAbortEx)
                    {
                        ChatWindowUtilities.Log($"ApplyChanges (finally): Exception while aborting lingering undo context for '{activeDoc.FullName}': {finalAbortEx.ToString()}");
                    }
                }
            }
        }

        public static void CloseDiffAndReset(DiffContext context)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (context == null)
            {
                Debug.WriteLine("CloseDiffAndReset called with null context.");
                return;
            }

            if (context.DiffFrame != null)
            {
                try
                {
                    // Use __FRAMECLOSE.FC_NOSAVE to close without saving changes to the temporary diff files.
                    // FC_NOSAVE is 1. Your original code used 0 (FC_SAVEIFDIRTY).
                    // FC_NOSAVE is generally safer for temporary files that will be deleted.
                    ErrorHandler.ThrowOnFailure(context.DiffFrame.CloseFrame((uint)__FRAMECLOSE.FRAMECLOSE_NoSave));
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Error closing diff frame: {ex.Message}");
                    // Optionally, inform the user if closing fails, e.g., via MessageBox
                    // MessageBox.Show($"Failed to close the diff window: {ex.Message}", "Error Closing Diff", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                finally
                {
                    context.DiffFrame = null; // Prevent reuse even if CloseFrame failed
                }
            }

            if (!string.IsNullOrEmpty(context.TempCurrentFile) && File.Exists(context.TempCurrentFile))
            {
                try { File.Delete(context.TempCurrentFile); context.TempCurrentFile = null; }
                catch (IOException ex) { Debug.WriteLine($"Error deleting temp file {context.TempCurrentFile}: {ex.Message}"); }
            }
            if (!string.IsNullOrEmpty(context.TempAiFile) && File.Exists(context.TempAiFile))
            {
                try { File.Delete(context.TempAiFile); context.TempAiFile = null; }
                catch (IOException ex) { Debug.WriteLine($"Error deleting temp file {context.TempAiFile}: {ex.Message}"); }
            }
        }

        // May want to place this elsewhere
        public static string GetActiveDocumentText(DTE2 dte)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (dte == null) return string.Empty;

            if (dte.ActiveDocument != null)
            {
                TextDocument textDoc = dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (textDoc != null)
                {
                    return textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
                }
            }
            return string.Empty;
        }
    }
}