namespace diff_table_app.Services.Interfaces;

public interface ILoggerService
{
    void LogInformation(string message);
    void LogError(string message, Exception? ex = null);
    void LogWarning(string message);
}
