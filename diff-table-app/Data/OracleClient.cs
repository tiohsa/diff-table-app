using System.Data;
using diff_table_app.Services.Interfaces;
using Oracle.ManagedDataAccess.Client;

namespace diff_table_app.Data;

public class OracleClient : IDatabaseClient
{
    private OracleConnection? _connection;

    public async Task ConnectAsync(string connectionString)
    {
        _connection = new OracleConnection(connectionString);
        await _connection.OpenAsync();
    }

    public async Task<List<string>> GetSchemasAsync()
    {
        var list = new List<string>();
        using var cmd = _connection!.CreateCommand();
        cmd.CommandText = "SELECT username FROM all_users ORDER BY username";
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
        cmd.CommandText = "SELECT table_name FROM all_tables WHERE owner = :schema ORDER BY table_name";
        cmd.Parameters.Add("schema", schema);
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
            SELECT column_name, data_type, nullable 
            FROM all_tab_columns 
            WHERE owner = :schema AND table_name = :table 
            ORDER BY column_id";
        cmd.Parameters.Add("schema", schema);
        cmd.Parameters.Add("table", table);
        
        using var reader = await cmd.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            list.Add(new ColumnInfo
            {
                Name = reader.GetString(0),
                DataType = reader.GetString(1),
                IsNullable = reader.GetString(2) == "Y"
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
            SELECT cols.column_name
            FROM all_constraints cons
            JOIN all_cons_columns cols ON cols.constraint_name = cons.constraint_name AND cols.owner = cons.owner
            WHERE cons.constraint_type = 'P'
              AND cons.owner = :schema
              AND cons.table_name = :table
            ORDER BY cols.position";
        cmd.Parameters.Add("schema", schema);
        cmd.Parameters.Add("table", table);

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
                cmd.Parameters.Add(kvp.Key, kvp.Value ?? DBNull.Value);
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
        if (value is string || value is Guid) return $"'{value.ToString()!.Replace("'", "''")}'";
        if (value is DateTime dt) return $"TO_DATE('{dt:yyyy-MM-dd HH:mm:ss}', 'YYYY-MM-DD HH24:MI:SS')";
        return value.ToString()!;
    }

    public void Dispose()
    {
        _connection?.Dispose();
    }
}
