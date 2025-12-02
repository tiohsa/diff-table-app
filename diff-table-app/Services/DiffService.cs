using System.Data;
using System.Text;
using diff_table_app.Models;

namespace diff_table_app.Services;

public class DiffService
{
    public DiffResult Compare(DataTable source, DataTable target, List<string> keys, List<string> ignoreColumns)
    {
        var result = new DiffResult();
        
        // カラムリストの統合（両方にあるカラムのみ比較対象とするのが安全）
        var sourceCols = source.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetCols = target.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var commonCols = sourceCols.Intersect(targetCols).Where(c => !ignoreColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
        
        result.Columns = commonCols;

        // キーによるインデックス化
        var sourceMap = new Dictionary<string, DataRow>();
        var targetMap = new Dictionary<string, DataRow>();

        string GetKey(DataRow row)
        {
            var sb = new StringBuilder();
            foreach (var key in keys)
            {
                if (row.Table.Columns.Contains(key))
                    sb.Append(row[key]?.ToString() ?? "NULL").Append("|");
            }
            return sb.ToString();
        }

        foreach (DataRow row in source.Rows) sourceMap[GetKey(row)] = row;
        foreach (DataRow row in target.Rows) targetMap[GetKey(row)] = row;

        // Source -> Target (INSERT / UPDATE)
        foreach (var kvp in sourceMap)
        {
            var key = kvp.Key;
            var sRow = kvp.Value;

            if (!targetMap.TryGetValue(key, out var tRow))
            {
                // Targetにない -> Added
                var diffRow = new DiffRow { Status = RowStatus.Added };
                foreach (var k in keys) diffRow.KeyValues[k] = sRow[k];
                foreach (var col in commonCols)
                {
                    diffRow.ColumnDiffs[col] = new CellDiff { NewValue = sRow[col], IsChanged = true };
                }
                result.Rows.Add(diffRow);
            }
            else
            {
                // 両方ある -> 比較
                var diffRow = new DiffRow { Status = RowStatus.Unchanged };
                foreach (var k in keys) diffRow.KeyValues[k] = sRow[k];
                bool isModified = false;

                foreach (var col in commonCols)
                {
                    var sVal = sRow[col];
                    var tVal = tRow[col];
                    
                    if (!AreValuesEqual(sVal, tVal))
                    {
                        diffRow.ColumnDiffs[col] = new CellDiff { OldValue = tVal, NewValue = sVal, IsChanged = true };
                        isModified = true;
                    }
                    else
                    {
                         diffRow.ColumnDiffs[col] = new CellDiff { OldValue = tVal, NewValue = sVal, IsChanged = false };
                    }
                }

                if (isModified)
                {
                    diffRow.Status = RowStatus.Modified;
                    result.Rows.Add(diffRow);
                }
                else
                {
                    result.Rows.Add(diffRow);
                }
            }
        }

        // Target -> Source (DELETE)
        foreach (var kvp in targetMap)
        {
            if (!sourceMap.ContainsKey(kvp.Key))
            {
                var tRow = kvp.Value;
                var diffRow = new DiffRow { Status = RowStatus.Deleted };
                foreach (var k in keys) diffRow.KeyValues[k] = tRow[k];
                foreach (var col in commonCols)
                {
                    diffRow.ColumnDiffs[col] = new CellDiff { OldValue = tRow[col], IsChanged = true };
                }
                result.Rows.Add(diffRow);
            }
        }

        return result;
    }

    private bool AreValuesEqual(object? v1, object? v2)
    {
        if (v1 == DBNull.Value) v1 = null;
        if (v2 == DBNull.Value) v2 = null;
        if (v1 == null && v2 == null) return true;
        if (v1 == null || v2 == null) return false;

        // 型が違う場合の簡易比較（文字列化）
        if (v1.GetType() != v2.GetType())
        {
            // 数値系
            if (IsNumeric(v1) && IsNumeric(v2))
            {
                try {
                    return Convert.ToDecimal(v1) == Convert.ToDecimal(v2);
                } catch {
                    return v1.ToString() == v2.ToString();
                }
            }
            return v1.ToString() == v2.ToString();
        }

        return v1.Equals(v2);
    }

    private bool IsNumeric(object obj) => obj is int || obj is long || obj is float || obj is double || obj is decimal;
}
