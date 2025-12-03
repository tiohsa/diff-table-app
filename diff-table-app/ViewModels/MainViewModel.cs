using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using diff_table_app.Models;
using diff_table_app.Services;
using diff_table_app.Services.Interfaces;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Win32;

namespace diff_table_app.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DiffService _diffService = new();
    private readonly SqlGeneratorService _sqlGeneratorService = new();

    public ConnectionViewModel SourceConnection { get; } = new();
    public ConnectionViewModel TargetConnection { get; } = new();

    [ObservableProperty] private bool _isTableMode = true;
    [ObservableProperty] private bool _isSqlMode = false;

    // Table Mode
    [ObservableProperty] private ObservableCollection<string> _sourceSchemas = new();
    [ObservableProperty] private ObservableCollection<string> _sourceTables = new();
    [ObservableProperty] private ObservableCollection<string> _sourceColumns = new();
    [ObservableProperty] private string? _selectedSourceSchema;
    [ObservableProperty] private string? _selectedSourceTable;

    [ObservableProperty] private ObservableCollection<string> _targetSchemas = new();
    [ObservableProperty] private ObservableCollection<string> _targetTables = new();
    [ObservableProperty] private ObservableCollection<string> _targetColumns = new();
    [ObservableProperty] private string? _selectedTargetSchema;
    [ObservableProperty] private string? _selectedTargetTable;

    [ObservableProperty] private string _keysInput = ""; // Comma separated
    [ObservableProperty] private string _ignoreColumnsInput = ""; // Comma separated
    [ObservableProperty] private string _whereClause = "";
    [ObservableProperty] private ObservableCollection<ColumnMapping> _columnMappings = new();

    // SQL Mode
    [ObservableProperty] private string _sourceSql = "SELECT * FROM ...";
    [ObservableProperty] private string _targetSql = "SELECT * FROM ...";
    [ObservableProperty] private string _targetTableNameForSql = "";

    // Result
    [ObservableProperty] private DiffResult? _diffResult;
    [ObservableProperty] private DataView? _resultView;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Ready";

    partial void OnIsTableModeChanged(bool value) => IsSqlMode = !value;
    partial void OnIsSqlModeChanged(bool value) => IsTableMode = !value;

    partial void OnDiffResultChanged(DiffResult? value)
    {
        if (value == null)
        {
            ResultView = null;
            return;
        }

        var dt = new DataTable();
        dt.Columns.Add("Status", typeof(string));
        
        foreach (var col in value.Columns)
        {
            dt.Columns.Add(col, typeof(object));
        }

        foreach (var row in value.Rows)
        {
            var dr = dt.NewRow();
            dr["Status"] = row.Status.ToString();
            foreach (var col in value.Columns)
            {
                if (row.ColumnDiffs.TryGetValue(col, out var cellDiff))
                {
                    if (row.Status == RowStatus.Deleted)
                        dr[col] = cellDiff.OldValue;
                    else
                        dr[col] = cellDiff.NewValue;
                }
            }
            dt.Rows.Add(dr);
        }
        ResultView = dt.DefaultView;
    }

    partial void OnSelectedSourceSchemaChanged(string? value)
    {
        if (value != null) LoadTablesAsync(SourceConnection, value, SourceTables).ConfigureAwait(false);
    }

    partial void OnSelectedTargetSchemaChanged(string? value)
    {
        if (value != null) LoadTablesAsync(TargetConnection, value, TargetTables).ConfigureAwait(false);
    }

    partial void OnSelectedSourceTableChanged(string? value)
    {
        if (value != null && !string.IsNullOrEmpty(SelectedSourceSchema))
        {
             LoadKeysAsync(SourceConnection, SelectedSourceSchema, value).ConfigureAwait(false);
             LoadColumnsAsync(SourceConnection, SelectedSourceSchema, value, SourceColumns).ConfigureAwait(false);
        }
    }

    partial void OnSelectedTargetTableChanged(string? value)
    {
        if (value != null && !string.IsNullOrEmpty(SelectedTargetSchema))
        {
            LoadColumnsAsync(TargetConnection, SelectedTargetSchema, value, TargetColumns).ConfigureAwait(false);
        }
    }

    [RelayCommand]
    private async Task LoadSchemasAsync()
    {
        IsBusy = true;
        StatusMessage = "Loading Schemas...";
        try
        {
            await LoadSchemaListAsync(SourceConnection, SourceSchemas);
            await LoadSchemaListAsync(TargetConnection, TargetSchemas);
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = "Ready";
        }
    }

    [RelayCommand]
    private void AddMapping()
    {
        ColumnMappings.Add(new ColumnMapping());
    }

    [RelayCommand]
    private void RemoveMapping(ColumnMapping? mapping)
    {
        if (mapping != null)
        {
            ColumnMappings.Remove(mapping);
        }
    }

    private async Task LoadSchemaListAsync(ConnectionViewModel connVm, ObservableCollection<string> collection)
    {
        using var client = connVm.CreateClient();
        await client.ConnectAsync(connVm.GetConnectionString());
        var schemas = await client.GetSchemasAsync();
        Application.Current.Dispatcher.Invoke(() =>
        {
            collection.Clear();
            foreach (var s in schemas) collection.Add(s);
        });
    }

    private async Task LoadTablesAsync(ConnectionViewModel connVm, string schema, ObservableCollection<string> collection)
    {
        try
        {
            using var client = connVm.CreateClient();
            await client.ConnectAsync(connVm.GetConnectionString());
            var tables = await client.GetTablesAsync(schema);
            Application.Current.Dispatcher.Invoke(() =>
            {
                collection.Clear();
                foreach (var t in tables) collection.Add(t);
            });
        }
        catch (Exception ex)
        {
            // Log or show error
        }
    }
    
    private async Task LoadKeysAsync(ConnectionViewModel connVm, string schema, string table)
    {
        try
        {
            using var client = connVm.CreateClient();
            await client.ConnectAsync(connVm.GetConnectionString());
            var keys = await client.GetPrimaryKeysAsync(schema, table);
            Application.Current.Dispatcher.Invoke(() =>
            {
                KeysInput = string.Join(",", keys);
            });
        }
        catch
        {
            // ignore
        }
    }

    private async Task LoadColumnsAsync(ConnectionViewModel connVm, string schema, string table, ObservableCollection<string> collection)
    {
        try
        {
            using var client = connVm.CreateClient();
            await client.ConnectAsync(connVm.GetConnectionString());
            var columns = await client.GetColumnsAsync(schema, table);
            Application.Current.Dispatcher.Invoke(() =>
            {
                collection.Clear();
                foreach (var col in columns)
                {
                    collection.Add(col.Name);
                }
            });
        }
        catch
        {
            // ignore
        }
    }

    private void ApplyColumnMappings(DataTable source, DataTable target, List<ColumnMapping> mappings)
    {
        foreach (var mapping in mappings)
        {
            if (string.IsNullOrWhiteSpace(mapping.SourceColumn) || string.IsNullOrWhiteSpace(mapping.TargetColumn)) continue;
            if (!source.Columns.Contains(mapping.SourceColumn) || !target.Columns.Contains(mapping.TargetColumn)) continue;

            if (!target.Columns.Contains(mapping.SourceColumn))
            {
                target.Columns.Add(mapping.SourceColumn, target.Columns[mapping.TargetColumn].DataType);
            }

            foreach (DataRow row in target.Rows)
            {
                row[mapping.SourceColumn] = row[mapping.TargetColumn];
            }
        }
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        IsBusy = true;
        StatusMessage = "Comparing...";
        try
        {
            DataTable dtSource, dtTarget;
            List<string> keys;
            List<string> ignoreCols = IgnoreColumnsInput.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            var mappings = ColumnMappings.Where(m => !string.IsNullOrWhiteSpace(m.SourceColumn) && !string.IsNullOrWhiteSpace(m.TargetColumn)).ToList();

            if (IsTableMode)
            {
                if (string.IsNullOrEmpty(SelectedSourceSchema) || string.IsNullOrEmpty(SelectedSourceTable) ||
                    string.IsNullOrEmpty(SelectedTargetSchema) || string.IsNullOrEmpty(SelectedTargetTable))
                {
                    MessageBox.Show("Please select schemas and tables.");
                    return;
                }

                keys = KeysInput.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                if (keys.Count == 0)
                {
                    MessageBox.Show("Please define keys.");
                    return;
                }

                using var sClient = SourceConnection.CreateClient();
                await sClient.ConnectAsync(SourceConnection.GetConnectionString());
                // 簡易的に SELECT * で取得。本来はカラム指定すべき。
                var where = string.IsNullOrWhiteSpace(WhereClause) ? "" : $"WHERE {WhereClause}";
                dtSource = await sClient.ExecuteQueryAsync($"SELECT * FROM {sClient.QuoteIdentifier(SelectedSourceSchema)}.{sClient.QuoteIdentifier(SelectedSourceTable)} {where}");

                using var tClient = TargetConnection.CreateClient();
                await tClient.ConnectAsync(TargetConnection.GetConnectionString());
                dtTarget = await tClient.ExecuteQueryAsync($"SELECT * FROM {tClient.QuoteIdentifier(SelectedTargetSchema)}.{tClient.QuoteIdentifier(SelectedTargetTable)} {where}");
            }
            else
            {
                keys = KeysInput.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                
                using var sClient = SourceConnection.CreateClient();
                await sClient.ConnectAsync(SourceConnection.GetConnectionString());
                dtSource = await sClient.ExecuteQueryAsync(SourceSql);

                using var tClient = TargetConnection.CreateClient();
                await tClient.ConnectAsync(TargetConnection.GetConnectionString());
                dtTarget = await tClient.ExecuteQueryAsync(TargetSql);
            }

            ApplyColumnMappings(dtSource, dtTarget, mappings);

            DiffResult = await Task.Run(() => _diffService.Compare(dtSource, dtTarget, keys, ignoreCols));
            StatusMessage = $"Diff Complete. Added: {DiffResult.AddedCount}, Deleted: {DiffResult.DeletedCount}, Modified: {DiffResult.ModifiedCount}";
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}");
            StatusMessage = "Error";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private async Task GenerateSqlAsync()
    {
        if (DiffResult == null) return;

        try
        {
            var targetTable = IsTableMode ? $"{SelectedTargetSchema}.{SelectedTargetTable}" : TargetTableNameForSql;
            if (string.IsNullOrEmpty(targetTable))
            {
                MessageBox.Show("Target table name is required.");
                return;
            }

            var keys = KeysInput.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            
            // Target Clientを使ってクォート処理などを行う
            using var tClient = TargetConnection.CreateClient();
            
            var sql = await Task.Run(() => _sqlGeneratorService.GenerateSql(DiffResult, targetTable, tClient, keys));
            
            var saveDlg = new Microsoft.Win32.SaveFileDialog { Filter = "SQL Files|*.sql", FileName = "diff_sync.sql" };
            if (saveDlg.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(saveDlg.FileName, sql);
                MessageBox.Show("Saved.");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}");
        }
    }

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        var dialog = new SaveFileDialog { Filter = "Preset Files|*.json", FileName = "preset.json" };
        if (dialog.ShowDialog() != true) return;

        var preset = BuildPreset();
        var json = JsonSerializer.Serialize(preset, new JsonSerializerOptions { WriteIndented = true });
        await File.WriteAllTextAsync(dialog.FileName, json);
        MessageBox.Show("Preset saved.");
    }

    [RelayCommand]
    private async Task LoadPresetAsync()
    {
        var dialog = new OpenFileDialog { Filter = "Preset Files|*.json" };
        if (dialog.ShowDialog() != true) return;

        var json = await File.ReadAllTextAsync(dialog.FileName);
        var preset = JsonSerializer.Deserialize<Preset>(json);
        if (preset == null) return;

        await ApplyPresetAsync(preset);
        MessageBox.Show("Preset loaded.");
    }

    private Preset BuildPreset()
    {
        return new Preset
        {
            SourceConnection = BuildConnectionPreset(SourceConnection),
            TargetConnection = BuildConnectionPreset(TargetConnection),
            SelectedSourceSchema = SelectedSourceSchema,
            SelectedSourceTable = SelectedSourceTable,
            SelectedTargetSchema = SelectedTargetSchema,
            SelectedTargetTable = SelectedTargetTable,
            KeysInput = KeysInput,
            IgnoreColumnsInput = IgnoreColumnsInput,
            WhereClause = WhereClause,
            SourceSql = SourceSql,
            TargetSql = TargetSql,
            TargetTableNameForSql = TargetTableNameForSql,
            ColumnMappings = ColumnMappings.Select(m => new ColumnMapping { SourceColumn = m.SourceColumn, TargetColumn = m.TargetColumn }).ToList()
        };
    }

    private ConnectionPreset BuildConnectionPreset(ConnectionViewModel vm)
    {
        return new ConnectionPreset
        {
            SelectedType = vm.SelectedType,
            Host = vm.Host,
            Port = vm.Port,
            User = vm.User,
            Password = vm.Password,
            Database = vm.Database,
            ServiceName = vm.ServiceName
        };
    }

    private async Task ApplyPresetAsync(Preset preset)
    {
        ApplyConnectionPreset(SourceConnection, preset.SourceConnection);
        ApplyConnectionPreset(TargetConnection, preset.TargetConnection);

        await LoadSchemaListAsync(SourceConnection, SourceSchemas);
        await LoadSchemaListAsync(TargetConnection, TargetSchemas);

        SelectedSourceSchema = preset.SelectedSourceSchema;
        SelectedTargetSchema = preset.SelectedTargetSchema;

        if (!string.IsNullOrEmpty(preset.SelectedSourceSchema))
        {
            await LoadTablesAsync(SourceConnection, preset.SelectedSourceSchema, SourceTables);
        }

        if (!string.IsNullOrEmpty(preset.SelectedTargetSchema))
        {
            await LoadTablesAsync(TargetConnection, preset.SelectedTargetSchema, TargetTables);
        }

        SelectedSourceTable = preset.SelectedSourceTable;
        SelectedTargetTable = preset.SelectedTargetTable;

        KeysInput = preset.KeysInput;
        IgnoreColumnsInput = preset.IgnoreColumnsInput;
        WhereClause = preset.WhereClause;
        SourceSql = preset.SourceSql;
        TargetSql = preset.TargetSql;
        TargetTableNameForSql = preset.TargetTableNameForSql;

        ColumnMappings.Clear();
        foreach (var mapping in preset.ColumnMappings ?? Enumerable.Empty<ColumnMapping>())
        {
            ColumnMappings.Add(new ColumnMapping { SourceColumn = mapping.SourceColumn, TargetColumn = mapping.TargetColumn });
        }
    }

    private void ApplyConnectionPreset(ConnectionViewModel vm, ConnectionPreset preset)
    {
        vm.SelectedType = preset.SelectedType;
        vm.Host = preset.Host;
        vm.Port = preset.Port;
        vm.User = preset.User;
        vm.Password = preset.Password;
        vm.Database = preset.Database;
        vm.ServiceName = preset.ServiceName;
    }
}
