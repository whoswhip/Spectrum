using System.Diagnostics;
using System.Text;


namespace Spectrum
{
    public class LogManager
    {
        private static List<LogEntry> _logEntries = new List<LogEntry>();
        private static readonly object _logLock = new object();
        private static ConfigManager<ConfigData>? mainConfig;
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
        public static IReadOnlyList<LogEntry> LogEntries => _logEntries.AsReadOnly();
        public static void Log(string message, LogLevel level = LogLevel.Info)
        {
            if (string.IsNullOrWhiteSpace(message)) return;
            if (mainConfig == null)
            {
                try { mainConfig = Program.mainConfig; } catch { }
            }

            bool debugEnabled = Debugger.IsAttached;
            try
            {
                debugEnabled = debugEnabled || (mainConfig?.Data?.DebugMode ?? false);
            }
            catch { }
            if (level == LogLevel.Debug && !debugEnabled)
                return;
            try
            {
                string logMessage = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{level}] {message}";
                Debug.WriteLine(logMessage);
                lock (_logLock)
                {
                    if (_logEntries == null)
                    {
                        _logEntries = new List<LogEntry>();
                    }
                    _logEntries.Add(new LogEntry(level, message));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[LogManager ERROR] Failed to append log entry: {ex.Message}");
            }
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
                Arguments = Path.Combine(Directory.GetCurrentDirectory(), "bin", "logs"),
                UseShellExecute = true
            });
        }
    }
}