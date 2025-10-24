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
using Microsoft.VisualStudio.Shell;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Integrated_AI.Utilities
{
    public static class CodeAnalysisUtil
    {
        #region Code Analysis Regex and Keywords

        // A set of C# keywords that can precede a parenthesis, to avoid matching control flow blocks as functions.
        private static readonly HashSet<string> CSharpKeywords = new HashSet<string>(StringComparer.Ordinal)
        {
            "if", "for", "foreach", "while", "do", "switch", "case", "lock", "using", "fixed", "catch", "finally"
        };

        // A set of VB.NET keywords for the same purpose.
        private static readonly HashSet<string> VbKeywords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "If", "For", "ForEach", "While", "Do", "Select", "Case", "SyncLock", "Using", "Catch", "Finally", "With"
        };

        // This Regex is now only used for the "multiple functions" check in the full file detection logic.
        private static readonly Regex CSharpMethodRegex = new Regex(
            @"^.*\b[\w_][\w\d_]*\b\s*\(.*\)\s*\{",
            RegexOptions.Multiline | RegexOptions.Compiled);
        private static readonly Regex VbMethodRegex = new Regex(
            @"^\s*(?:(Public|Private|Protected|Friend|Shared|Async|Overrides|Overridable|MustOverride|Iterator)\s+)*(?:Function|Sub)\s+\b[\w_][\w\d_]*\b\s*\(.*\)",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.Compiled);

        #endregion

        public static (bool IsFunction, string FunctionName, bool IsFullFile) AnalyzeCodeBlock(DTE2 dte, Document activeDoc, string code)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            if (dte == null || activeDoc == null || string.IsNullOrEmpty(code))
            {
                WebViewUtilities.Log("AnalyzeCodeBlock: Invalid input (DTE, Document, or code is null/empty).");
                return (false, null, false);
            }

            try
            {
                string extension = Path.GetExtension(activeDoc.FullName)?.ToLowerInvariant();
                bool isCSharp = extension == ".cs";
                bool isVB = extension == ".vb";

                if (!isCSharp && !isVB)
                {
                    WebViewUtilities.Log($"AnalyzeCodeBlock: Unsupported file extension: {extension}");
                    return (false, null, false);
                }

                string trimmedCode = code.Trim();

                // --- 1. FUNCTION DETECTION (Procedural Logic) ---

                // Rule: A function block must start with a modifier, contain parentheses, and be properly enclosed.
                string codeWithoutLeadingComments = Regex.Replace(trimmedCode, @"^(\s*(\/\*[\s\S]*?\*\/|\/\/[^\r\n]*))+", "").TrimStart();

                var startKeywords = isCSharp
                    ? new HashSet<string> { "public", "private", "protected", "internal", "static", "async", "virtual", "override", "sealed", "unsafe" }
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Public", "Private", "Protected", "Friend", "Shared", "Async", "Overrides", "Overridable", "MustOverride", "Iterator", "Function", "Sub" };
                
                string firstWord = codeWithoutLeadingComments.Split(new[] { ' ', '\t', '\r', '\n', '<' }, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                
                bool startsWithModifier = firstWord != null && startKeywords.Contains(firstWord);
                bool hasParens = trimmedCode.Contains("(") && trimmedCode.Contains(")");
                
                bool isEnclosedAndBalanced = false;
                if (isCSharp)
                {
                    int openBraces = trimmedCode.Count(c => c == '{');
                    int closeBraces = trimmedCode.Count(c => c == '}');
                    isEnclosedAndBalanced = openBraces > 0 && openBraces == closeBraces && trimmedCode.EndsWith("}");
                }
                else if (isVB)
                {
                    isEnclosedAndBalanced = Regex.IsMatch(trimmedCode, @"\bEnd\s+(Function|Sub)\s*$", RegexOptions.IgnoreCase);
                }

                if (startsWithModifier && hasParens && isEnclosedAndBalanced)
                {
                    int parenIndex = trimmedCode.IndexOf('(');
                    if (parenIndex > 0)
                    {
                        string signatureBeforeParen = trimmedCode.Substring(0, parenIndex);
                        string[] parts = signatureBeforeParen.Split(new[] { ' ', '\t', '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries);
                        string functionName = parts.LastOrDefault();

                        if (!string.IsNullOrEmpty(functionName))
                        {
                            var keywordsToAvoid = isCSharp ? CSharpKeywords : VbKeywords;
                            if (!keywordsToAvoid.Contains(functionName))
                            {
                                WebViewUtilities.Log($"AnalyzeCodeBlock: Detected single method by explicit rules: {functionName}");
                                return (true, functionName, false);
                            }
                        }
                    }
                }

                // --- 2. FULL FILE DETECTION (If function detection failed) ---

                var methodRegex = isCSharp ? CSharpMethodRegex : VbMethodRegex;
                var matches = methodRegex.Matches(code);

                bool hasImportsOrUsing = isCSharp
                    ? new Regex(@"^\s*using\s+", RegexOptions.Multiline).IsMatch(trimmedCode)
                    : new Regex(@"^\s*Imports\s+", RegexOptions.IgnoreCase | RegexOptions.Multiline).IsMatch(trimmedCode);
                bool hasNamespace = new Regex(@"^\s*namespace\s+", RegexOptions.Multiline).IsMatch(trimmedCode);
                bool hasTopLevelType = isCSharp
                    ? new Regex(@"^\s*(public|internal|private)?\s*(abstract|sealed|static|partial)?\s*(class|struct|interface|enum|record)\s+", RegexOptions.Multiline).IsMatch(trimmedCode)
                    : new Regex(@"^\s*(Public|Friend|Private)?\s*(Shared|MustInherit|NotInheritable|Partial)?\s*(Class|Structure|Interface|Enum|Module)\s+", RegexOptions.IgnoreCase | RegexOptions.Multiline).IsMatch(trimmedCode);

                if (hasImportsOrUsing || hasNamespace || hasTopLevelType || matches.Count > 1)
                {
                    WebViewUtilities.Log("AnalyzeCodeBlock: Detected full file based on container keywords or multiple functions.");
                    return (false, null, true);
                }
                
                // --- 3. FALLBACK TO SNIPPET ---

                WebViewUtilities.Log("AnalyzeCodeBlock: Code does not match function or full file criteria. Treating as a snippet.");
                return (false, null, false);
            }
            catch (Exception ex)
            {
                WebViewUtilities.Log($"Exception in AnalyzeCodeBlock: {ex.Message}\nStackTrace: {ex.StackTrace}");
                return (false, null, false);
            }
        }
    }
}