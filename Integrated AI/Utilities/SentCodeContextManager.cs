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
            public string FunctionFullName { get; set; }
            public int AgeCounter { get; set; }
        }

        private static List<SentCodeContext> _sentContexts = new List<SentCodeContext>();
        private const int MaxAgeCounter = 5;

        //Adds new contexts if not a duplicate
        public static bool AddSentContext(SentCodeContext context)
        {
            // Check for duplicate context based on FilePath, Type, and normalized Code
            string normalizedNewCode = StringUtil.NormalizeCode(context.Code);
            var existingContext = _sentContexts.FirstOrDefault(c =>
                c.FilePath == context.FilePath &&
                c.Type == context.Type &&
                StringUtil.NormalizeCode(c.Code) == normalizedNewCode);

            if (existingContext != null)
            {
                // Duplicate found, reset its AgeCounter instead of adding new context
                //Not sure if I want to reset the AgeCounter here, so commenting out for now
                //existingContext.AgeCounter = 0;
                return false; // Indicate that no new context was added
            }

            context.AgeCounter = 0; // Initialize counter
            _sentContexts.Add(context);
            return true; // Indicate that a new context was added
        }

        public static List<SentCodeContext> GetContextsForFile(string filePath)
        {
            return _sentContexts.Where(c => c.FilePath == filePath).ToList();
        }

public static void UpdateContextData(SentCodeContext context, string code)
{
    // Update function name if the type is a function
    if (context.Type == "function" && !string.IsNullOrEmpty(context.FunctionFullName))
    {
        //FullName includes namespace and class, so we only need to change the function name part at the end
        // Extract the basic function name from the AI response
        string newFunctionName = StringUtil.ExtractFunctionName(code);
        if (!string.IsNullOrEmpty(newFunctionName))
        {
            // Split the existing FunctionFullName to preserve namespace and class
            string[] parts = context.FunctionFullName.Split('.');
            if (parts.Length > 1)
            {
                // Replace the last part (function name) with the new name, keeping namespace/class
                parts[parts.Length - 1] = newFunctionName;
                context.FunctionFullName = string.Join(".", parts);
            }
            else
            {
                // Fallback: if no namespace/class, just use the new name
                context.FunctionFullName = newFunctionName;
            }
        }
    }

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

        // Can optionally clear contexts for a specific file, or all contexts if no file is specified
        public static void ClearContexts(string filePath = null)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                _sentContexts.Clear(); // Clear all contexts if no filePath is provided
            }
            else
            {
                _sentContexts.RemoveAll(c => c.FilePath == filePath); // Clear contexts for the specified file
            }
        }

        // Includes contexts from all files
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
            string normalizedCodeBlock = StringUtil.NormalizeCode(codeBlock);

            SentCodeContext bestMatch = null;
            double highestSimilarity = 0.0;
            //Unused for now
            const double MINIMUM_SIMILARITY = 0.5; // Adjust based on testing

            foreach (var context in contexts)
            {
                // Normalize context code
                string normalizedContextCode = StringUtil.NormalizeCode(context.Code);

                // Check for exact match
                if (normalizedContextCode == normalizedCodeBlock)
                {
                    return context; // Exact match is best, return immediately
                }

                // Calculate similarity
                double similarity = StringUtil.CalculateSimilarity(normalizedContextCode, normalizedCodeBlock);
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
        public static string ReplaceCodeInDocument(DTE2 dte, string currentCode, SentCodeContext context, string aiCode, Document activeDoc)
        {
            if (context == null)
            {
                return StringUtil.InsertAtCursorOrAppend(currentCode, aiCode, activeDoc);
            }

            if (context.Type == "file")
            {
                return aiCode; // Replace entire document for "file" type
            }

            if (context.Type == "function")
            {
                // For C# functions, find the function by name
                var functions = FunctionSelectionUtilities.GetFunctionsFromActiveDocument(dte);
                var targetFunction = functions.FirstOrDefault(f => f.FullName == context.FunctionFullName);

                if (targetFunction != null)
                {
                    // Find the function's current code in the document
                    int startIndex = currentCode.IndexOf(targetFunction.FullCode, StringComparison.Ordinal);
                    if (startIndex >= 0)
                    {
                        return StringUtil.ReplaceCodeBlock(currentCode, startIndex, targetFunction.FullCode.Length, aiCode);
                    }
                }
            }

            // For "Selection" or unmatched functions, check for highlighted text
            var selection = activeDoc.Selection as TextSelection;
            if (selection != null)
            {
                if (!selection.IsEmpty) // Check if text is highlighted
                {
                    // Get the start and end points of the selection
                    int startIndex = selection.TopPoint.AbsoluteCharOffset;
                    int length = selection.BottomPoint.AbsoluteCharOffset - startIndex;
                    return StringUtil.ReplaceCodeBlock(currentCode, startIndex, length, aiCode);
                }
            }

            // Fallback: Insert at cursor position or append if selection was empty or other errors
            return StringUtil.InsertAtCursorOrAppend(currentCode, aiCode, activeDoc);
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
                builder.AppendLine($"  FunctionFullName: {context.FunctionFullName ?? "N/A"}");
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
