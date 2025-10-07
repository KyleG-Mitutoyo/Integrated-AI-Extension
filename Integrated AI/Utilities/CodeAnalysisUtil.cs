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

        /// <summary>
        /// A robust Regex for C# method detection.
        /// It uses a positive lookahead to find the method's structure (name, parameters, opening brace)
        /// without trying to parse the complex return types and modifiers, which was the point of failure.
        /// It then captures the name for validation.
        /// </summary>
        private static readonly Regex CSharpMethodRegex = new Regex(
            @"
            ^                                       # Start of a line (in Multiline mode)
            \s*                                     # Leading whitespace
            # Lookahead to find a pattern that looks like a method signature on the current line
            (?=
                .*                                  # Allow any modifiers, return type, etc.
                \b(?<MethodName>[\w_][\w\d_]*)\b    # Capture a valid identifier as the method name
                \s*
                (?:<.*?>)?                          # Optional generic parameters on the method itself, e.g., <T>
                \s*
                \(.*?\)                             # The parameter list (non-greedy)
                \s*
                (?:where\s.*?)?                     # Optional 'where' constraints for generics
                \s*
                \{                                  # Must be followed by an opening brace
            )
            ",
            RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

        /// <summary>
        /// A Regex for VB.NET Sub/Function detection.
        /// This is more straightforward than C# because we can anchor the search on the 'Function' or 'Sub' keyword.
        /// </summary>
        private static readonly Regex VbMethodRegex = new Regex(
            @"
            ^                                       # Start of a line (in Multiline mode)
            \s*
            (?:<[\w\s=""',.]*>\s*)*                  # Optional attributes like <TestMethod()>
            (?:(Public|Private|Protected|Friend|Shared|Async|Overrides|Overridable|MustOverride|Iterator)\s+)* # Modifiers
            (?:Function|Sub)\s+                     # The 'Function' or 'Sub' keyword is mandatory
            \b(?<MethodName>[\w_][\w\d_]*)\b        # Capture the method name
            \s*
            \(.*?\)                                 # The parameter list
            ",
            RegexOptions.IgnoreCase | RegexOptions.Multiline | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

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

                // --- Stricter Full File Detection ---
                string trimmedCode = code.Trim();

                // THE FIX: Use Regex anchored to the start (^) for all top-level keyword checks.
                // This prevents matching keywords that appear inside a method body.
                bool hasImportsOrUsing = isCSharp
                    ? new Regex(@"^\s*using\s+").IsMatch(trimmedCode)
                    : new Regex(@"^\s*Imports\s+", RegexOptions.IgnoreCase).IsMatch(trimmedCode);

                bool hasNamespace = new Regex(@"^\s*namespace\s+").IsMatch(trimmedCode);

                bool hasTopLevelType = false;
                if (isCSharp)
                {
                    hasTopLevelType = new Regex(@"^\s*(public|internal)?\s*(abstract|sealed|static)?\s*(class|struct|interface|enum)\s+").IsMatch(trimmedCode);
                }
                else if (isVB)
                {
                    hasTopLevelType = new Regex(@"^\s*(Public|Friend)?\s*(Shared|MustInherit|NotInheritable)?\s*(Class|Structure|Interface|Enum|Module)\s+", RegexOptions.IgnoreCase).IsMatch(trimmedCode);
                }

                // A full file must have a balanced structure.
                int openBraces = code.Count(c => c == '{');
                int closeBraces = code.Count(c => c == '}');
                // A valid C# file/class block must contain at least one brace pair and be balanced.
                bool bracesBalanced = openBraces > 0 && openBraces == closeBraces;

                bool vbStructureBalanced = true;
                if (isVB)
                {
                    int startCount = Regex.Matches(code, @"\b(Class|Module|Structure|Enum|Interface)\b", RegexOptions.IgnoreCase).Count;
                    int endCount = Regex.Matches(code, @"\bEnd\s+(Class|Module|Structure|Enum|Interface)\b", RegexOptions.IgnoreCase).Count;
                    // A valid VB structure must have at least one container and they must be balanced.
                    vbStructureBalanced = startCount > 0 && startCount == endCount;
                }

                if (((isCSharp && bracesBalanced) || (isVB && vbStructureBalanced)) && (hasImportsOrUsing || hasNamespace || hasTopLevelType))
                {
                    WebViewUtilities.Log("AnalyzeCodeBlock: Detected full file based on structure and keywords.");
                    return (false, null, true);
                }


                // --- Method Detection ---
                // If it's not a full file, check if it's a function.
                Regex regex = isCSharp ? CSharpMethodRegex : VbMethodRegex;
                var matches = regex.Matches(code);

                if (matches.Count > 0)
                {
                    var match = matches[0];
                    string methodName = match.Groups["MethodName"].Value;
                    bool isKeyword = isCSharp ? CSharpKeywords.Contains(methodName) : VbKeywords.Contains(methodName);

                    if (!string.IsNullOrEmpty(methodName) && !isKeyword)
                    {
                        if (isCSharp)
                        {
                            // A single function should also have balanced braces.
                            if (bracesBalanced)
                            {
                                WebViewUtilities.Log($"AnalyzeCodeBlock: Detected C# method: {methodName}");
                                return (true, methodName, false);
                            }
                        }
                        else if (isVB)
                        {
                            if (Regex.IsMatch(code, @"\bEnd\s+(Function|Sub)\b", RegexOptions.IgnoreCase))
                            {
                                WebViewUtilities.Log($"AnalyzeCodeBlock: Detected VB method: {methodName}");
                                return (true, methodName, false);
                            }
                        }
                    }
                }

                WebViewUtilities.Log("AnalyzeCodeBlock: Code is not a full file or a recognized method. Treating as a snippet.");
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
