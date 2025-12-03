using Xunit;
using diff_table_app.Services;
using System.IO;
using System.Threading;
using System.Linq;
using System;

namespace diff_table_app.Tests;

public class FileLoggerServiceTests : IDisposable
{
    private readonly string _logsPath;

    public FileLoggerServiceTests()
    {
        _logsPath = Path.Combine(Path.GetTempPath(), "diff_table_app_test_logs_" + Guid.NewGuid());
        if (Directory.Exists(_logsPath))
        {
            Directory.Delete(_logsPath, true);
        }
    }

    [Fact]
    public void LogInformation_ShouldCreateLogFile()
    {
        var logger = new FileLoggerService(_logsPath);
        logger.LogInformation("Test Message");

        var file = Path.Combine(_logsPath, "app.log");
        Assert.True(File.Exists(file));

        var content = File.ReadAllText(file);
        Assert.Contains("[INFO] Test Message", content);
    }

    [Fact]
    public void LogError_ShouldIncludeException()
    {
        var logger = new FileLoggerService(_logsPath);
        try
        {
            throw new InvalidOperationException("Test Exception");
        }
        catch(Exception ex)
        {
            logger.LogError("Error occurred", ex);
        }

        var file = Path.Combine(_logsPath, "app.log");
        var content = File.ReadAllText(file);
        Assert.Contains("[ERROR] Error occurred", content);
        Assert.Contains("System.InvalidOperationException: Test Exception", content);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_logsPath))
            {
                Directory.Delete(_logsPath, true);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
