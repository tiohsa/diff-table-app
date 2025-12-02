using System.Data;

namespace diff_table_app.Services.Interfaces;

public class ColumnInfo
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
}

public interface IDatabaseClient : IDisposable
{
    Task ConnectAsync(string connectionString);
    Task<List<string>> GetSchemasAsync();
    Task<List<string>> GetTablesAsync(string schema);
    Task<List<ColumnInfo>> GetColumnsAsync(string schema, string table);
    Task<List<string>> GetPrimaryKeysAsync(string schema, string table);
    Task<DataTable> ExecuteQueryAsync(string sql, Dictionary<string, object>? parameters = null);
    string QuoteIdentifier(string identifier);
    string FormatValue(object? value);
}
