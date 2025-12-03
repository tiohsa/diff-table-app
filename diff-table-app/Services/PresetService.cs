using diff_table_app.Models;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace diff_table_app.Services;

public class PresetService
{
    private readonly string _presetsFilePath;

    public PresetService(string? presetsFilePath = null)
    {
        _presetsFilePath = presetsFilePath ?? Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "presets.json");
    }

    public async Task<List<Preset>> LoadPresetsAsync()
    {
        if (!File.Exists(_presetsFilePath))
        {
            return new List<Preset>();
        }

        try
        {
            var json = await File.ReadAllTextAsync(_presetsFilePath);
            return JsonSerializer.Deserialize<List<Preset>>(json) ?? new List<Preset>();
        }
        catch
        {
            return new List<Preset>();
        }
    }

    public async Task SavePresetsAsync(List<Preset> presets)
    {
        try
        {
            var json = JsonSerializer.Serialize(presets, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(_presetsFilePath, json);
        }
        catch
        {
            // Handle error appropriately
        }
    }
}
