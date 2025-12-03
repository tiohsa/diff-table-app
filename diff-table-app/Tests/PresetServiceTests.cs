using Xunit;
using diff_table_app.Models;
using diff_table_app.Services;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System;

namespace diff_table_app.Tests;

public class PresetServiceTests : IDisposable
{
    private readonly string _presetsFilePath;

    public PresetServiceTests()
    {
        _presetsFilePath = Path.Combine(Path.GetTempPath(), $"presets_{Guid.NewGuid()}.json");
        if (File.Exists(_presetsFilePath))
        {
            File.Delete(_presetsFilePath);
        }
    }

    [Fact]
    public async Task SaveAndLoadPresets_ShouldWork()
    {
        var service = new PresetService(_presetsFilePath);
        var presets = new List<Preset>
        {
            new Preset { Name = "Test1", IsTableMode = true },
            new Preset { Name = "Test2", IsTableMode = false }
        };

        await service.SavePresetsAsync(presets);

        Assert.True(File.Exists(_presetsFilePath));

        var loaded = await service.LoadPresetsAsync();
        Assert.Equal(2, loaded.Count);
        Assert.Equal("Test1", loaded[0].Name);
        Assert.True(loaded[0].IsTableMode);
        Assert.Equal("Test2", loaded[1].Name);
        Assert.False(loaded[1].IsTableMode);
    }

    [Fact]
    public async Task LoadPresets_WhenFileDoesNotExist_ShouldReturnEmpty()
    {
        var service = new PresetService(_presetsFilePath);
        var loaded = await service.LoadPresetsAsync();
        Assert.Empty(loaded);
    }

    public void Dispose()
    {
        try
        {
            if (File.Exists(_presetsFilePath))
            {
                File.Delete(_presetsFilePath);
            }
        }
        catch
        {
            // Ignore cleanup errors
        }
    }
}
