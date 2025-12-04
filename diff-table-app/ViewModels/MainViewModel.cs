using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using diff_table_app.Models;
using diff_table_app.Services;
using diff_table_app.Services.Interfaces;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Data;
using System.Linq;
using System.IO;
using System.Windows;
using System.ComponentModel;

namespace diff_table_app.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly DiffService _diffService = new();
    private readonly SqlGeneratorService _sqlGeneratorService = new();
    private readonly ILoggerService _logger;
    private readonly PresetService _presetService;
    private bool _isPresetLoading = false;
    private bool _suppressMappingSync = false;

    public ConnectionViewModel SourceConnection { get; } = new();
    public ConnectionViewModel TargetConnection { get; } = new();

    [ObservableProperty] private bool _isTableMode = true;
    [ObservableProperty] private bool _isSqlMode = false;

    // Presets
    [ObservableProperty] private ObservableCollection<Preset> _presets = new();
    [ObservableProperty] private Preset? _selectedPreset;
    [ObservableProperty] private string _newPresetName = "";

    // Table Mode
    private ObservableCollection<string> _allSourceSchemas = new();
    private ObservableCollection<string> _allSourceTables = new();
    private ObservableCollection<string> _allTargetSchemas = new();
    private ObservableCollection<string> _allTargetTables = new();

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

    // Filters
    [ObservableProperty] private string _sourceSchemaFilter = "";
    [ObservableProperty] private string _sourceTableFilter = "";
    [ObservableProperty] private string _targetSchemaFilter = "";
    [ObservableProperty] private string _targetTableFilter = "";

    [ObservableProperty] private string _keysInput = ""; // Comma separated
    [ObservableProperty] private string _ignoreColumnsInput = ""; // Comma separated
    [ObservableProperty] private string _columnMappingInput = ""; // Comma separated, Source=Target
    [ObservableProperty] private string _whereClause = "";
    [ObservableProperty] private ObservableCollection<ColumnMappingEntry> _columnMappings = new();

    [ObservableProperty] private bool _showDiffOnly = false;

    // SQL Mode
    [ObservableProperty] private string _sourceSql = "SELECT * FROM ...";
    [ObservableProperty] private string _targetSql = "SELECT * FROM ...";
    [ObservableProperty] private string _targetTableNameForSql = "";

    // Result
    [ObservableProperty] private DiffResult? _diffResult;
    [ObservableProperty] private DataView? _resultView;
    [ObservableProperty] private bool _isBusy;
    [ObservableProperty] private string _statusMessage = "Ready";

    // Constructor injection for tests
    public MainViewModel(ILoggerService logger, PresetService presetService)
    {
        _logger = logger;
        _presetService = presetService;
        _logger.LogInformation("Application started.");
        HookColumnMappingEvents();
        LoadPresetsCommand.Execute(null);
    }

    // Default constructor for XAML
    public MainViewModel() : this(new FileLoggerService(), new PresetService())
    {
    }

    partial void OnIsTableModeChanged(bool value) => IsSqlMode = !value;
    partial void OnIsSqlModeChanged(bool value) => IsTableMode = !value;

    partial void OnSourceSchemaFilterChanged(string value) => FilterCollection(_allSourceSchemas, SourceSchemas, value);
    partial void OnSourceTableFilterChanged(string value) => FilterCollection(_allSourceTables, SourceTables, value);
    partial void OnTargetSchemaFilterChanged(string value) => FilterCollection(_allTargetSchemas, TargetSchemas, value);
    partial void OnTargetTableFilterChanged(string value) => FilterCollection(_allTargetTables, TargetTables, value);

    private void FilterCollection(ObservableCollection<string> allItems, ObservableCollection<string> filteredItems, string filter)
    {
        // Preserve selection if possible (though binding might clear it when collection changes)
        // We re-populate.
        filteredItems.Clear();
        if (string.IsNullOrWhiteSpace(filter))
        {
            foreach (var item in allItems) filteredItems.Add(item);
        }
        else
        {
            var lowerFilter = filter.ToLowerInvariant();
            foreach (var item in allItems.Where(i => i.ToLowerInvariant().Contains(lowerFilter)))
            {
                filteredItems.Add(item);
            }
        }
    }

    private void ApplySavedColumns(ObservableCollection<string> target, IEnumerable<string> savedColumns)
    {
        if (savedColumns == null) return;
        target.Clear();
        foreach (var column in savedColumns)
        {
            target.Add(column);
        }
    }

    private void ApplyResultFilter(DataView? view = null)
    {
        var targetView = view ?? ResultView;
        if (targetView == null) return;

        targetView.RowFilter = ShowDiffOnly
            ? "IsChanged = true OR Status <> 'Unchanged'"
            : string.Empty;
    }

    partial void OnShowDiffOnlyChanged(bool value) => ApplyResultFilter();

    partial void OnColumnMappingInputChanged(string value)
    {
        if (_suppressMappingSync) return;
        ApplyMappingString(value);
        if (SourceColumns.Any())
        {
            AlignMappingsWithSourceColumns();
        }
    }

    partial void OnDiffResultChanged(DiffResult? value)
    {
        if (value == null)
        {
            ResultView = null;
            return;
        }

        var dt = new DataTable();
        dt.Columns.Add("Status", typeof(string));
        dt.Columns.Add("KeySummary", typeof(string));
        dt.Columns.Add("Column", typeof(string));
        dt.Columns.Add("SourceValue", typeof(object));
        dt.Columns.Add("TargetValue", typeof(object));
        dt.Columns.Add("IsChanged", typeof(bool));

        foreach (var row in value.Rows)
        {
            var keySummary = row.KeyValues.Count == 0
                ? string.Empty
                : string.Join(", ", row.KeyValues.Select(kv => $"{kv.Key}={FormatValue(kv.Value)}"));

            foreach (var columnName in value.Columns)
            {
                if (!row.ColumnDiffs.TryGetValue(columnName, out var cellDiff))
                {
                    continue;
                }

                var dr = dt.NewRow();
                dr["Status"] = row.Status.ToString();
                dr["KeySummary"] = keySummary;
                dr["Column"] = columnName;
                dr["SourceValue"] = row.Status == RowStatus.Added ? null : cellDiff.OldValue;
                dr["TargetValue"] = row.Status == RowStatus.Deleted ? null : cellDiff.NewValue;
                dr["IsChanged"] = row.Status != RowStatus.Unchanged || cellDiff.IsChanged;
                dt.Rows.Add(dr);
            }
        }

        var view = dt.DefaultView;
        ApplyResultFilter(view);
        ResultView = view;
    }

    private static string FormatValue(object? value) => value switch
    {
        null => "NULL",
        DBNull => "NULL",
        DateTime dt => dt.ToString("s"),
        _ => value?.ToString() ?? string.Empty
    };

    partial void OnSelectedSourceSchemaChanged(string? value)
    {
        if (_isPresetLoading) return;
        SourceColumns.Clear();
        ColumnMappings.Clear();
        if (value != null) LoadTablesAsync(SourceConnection, value, _allSourceTables, SourceTables).ConfigureAwait(false);
    }

    partial void OnSelectedTargetSchemaChanged(string? value)
    {
        if (_isPresetLoading) return;
        TargetColumns.Clear();
        ColumnMappings.Clear();
        if (value != null) LoadTablesAsync(TargetConnection, value, _allTargetTables, TargetTables).ConfigureAwait(false);
    }

    partial void OnSelectedTargetTableChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(SelectedTargetSchema))
        {
            TargetColumns.Clear();
            ColumnMappings.Clear();
            RefreshColumnOptionsAsync().ConfigureAwait(false);
        }
    }

    partial void OnSelectedSourceTableChanged(string? value)
    {
        if (!string.IsNullOrEmpty(value) && !string.IsNullOrEmpty(SelectedSourceSchema))
        {
            SourceColumns.Clear();
            ColumnMappings.Clear();
            RefreshColumnOptionsAsync().ConfigureAwait(false);
        }
    }

    // Preset Commands
    [RelayCommand]
    private async Task LoadPresetsAsync()
    {
        var loaded = await _presetService.LoadPresetsAsync();
        Presets.Clear();
        foreach (var p in loaded) Presets.Add(p);
    }

    [RelayCommand]
    private async Task SavePresetAsync()
    {
        SyncColumnMappingString();
        if (string.IsNullOrWhiteSpace(NewPresetName))
        {
            MessageBox.Show("Please enter a preset name.");
            return;
        }

        var preset = new Preset
        {
            Name = NewPresetName,
            SourceConnection = new ConnectionInfo
            {
                SelectedType = SourceConnection.SelectedType,
                Host = SourceConnection.Host,
                Port = SourceConnection.Port,
                User = SourceConnection.User,
                Password = SourceConnection.Password,
                Database = SourceConnection.Database
            },
            TargetConnection = new ConnectionInfo
            {
                SelectedType = TargetConnection.SelectedType,
                Host = TargetConnection.Host,
                Port = TargetConnection.Port,
                User = TargetConnection.User,
                Password = TargetConnection.Password,
                Database = TargetConnection.Database
            },
            IsTableMode = IsTableMode,
            SelectedSourceSchema = SelectedSourceSchema,
            SelectedSourceTable = SelectedSourceTable,
            SelectedTargetSchema = SelectedTargetSchema,
            SelectedTargetTable = SelectedTargetTable,
            SourceColumns = SourceColumns.ToList(),
            TargetColumns = TargetColumns.ToList(),
            KeysInput = KeysInput,
            IgnoreColumnsInput = IgnoreColumnsInput,
            ColumnMappingInput = ColumnMappingInput,
            WhereClause = WhereClause,
            SourceSql = SourceSql,
            TargetSql = TargetSql,
            TargetTableNameForSql = TargetTableNameForSql,
            ShowDiffOnly = ShowDiffOnly
        };

        var existing = Presets.FirstOrDefault(p => p.Name == NewPresetName);
        if (existing != null)
        {
            Presets.Remove(existing);
        }
        Presets.Add(preset);
        await _presetService.SavePresetsAsync(Presets.ToList());
        MessageBox.Show("Preset saved.");
    }

    [RelayCommand]
    private async Task DeletePresetAsync()
    {
        if (SelectedPreset == null) return;
        Presets.Remove(SelectedPreset);
        await _presetService.SavePresetsAsync(Presets.ToList());
    }

    [RelayCommand]
    private async Task ApplyPresetAsync()
    {
        if (SelectedPreset == null) return;
        var value = SelectedPreset;

        IsBusy = true;
        StatusMessage = "Applying Preset...";
        _isPresetLoading = true;
        try
        {
            SourceColumns.Clear();
            TargetColumns.Clear();

            SourceConnection.SelectedType = value.SourceConnection.SelectedType;
            SourceConnection.Host = value.SourceConnection.Host;
            SourceConnection.Port = value.SourceConnection.Port;
            SourceConnection.User = value.SourceConnection.User;
            SourceConnection.Password = value.SourceConnection.Password;
            SourceConnection.Database = value.SourceConnection.Database;

            TargetConnection.SelectedType = value.TargetConnection.SelectedType;
            TargetConnection.Host = value.TargetConnection.Host;
            TargetConnection.Port = value.TargetConnection.Port;
            TargetConnection.User = value.TargetConnection.User;
            TargetConnection.Password = value.TargetConnection.Password;
            TargetConnection.Database = value.TargetConnection.Database;

            IsTableMode = value.IsTableMode;
            NewPresetName = value.Name;

            // Set Inputs
            KeysInput = value.KeysInput;
            IgnoreColumnsInput = value.IgnoreColumnsInput;
            ColumnMappingInput = value.ColumnMappingInput;
            WhereClause = value.WhereClause;
            SourceSql = value.SourceSql;
            TargetSql = value.TargetSql;
            TargetTableNameForSql = value.TargetTableNameForSql;
            ShowDiffOnly = value.ShowDiffOnly;

            if (IsTableMode)
            {
                // Load Schemas
                await LoadSchemaListAsync(SourceConnection, _allSourceSchemas, SourceSchemas);
                await LoadSchemaListAsync(TargetConnection, _allTargetSchemas, TargetSchemas);

                SelectedSourceSchema = value.SelectedSourceSchema;
                SelectedTargetSchema = value.SelectedTargetSchema;

                // Load Tables
                if (!string.IsNullOrEmpty(SelectedSourceSchema))
                {
                    await LoadTablesAsync(SourceConnection, SelectedSourceSchema, _allSourceTables, SourceTables);
                    SelectedSourceTable = value.SelectedSourceTable;
                }

                if (!string.IsNullOrEmpty(SelectedTargetSchema))
                {
                    await LoadTablesAsync(TargetConnection, SelectedTargetSchema, _allTargetTables, TargetTables);
                    SelectedTargetTable = value.SelectedTargetTable;
                }

                await RefreshColumnOptionsAsync();
            }
        }
        finally
        {
            _isPresetLoading = false;
            IsBusy = false;
            StatusMessage = "Preset Applied";
        }
    }

    partial void OnSelectedPresetChanged(Preset? value)
    {
         if (value != null) NewPresetName = value.Name;
    }

    [RelayCommand]
    private async Task LoadSchemasAsync()
    {
        IsBusy = true;
        StatusMessage = "Loading Schemas...";
        _logger.LogInformation("Loading schemas initiated.");
        try
        {
            await LoadSchemaListAsync(SourceConnection, _allSourceSchemas, SourceSchemas);
            await LoadSchemaListAsync(TargetConnection, _allTargetSchemas, TargetSchemas);
            _logger.LogInformation("Schemas loaded successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError("Error loading schemas.", ex);
            MessageBox.Show(ex.Message);
        }
        finally
        {
            IsBusy = false;
            StatusMessage = "Ready";
        }
    }

    [RelayCommand]
    private async Task LoadColumnsForMappingAsync()
    {
        await LoadColumnsFromDatabaseAsync();
    }

    private async Task<bool> LoadColumnsFromDatabaseAsync()
    {
        if (!IsTableMode)
        {
            MessageBox.Show("Column lists can be loaded in Table mode.");
            return false;
        }

        if (string.IsNullOrEmpty(SelectedSourceSchema) || string.IsNullOrEmpty(SelectedSourceTable) ||
            string.IsNullOrEmpty(SelectedTargetSchema) || string.IsNullOrEmpty(SelectedTargetTable))
        {
            MessageBox.Show("Please select schemas and tables before loading columns.");
            return false;
        }

        IsBusy = true;
        StatusMessage = "Loading Columns...";
        try
        {
            await RefreshColumnOptionsAsync();
            StatusMessage = "Columns loaded from database.";
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError("Error loading columns for mapping.", ex);
            MessageBox.Show(ex.Message);
            StatusMessage = "Error loading columns";
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task LoadSchemaListAsync(ConnectionViewModel connVm, ObservableCollection<string> allCollection, ObservableCollection<string> filteredCollection)
    {
        using var client = connVm.CreateClient();
        await client.ConnectAsync(connVm.GetConnectionString());
        var schemas = await client.GetSchemasAsync();
        Application.Current.Dispatcher.Invoke(() =>
        {
            allCollection.Clear();
            filteredCollection.Clear();
            foreach (var s in schemas)
            {
                allCollection.Add(s);
                filteredCollection.Add(s);
            }
        });
    }

    private async Task LoadTablesAsync(ConnectionViewModel connVm, string schema, ObservableCollection<string> allCollection, ObservableCollection<string> filteredCollection)
    {
        try
        {
            using var client = connVm.CreateClient();
            await client.ConnectAsync(connVm.GetConnectionString());
            var tables = await client.GetTablesAsync(schema);
            Application.Current.Dispatcher.Invoke(() =>
            {
                allCollection.Clear();
                filteredCollection.Clear();
                foreach (var t in tables)
                {
                    allCollection.Add(t);
                    filteredCollection.Add(t);
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading tables for schema {schema}.", ex);
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
        catch (Exception ex)
        {
            _logger.LogError($"Error loading keys for table {table}.", ex);
        }
    }

    private async Task LoadColumnsAsync(ConnectionViewModel connVm, string schema, string table, ObservableCollection<string> targetCollection)
    {
        try
        {
            using var client = connVm.CreateClient();
            await client.ConnectAsync(connVm.GetConnectionString());
            var cols = await client.GetColumnsAsync(schema, table);
            Application.Current.Dispatcher.Invoke(() =>
            {
                targetCollection.Clear();
                foreach (var col in cols) targetCollection.Add(col.Name);
            });
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error loading columns for {schema}.{table}.", ex);
        }
    }

    private async Task RefreshColumnOptionsAsync()
    {
        var tasks = new List<Task>();

        if (!string.IsNullOrEmpty(SelectedSourceSchema) && !string.IsNullOrEmpty(SelectedSourceTable))
        {
            tasks.Add(LoadKeysAsync(SourceConnection, SelectedSourceSchema, SelectedSourceTable));
            tasks.Add(LoadColumnsAsync(SourceConnection, SelectedSourceSchema, SelectedSourceTable, SourceColumns));
        }

        if (!string.IsNullOrEmpty(SelectedTargetSchema) && !string.IsNullOrEmpty(SelectedTargetTable))
        {
            tasks.Add(LoadColumnsAsync(TargetConnection, SelectedTargetSchema, SelectedTargetTable, TargetColumns));
        }

        if (tasks.Count > 0)
        {
            await Task.WhenAll(tasks);
            AlignMappingsWithSourceColumns();
        }
    }

    private void AlignMappingsWithSourceColumns()
    {
        _suppressMappingSync = true;

        var existingTargets = ColumnMappings
            .Where(m => !string.IsNullOrWhiteSpace(m.SourceColumn))
            .ToDictionary(m => m.SourceColumn, m => m.TargetColumn, StringComparer.OrdinalIgnoreCase);

        ColumnMappings.Clear();

        foreach (var source in SourceColumns)
        {
            string? target = null;

            if (existingTargets.TryGetValue(source, out var mappedTarget) && TargetColumns.Contains(mappedTarget))
            {
                target = mappedTarget;
            }
            else
            {
                target = TargetColumns.FirstOrDefault(t => string.Equals(t, source, StringComparison.OrdinalIgnoreCase));
            }

            ColumnMappings.Add(new ColumnMappingEntry
            {
                SourceColumn = source,
                TargetColumn = target ?? string.Empty
            });
        }

        _suppressMappingSync = false;
        SyncColumnMappingString();
    }

    private void HookColumnMappingEvents()
    {
        ColumnMappings.CollectionChanged += ColumnMappings_CollectionChanged;
        foreach (var entry in ColumnMappings)
        {
            entry.PropertyChanged += MappingEntry_PropertyChanged;
        }
    }

    private void ColumnMappings_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.NewItems != null)
        {
            foreach (ColumnMappingEntry entry in e.NewItems)
            {
                entry.PropertyChanged += MappingEntry_PropertyChanged;
            }
        }
        if (e.OldItems != null)
        {
            foreach (ColumnMappingEntry entry in e.OldItems)
            {
                entry.PropertyChanged -= MappingEntry_PropertyChanged;
            }
        }
        SyncColumnMappingString();
    }

    private void MappingEntry_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        SyncColumnMappingString();
    }

    private void SyncColumnMappingString()
    {
        if (_suppressMappingSync) return;
        _suppressMappingSync = true;
        ColumnMappingInput = BuildMappingString(ColumnMappings);
        _suppressMappingSync = false;
    }

    private string BuildMappingString(IEnumerable<ColumnMappingEntry> entries)
    {
        return string.Join(",", entries
            .Where(m => !string.IsNullOrWhiteSpace(m.SourceColumn) && !string.IsNullOrWhiteSpace(m.TargetColumn))
            .Select(m => $"{m.SourceColumn}={m.TargetColumn}"));
    }

    private void ApplyMappingString(string value)
    {
        _suppressMappingSync = true;
        ColumnMappings.Clear();
        if (!string.IsNullOrWhiteSpace(value))
        {
            var pairs = value.Split(',', StringSplitOptions.RemoveEmptyEntries);
            foreach (var pair in pairs)
            {
                var parts = pair.Split('=');
                if (parts.Length == 2)
                {
                    ColumnMappings.Add(new ColumnMappingEntry
                    {
                        SourceColumn = parts[0].Trim(),
                        TargetColumn = parts[1].Trim()
                    });
                }
            }
        }
        _suppressMappingSync = false;
    }

    private Dictionary<string, string> BuildColumnMappingDictionary()
    {
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in ColumnMappings)
        {
            if (string.IsNullOrWhiteSpace(entry.SourceColumn) || string.IsNullOrWhiteSpace(entry.TargetColumn)) continue;
            dict[entry.SourceColumn] = entry.TargetColumn;
        }
        return dict;
    }

    [RelayCommand]
    private async Task AddMappingAsync()
    {
        if (SourceColumns.Count == 0 || TargetColumns.Count == 0)
        {
            var loaded = await LoadColumnsFromDatabaseAsync();
            if (!loaded) return;
        }

        var sourceDefault = SourceColumns.FirstOrDefault() ?? string.Empty;
        var targetDefault = TargetColumns.FirstOrDefault() ?? string.Empty;
        ColumnMappings.Add(new ColumnMappingEntry { SourceColumn = sourceDefault, TargetColumn = targetDefault });
    }

    [RelayCommand]
    private void RemoveMapping(ColumnMappingEntry? entry)
    {
        if (entry == null) return;
        ColumnMappings.Remove(entry);
    }

    [RelayCommand]
    private async Task CompareAsync()
    {
        IsBusy = true;
        StatusMessage = "Comparing...";
        _logger.LogInformation("Comparison started.");
        try
        {
            DataTable dtSource, dtTarget;
            List<string> keys;
            List<string> ignoreCols = IgnoreColumnsInput.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            Dictionary<string, string> colMapping = BuildColumnMappingDictionary();

            if (IsTableMode)
            {
                if (string.IsNullOrEmpty(SelectedSourceSchema) || string.IsNullOrEmpty(SelectedSourceTable) ||
                    string.IsNullOrEmpty(SelectedTargetSchema) || string.IsNullOrEmpty(SelectedTargetTable))
                {
                    MessageBox.Show("Please select schemas and tables.");
                    _logger.LogWarning("Comparison aborted: Schemas/Tables not selected.");
                    return;
                }

                keys = KeysInput.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                if (keys.Count == 0)
                {
                    MessageBox.Show("Please define keys.");
                    _logger.LogWarning("Comparison aborted: Keys not defined.");
                    return;
                }

                using var sClient = SourceConnection.CreateClient();
                await sClient.ConnectAsync(SourceConnection.GetConnectionString());
                // 簡易的に SELECT * で取得。本来はカラム指定すべき。
                var where = string.IsNullOrWhiteSpace(WhereClause) ? "" : $"WHERE {WhereClause}";
                var srcSql = $"SELECT * FROM {sClient.QuoteIdentifier(SelectedSourceSchema)}.{sClient.QuoteIdentifier(SelectedSourceTable)} {where}";
                _logger.LogInformation($"Executing Source Query: {srcSql}");
                dtSource = await sClient.ExecuteQueryAsync(srcSql);

                using var tClient = TargetConnection.CreateClient();
                await tClient.ConnectAsync(TargetConnection.GetConnectionString());
                var tgtSql = $"SELECT * FROM {tClient.QuoteIdentifier(SelectedTargetSchema)}.{tClient.QuoteIdentifier(SelectedTargetTable)} {where}";
                _logger.LogInformation($"Executing Target Query: {tgtSql}");
                dtTarget = await tClient.ExecuteQueryAsync(tgtSql);
            }
            else
            {
                keys = KeysInput.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
                
                using var sClient = SourceConnection.CreateClient();
                await sClient.ConnectAsync(SourceConnection.GetConnectionString());
                _logger.LogInformation("Executing Source SQL in SQL Mode.");
                dtSource = await sClient.ExecuteQueryAsync(SourceSql);

                using var tClient = TargetConnection.CreateClient();
                await tClient.ConnectAsync(TargetConnection.GetConnectionString());
                _logger.LogInformation("Executing Target SQL in SQL Mode.");
                dtTarget = await tClient.ExecuteQueryAsync(TargetSql);
            }

            DiffResult = await Task.Run(() => _diffService.Compare(dtSource, dtTarget, keys, ignoreCols, colMapping));
            var msg = $"Diff Complete. Added: {DiffResult.AddedCount}, Deleted: {DiffResult.DeletedCount}, Modified: {DiffResult.ModifiedCount}";
            StatusMessage = msg;
            _logger.LogInformation(msg);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}");
            StatusMessage = "Error";
            _logger.LogError("Error during comparison.", ex);
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

            _logger.LogInformation($"Generating SQL for target table: {targetTable}");
            var keys = KeysInput.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()).ToList();
            
            // Target Clientを使ってクォート処理などを行う
            using var tClient = TargetConnection.CreateClient();
            
            var sql = await Task.Run(() => _sqlGeneratorService.GenerateSql(DiffResult, targetTable, tClient, keys));
            
            var saveDlg = new Microsoft.Win32.SaveFileDialog { Filter = "SQL Files|*.sql", FileName = "diff_sync.sql" };
            if (saveDlg.ShowDialog() == true)
            {
                await File.WriteAllTextAsync(saveDlg.FileName, sql);
                MessageBox.Show("Saved.");
                _logger.LogInformation($"SQL saved to {saveDlg.FileName}");
            }
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Error: {ex.Message}");
            _logger.LogError("Error generating SQL.", ex);
        }
    }
}
