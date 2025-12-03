using diff_table_app.ViewModels;

namespace diff_table_app.Models;

public class ConnectionPreset
{
    public DatabaseType SelectedType { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
    public string ServiceName { get; set; } = string.Empty;
}

public class Preset
{
    public ConnectionPreset SourceConnection { get; set; } = new();
    public ConnectionPreset TargetConnection { get; set; } = new();

    public string? SelectedSourceSchema { get; set; }
    public string? SelectedSourceTable { get; set; }
    public string? SelectedTargetSchema { get; set; }
    public string? SelectedTargetTable { get; set; }

    public string KeysInput { get; set; } = string.Empty;
    public string IgnoreColumnsInput { get; set; } = string.Empty;
    public string WhereClause { get; set; } = string.Empty;

    public string SourceSql { get; set; } = string.Empty;
    public string TargetSql { get; set; } = string.Empty;
    public string TargetTableNameForSql { get; set; } = string.Empty;

    public List<ColumnMapping> ColumnMappings { get; set; } = new();
}
