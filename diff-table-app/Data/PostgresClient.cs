using System.Data;
using diff_table_app.Services.Interfaces;
using Npgsql;

namespace diff_table_app.Data;

public class PostgresClient : IDatabaseClient
{
    private NpgsqlConnection? _connection;

    public async Task ConnectAsync(string connectionString)
    {
        _connection = new NpgsqlConnection(connectionString);
        await _connection.OpenAsync();
    }

    public async Task<List<string>> GetSchemasAsync()
    {
        var list = new List<string>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT schema_name FROM information_schema.schemata ORDER BY schema_name";
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public async Task<List<string>> GetTablesAsync(string schema)
    {
        var list = new List<string>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema ORDER BY table_name";
        cmd.Parameters.AddWithValue("schema", schema);
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public async Task<List<ColumnInfo>> GetColumnsAsync(string schema, string table)
    {
        var list = new List<ColumnInfo>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT column_name, data_type, is_nullable 
            FROM information_schema.columns 
            WHERE table_schema = @schema AND table_name = @table 
            ORDER BY ordinal_position";
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);
        
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ColumnInfo
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                IsNullable = reader.GetString(2) == "YES"
            });
        }
        
        var pks = await GetPrimaryKeysAsync(schema, table);
        foreach(var col in list)
        {
            if (pks.Contains(col.Name)) col.IsPrimaryKey = true;
        }

        return list;
    }

    public async Task<List<string>> GetPrimaryKeysAsync(string schema, string table)
    {
        var list = new List<string>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = @"
            SELECT kcu.column_name
            FROM information_schema.table_constraints tco
            JOIN information_schema.key_column_usage kcu 
              ON kcu.constraint_name = tco.constraint_name
              AND kcu.constraint_schema = tco.constraint_schema
            WHERE tco.constraint_type = 'PRIMARY KEY'
              AND kcu.table_schema = @schema
              AND kcu.table_name = @table
            ORDER BY kcu.ordinal_position";
        cmd.Parameters.AddWithValue("schema", schema);
        cmd.Parameters.AddWithValue("table", table);

        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(reader.GetString(0));
        }
        return list;
    }

    public async Task<DataTable> ExecuteQueryAsync(string sql, Dictionary<string, object>? parameters = null)
    {
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = sql;
        if (parameters != null)
        {
            foreach (var kvp in parameters)
            {
                cmd.Parameters.AddWithValue(kvp.Key, kvp.Value ?? DBNull.Value);
            }
        }

        var dt = new DataTable();
        using var reader = await cmd.ExecuteReaderAsync();
        dt.Load(reader);
        return dt;
    }

    public string QuoteIdentifier(string identifier)
    {
        return $"\"{identifier}\"";
    }

    public string FormatValue(object? value)
    {
        if (value == null || value == DBNull.Value) return "NULL";
        if (value is string || value is DateTime || value is Guid) return $"'{value.ToString()!.Replace("'", "''")}'";
        if (value is bool b) return b ? "TRUE" : "FALSE";
        return value.ToString()!;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
