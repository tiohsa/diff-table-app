using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using diff_table_app.Models;
using diff_table_app.Services;
using diff_table_app.Services.Interfaces;
using System.Collections.ObjectModel;
using System.Data;
using System.IO;
using System.Windows;

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
    [ObservableProperty] private string? _selectedSourceSchema;
    [ObservableProperty] private string? _selectedSourceTable;
    
    [ObservableProperty] private ObservableCollection<string> _targetSchemas = new();
    [ObservableProperty] private ObservableCollection<string> _targetTables = new();
    [ObservableProperty] private string? _selectedTargetSchema;
    [ObservableProperty] private string? _selectedTargetTable;

    [ObservableProperty] private string _keysInput = ""; // Comma separated
    [ObservableProperty] private string _ignoreColumnsInput = ""; // Comma separated
    [ObservableProperty] private string _whereClause = "";

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
}
