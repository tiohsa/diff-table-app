using System.Text;
using diff_table_app.Models;
using diff_table_app.Services.Interfaces;

namespace diff_table_app.Services;

public class SqlGeneratorService
{
    public string GenerateSql(DiffResult result, string targetTableName, IDatabaseClient targetClient, List<string> keys)
    {
        var sb = new StringBuilder();

        foreach (var row in result.Rows)
        {
            if (row.Status == RowStatus.Unchanged) continue;

            if (row.Status == RowStatus.Added)
            {
                // INSERT
                var cols = new List<string>();
                var vals = new List<string>();
                foreach (var kvp in row.ColumnDiffs)
                {
                    cols.Add(targetClient.QuoteIdentifier(kvp.Key));
                    vals.Add(targetClient.FormatValue(kvp.Value.NewValue));
                }
                sb.AppendLine($"INSERT INTO {targetTableName} ({string.Join(", ", cols)}) VALUES ({string.Join(", ", vals)});");
            }
            else if (row.Status == RowStatus.Deleted)
            {
                // DELETE
                var where = new List<string>();
                foreach (var k in keys)
                {
                    var val = row.KeyValues[k];
                    where.Add($"{targetClient.QuoteIdentifier(k)} = {targetClient.FormatValue(val)}");
                }
                sb.AppendLine($"DELETE FROM {targetTableName} WHERE {string.Join(" AND ", where)};");
            }
            else if (row.Status == RowStatus.Modified)
            {
                // UPDATE
                var set = new List<string>();
                foreach (var kvp in row.ColumnDiffs)
                {
                    if (kvp.Value.IsChanged)
                    {
                        set.Add($"{targetClient.QuoteIdentifier(kvp.Key)} = {targetClient.FormatValue(kvp.Value.NewValue)}");
                    }
                }
                
                var where = new List<string>();
                foreach (var k in keys)
                {
                    var val = row.KeyValues[k];
                    where.Add($"{targetClient.QuoteIdentifier(k)} = {targetClient.FormatValue(val)}");
                }
                
                if (set.Count > 0)
                {
                    sb.AppendLine($"UPDATE {targetTableName} SET {string.Join(", ", set)} WHERE {string.Join(" AND ", where)};");
                }
            }
        }

        return sb.ToString();
    }
}
