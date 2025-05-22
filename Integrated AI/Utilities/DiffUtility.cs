using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using System;
using System.IO;
using System.Windows;

namespace Integrated_AI.Utilities
{
    public class DiffUtility
    {
        private readonly DTE2 _dte;
        private string _tempCurrentFile;
        private string _tempAiFile;
        private IVsWindowFrame _diffFrame;

        public DiffUtility(DTE2 dte)
        {
            _dte = dte ?? throw new ArgumentNullException(nameof(dte));
        }

        public void OpenDiffView(string currentCode, string aiCode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (string.IsNullOrEmpty(currentCode) || string.IsNullOrEmpty(aiCode))
                {
                    MessageBox.Show("No code to compare.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                _tempCurrentFile = Path.GetTempFileName();
                _tempAiFile = Path.GetTempFileName();
                File.WriteAllText(_tempCurrentFile, currentCode);
                File.WriteAllText(_tempAiFile, aiCode);

                IVsDifferenceService diffService = Package.GetGlobalService(typeof(SVsDifferenceService)) as IVsDifferenceService;
                if (diffService != null)
                {
                    _diffFrame = diffService.OpenComparisonWindow2(
                        leftFileMoniker: _tempCurrentFile,
                        rightFileMoniker: _tempAiFile,
                        caption: "AI Code vs Current Document",
                        Tooltip: "AI Code vs Current Document",
                        leftLabel: "Current Document",
                        rightLabel: "AI-Generated Code",
                        inlineLabel: "",
                        roles: "",
                        grfDiffOptions: (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_DoNotShow |
                                        (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_LeftFileIsTemporary |
                                        (uint)__VSDIFFSERVICEOPTIONS.VSDIFFOPT_RightFileIsTemporary);

                    ErrorHandler.ThrowOnFailure(_diffFrame.Show());
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error opening diff view: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                CloseDiffAndReset();
            }
        }

        public string GetAICode()
        {
            if (!string.IsNullOrEmpty(_tempAiFile) && File.Exists(_tempAiFile))
            {
                try
                {
                    return File.ReadAllText(_tempAiFile);
                }
                catch (Exception ex)
                {
                    LoggingUtility.Log($"Error reading AI code file: {ex.Message}");
                    MessageBox.Show($"Error reading AI code: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                    return string.Empty;
                }
            }
            LoggingUtility.Log("AI code file is not available.");
            return string.Empty;
        }

        public void ApplyChanges(string aiCode)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                if (_dte.ActiveDocument != null)
                {
                    TextDocument textDoc = _dte.ActiveDocument.Object("TextDocument") as TextDocument;
                    if (textDoc != null)
                    {
                        EditPoint editPoint = textDoc.StartPoint.CreateEditPoint();
                        editPoint.Delete(textDoc.EndPoint);
                        editPoint.Insert(aiCode);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error applying changes: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                CloseDiffAndReset();
            }
        }

        public void CloseDiffAndReset()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_diffFrame != null)
            {
                _diffFrame.CloseFrame(0);
                _diffFrame = null;
            }

            if (!string.IsNullOrEmpty(_tempCurrentFile) && File.Exists(_tempCurrentFile))
                File.Delete(_tempCurrentFile);
            if (!string.IsNullOrEmpty(_tempAiFile) && File.Exists(_tempAiFile))
                File.Delete(_tempAiFile);
        }

        public string GetActiveDocumentText()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            if (_dte.ActiveDocument != null)
            {
                TextDocument textDoc = _dte.ActiveDocument.Object("TextDocument") as TextDocument;
                if (textDoc != null)
                {
                    return textDoc.StartPoint.CreateEditPoint().GetText(textDoc.EndPoint);
                }
            }
            return string.Empty;
        }
    }
}