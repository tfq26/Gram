namespace Gram.Blazor.Shared;

public static class GramLog
{
    private static readonly object _sync = new();
    private static string? _logFilePath;

    public static string LogFilePath
    {
        get
        {
            if (_logFilePath != null) return _logFilePath;

            var rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Projects", "Gram", ".logs");
            try
            {
                Directory.CreateDirectory(rootDir);
                _logFilePath = Path.Combine(rootDir, "gram-activity.log");
            }
            catch
            {
                var fallbackDir = Path.Combine(Path.GetTempPath(), "Gram", "logs");
                Directory.CreateDirectory(fallbackDir);
                _logFilePath = Path.Combine(fallbackDir, "gram-activity.log");
            }
            return _logFilePath;
        }
    }

    public static void Info(string source, string message) => Log("INFO", source, message);
    public static void Warn(string source, string message) => Log("WARN", source, message);
    public static void Error(string source, string message, Exception? ex = null)
    {
        var formatted = ex != null ? $"{message}\n{ex}" : message;
        Log("ERROR", source, formatted);
        Console.Error.WriteLine($"[{DateTime.UtcNow:HH:mm:ss.fff}] [{source}] ERROR: {formatted}");
    }

    private static void Log(string level, string source, string message)
    {
        try
        {
            var line = $"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff}] [{level}] [{source}] {message}";
            lock (_sync)
            {
                File.AppendAllText(LogFilePath, line + Environment.NewLine);
            }
        }
        catch
        {
            // Ignore logging file write errors
        }
    }
}
