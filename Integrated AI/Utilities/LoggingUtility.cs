using System.Diagnostics;

namespace Integrated_AI.Utilities
{
    public static class LoggingUtility
    {
        public static void Log(string message)
        {
            Debug.WriteLine($"LOG: {message}");
        }
    }
}