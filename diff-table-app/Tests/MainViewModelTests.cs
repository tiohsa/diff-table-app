using Xunit;
using diff_table_app.ViewModels;
using diff_table_app.Services;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System;

namespace diff_table_app.Tests;

public class MainViewModelTests : IDisposable
{
    private readonly string _tempLogDir;
    private readonly string _tempPresetFile;

    public MainViewModelTests()
    {
        _tempLogDir = Path.Combine(Path.GetTempPath(), "diff_vm_logs_" + Guid.NewGuid());
        _tempPresetFile = Path.Combine(Path.GetTempPath(), "diff_vm_presets_" + Guid.NewGuid() + ".json");
    }

    [Fact]
    public void IsBusy_ShouldBeFalse_Initially()
    {
        // Arrange
        // Inject services with safe paths to avoid conflicting with other tests or global state
        var logger = new FileLoggerService(_tempLogDir);
        var presetService = new PresetService(_tempPresetFile);
        var vm = new MainViewModel(logger, presetService);

        // Assert
        Assert.False(vm.IsBusy);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempLogDir)) Directory.Delete(_tempLogDir, true);
            if (File.Exists(_tempPresetFile)) File.Delete(_tempPresetFile);
        }
        catch { /* ignore */ }
    }
}
