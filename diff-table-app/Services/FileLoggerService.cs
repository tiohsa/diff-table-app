using diff_table_app.Services.Interfaces;
using System.IO;
using System.Linq;
using System.Text;

namespace diff_table_app.Services;

public class FileLoggerService : ILoggerService
{
    private readonly string _logDirectory;
    private readonly string _logFilePath;
    private readonly object _lock = new();
    private const long MaxFileSize = 5 * 1024 * 1024; // 5MB
    private const int MaxRetainedFiles = 5;

    public FileLoggerService(string? logDirectory = null)
    {
        _logDirectory = logDirectory ?? Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
        _logFilePath = Path.Combine(_logDirectory, "app.log");

        if (!Directory.Exists(_logDirectory))
        {
            Directory.CreateDirectory(_logDirectory);
        }
    }

    public void LogInformation(string message) => Log("INFO", message);
    public void LogWarning(string message) => Log("WARN", message);
    public void LogError(string message, Exception? ex = null)
    {
        var sb = new StringBuilder(message);
        if (ex != null)
        {
            sb.AppendLine();
            sb.Append(ex.ToString());
        }
        Log("ERROR", sb.ToString());
    }

    private void Log(string level, string message)
    {
        var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {message}{Environment.NewLine}";

        lock (_lock)
        {
            try
            {
                CheckRotation();
                File.AppendAllText(_logFilePath, logEntry);
            }
            catch (Exception)
            {
                // Fail silently or write to debug console
                System.Diagnostics.Debug.WriteLine($"Failed to write log: {message}");
            }
        }
    }

    private void CheckRotation()
    {
        var fileInfo = new FileInfo(_logFilePath);
        if (!fileInfo.Exists || fileInfo.Length < MaxFileSize) return;

        var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        var archivePath = Path.Combine(_logDirectory, $"app_{timestamp}.log");

        try
        {
            fileInfo.MoveTo(archivePath);
            CleanupOldLogs();
        }
        catch (Exception ex)
        {
             System.Diagnostics.Debug.WriteLine($"Failed to rotate log: {ex.Message}");
        }
    }

    private void CleanupOldLogs()
    {
        var files = Directory.GetFiles(_logDirectory, "app_*.log")
                             .Select(f => new FileInfo(f))
                             .OrderByDescending(f => f.CreationTime)
                             .ToList();

        if (files.Count > MaxRetainedFiles)
        {
            foreach (var file in files.Skip(MaxRetainedFiles))
            {
                try
                {
                    file.Delete();
                }
                catch
                {
                    // Ignore deletion errors
                }
            }
        }
    }
}
