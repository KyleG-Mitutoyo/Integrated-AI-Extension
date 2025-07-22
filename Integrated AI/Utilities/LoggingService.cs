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