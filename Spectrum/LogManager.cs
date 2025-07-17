using System.Diagnostics;
using System.Text;

namespace Spectrum
{
    public class LogManager
    {
        public enum LogLevel
        {
            Debug,
            Info,
            Warning,
            Error,
            Critical
        }
        public class LogEntry
        {
            public DateTime Timestamp { get; set; }
            public LogLevel Level { get; set; }
            public string Message { get; set; }
            public LogEntry(LogLevel level, string message)
            {
                Timestamp = DateTime.Now;
                Level = level;
                Message = message;
            }
            public override string ToString()
            {
                return $"[{Timestamp:yyyy-MM-dd HH:mm:ss}] [{Level}] {Message}";
            }
        }
        private static List<LogEntry> _logEntries = new List<LogEntry>();
        public static IReadOnlyList<LogEntry> LogEntries => _logEntries.AsReadOnly();
        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
            Debug.WriteLine(logMessage);
            _logEntries.Add(new LogEntry(level, message));
        }
        public static void SaveLog(string filePath)
        {
            try
            {
                var logContent = new StringBuilder();
                foreach (var entry in _logEntries)
                {
                    logContent.AppendLine(entry.ToString());
                }
                File.WriteAllText(filePath, logContent.ToString());
                Log($"Log saved to {filePath}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                Log($"Failed to save log: {ex.Message}", LogLevel.Error);
            }
        }
        public static void ClearLog()
        {
            _logEntries.Clear();
            Log("Log cleared.", LogLevel.Info);
        }

        public static void OpenLogFolder()
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = Directory.GetCurrentDirectory(),
                UseShellExecute = true
            });
        }
    }
}