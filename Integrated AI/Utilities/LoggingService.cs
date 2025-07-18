using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Integrated_AI.Utilities
{
    /// <summary>
    /// A simple static service for in-memory logging.
    /// </summary>
    public static class LoggingService
    {
        private static readonly int MaxLogEntries = 200;
        private static readonly Queue<string> LogMessages = new Queue<string>();
        private static readonly object _lock = new object();

        /// <summary>
        /// Event raised when a new message is logged.
        /// </summary>
        public static event EventHandler<LogEventArgs> MessageLogged;

        /// <summary>
        /// Logs a message, adding a timestamp and storing it in the buffer.
        /// </summary>
        /// <param name="message">The message to log.</param>
        public static void Log(string message)
        {
            string formattedMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}";

            lock (_lock)
            {
                if (LogMessages.Count >= MaxLogEntries)
                {
                    LogMessages.Dequeue();
                }
                LogMessages.Enqueue(formattedMessage);
            }

            // Raise the event for any listeners (like the log window)
            OnMessageLogged(formattedMessage);
        }

        /// <summary>
        /// Gets all buffered log messages as a single string.
        /// </summary>
        public static string GetAllLogs()
        {
            lock (_lock)
            {
                return string.Join(Environment.NewLine, LogMessages.ToArray());
            }
        }

        private static void OnMessageLogged(string message)
        {
            MessageLogged?.Invoke(null, new LogEventArgs(message));
        }
    }

    public class LogEventArgs : EventArgs
    {
        public string NewMessage { get; }
        public LogEventArgs(string message)
        {
            NewMessage = message;
        }
    }
}