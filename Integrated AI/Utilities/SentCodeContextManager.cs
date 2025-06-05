using EnvDTE;
using EnvDTE80;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Integrated_AI.Utilities
{
    public static class SentCodeContextManager
    {
        public class SentCodeContext
        {
            public string Code { get; set; }
            public string Type { get; set; } // "snippet", "function", or "file"
            public string FilePath { get; set; }
            public int StartLine { get; set; }
            public int EndLine { get; set; }
            public int AgeCounter { get; set; }
        }


        private static List<SentCodeContext> _sentContexts = new List<SentCodeContext>();
        private const int MaxAgeCounter = 5;

        public static void AddSentContext(SentCodeContext context)
        {
            context.AgeCounter = 0; // Initialize counter
            _sentContexts.Add(context);
        }

        public static List<SentCodeContext> GetContextsForFile(string filePath)
        {
            return _sentContexts.Where(c => c.FilePath == filePath).ToList();
        }

        public static void UpdateContextData(SentCodeContext context, int newStartLine, int newEndLine, string code)
        {
            context.StartLine = newStartLine;
            context.EndLine = newEndLine;
            context.Code = code;
            context.AgeCounter = 0; // Reset counter when context is used
        }

        //Incrememnts the age counter for all contexts in the specified file, except the context that was used
        public static void IncrementContextAges(string filePath, SentCodeContext usedContext)
        {
            foreach (var context in _sentContexts.Where(c => c.FilePath == filePath && c != usedContext))
            {
                context.AgeCounter++;
            }
            // Remove contexts that are too old
            _sentContexts.RemoveAll(c => c.FilePath == filePath && c.AgeCounter >= MaxAgeCounter);
        }

        //You know what this does
        public static void ClearContextsForFile(string filePath)
        {
            _sentContexts.RemoveAll(c => c.FilePath == filePath);
        }

        //Includes contexts from all files
        public static List<SentCodeContext> GetAllSentContexts()
        {
            return _sentContexts.ToList(); // Return a copy to avoid modifying the original
        }

        //Returns the best matching context based on similarity to the provided code block
        public static SentCodeContext FindMatchingContext(List<SentCodeContext> contexts, string codeBlock)
        {
            if (contexts == null || contexts.Count == 0 || string.IsNullOrWhiteSpace(codeBlock))
            {
                return null; // No contexts or code block to match against
            }

            // Normalize the input code block
            string normalizedCodeBlock = StringUtilities.NormalizeCode(codeBlock);

            SentCodeContext bestMatch = null;
            double highestSimilarity = 0.0;
            //Unused for now
            const double MINIMUM_SIMILARITY = 0.5; // Adjust based on testing

            foreach (var context in contexts.OrderByDescending(c => c.StartLine))
            {
                // Normalize context code
                string normalizedContextCode = StringUtilities.NormalizeCode(context.Code);

                // Check for exact match
                if (normalizedContextCode == normalizedCodeBlock)
                {
                    return context; // Exact match is best, return immediately
                }

                // Calculate similarity
                double similarity = StringUtilities.CalculateSimilarity(normalizedContextCode, normalizedCodeBlock);
                if (similarity > highestSimilarity)
                {
                    highestSimilarity = similarity;
                    bestMatch = context;
                }

                // Fallback: Check if context is a file and code block is a subset
                if (context.Type == "file" && normalizedContextCode.Contains(normalizedCodeBlock))
                {
                    if (similarity > highestSimilarity) // Only update if better
                    {
                        highestSimilarity = similarity;
                        bestMatch = context;
                    }
                }
            }

            return bestMatch;
        }

        // Integrated AI/Utilities/ChatWindowUtilities.cs
        public static string ReplaceCodeInDocument(string currentCode, SentCodeContext context, string aiCode, DTE2 dte)
        {
            var lines = currentCode.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None).ToList();

            if (context != null)
            {
                if (context.Type == "file")
                {
                    return aiCode; // Replace entire document for "file" type
                }

                if (context.StartLine > 0 && context.EndLine >= context.StartLine && context.EndLine <= lines.Count)
                {
                    // Replace code within the context's line range
                    lines.RemoveRange(context.StartLine - 1, context.EndLine - context.StartLine + 1);
                    lines.Insert(context.StartLine - 1, aiCode);
                    return string.Join("\n", lines);
                }
            }

            // Fallback: Insert aiCode at cursor position
            if (dte?.ActiveDocument != null)
            {
                var selection = (TextSelection)dte.ActiveDocument.Selection;
                int cursorLine = selection.ActivePoint.Line;

                if (cursorLine > 0 && cursorLine <= lines.Count + 1)
                {
                    lines.Insert(cursorLine - 1, aiCode); // Insert at cursor line
                }
                else
                {
                    lines.Add(aiCode); // Append to end if cursor position is invalid
                }
            }
            else
            {
                // If no DTE or active document, append to end as last resort
                lines.Add(aiCode);
            }

            return string.Join("\n", lines);
        }

        public static string FormatContextList(List<SentCodeContext> contexts)
        {
            if (!contexts.Any())
            {
                return "No SentCodeContext entries available.";
            }

            var builder = new System.Text.StringBuilder();
            builder.AppendLine("SentCodeContext List:");
            builder.AppendLine(new string('-', 50));

            for (int i = 0; i < contexts.Count; i++)
            {
                var context = contexts[i];
                builder.AppendLine($"Context {i + 1}:");
                builder.AppendLine($"  FilePath: {context.FilePath}");
                builder.AppendLine($"  Type: {context.Type}");
                builder.AppendLine($"  StartLine: {context.StartLine}");
                builder.AppendLine($"  EndLine: {context.EndLine}");
                builder.AppendLine($"  AgeCounter: {context.AgeCounter}");
                builder.AppendLine($"  Code:");
                builder.AppendLine($"  {new string('-', 30)}");
                builder.AppendLine(context.Code);
                builder.AppendLine(new string('-', 50));
            }

            return builder.ToString();
        }
    }
}
