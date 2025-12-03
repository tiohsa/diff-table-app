using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace diff_table_app.Models;

public class ConnectionInfo
{
    public DatabaseType SelectedType { get; set; }
    public string Host { get; set; } = string.Empty;
    public int Port { get; set; }
    public string User { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string Database { get; set; } = string.Empty;
}

public class Preset
{
    public string Name { get; set; } = string.Empty;
    public ConnectionInfo SourceConnection { get; set; } = new();
    public ConnectionInfo TargetConnection { get; set; } = new();

    public bool IsTableMode { get; set; } = true;

    // Table Mode Settings
    public string? SelectedSourceSchema { get; set; }
    public string? SelectedSourceTable { get; set; }
    public string? SelectedTargetSchema { get; set; }
    public string? SelectedTargetTable { get; set; }
    public List<string> SourceColumns { get; set; } = new();
    public List<string> TargetColumns { get; set; } = new();
    public string KeysInput { get; set; } = string.Empty;
    public string IgnoreColumnsInput { get; set; } = string.Empty;
    public string ColumnMappingInput { get; set; } = string.Empty;
    public string WhereClause { get; set; } = string.Empty;
    public bool ShowDiffOnly { get; set; } = false;

    // SQL Mode Settings
    public string SourceSql { get; set; } = string.Empty;
    public string TargetSql { get; set; } = string.Empty;
    public string TargetTableNameForSql { get; set; } = string.Empty;
}
