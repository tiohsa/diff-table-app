using System.Data;
using System.Text;
using diff_table_app.Models;

namespace diff_table_app.Services;

public class DiffService
{
    public DiffResult Compare(DataTable source, DataTable target, List<string> keys, List<string> ignoreColumns, Dictionary<string, string>? columnMapping = null)
    {
        var result = new DiffResult();
        
        // カラムリストの統合
        // マッピングがある場合: Sourceのカラム名を基準にし、Targetのカラムはマッピングに従って取得する
        // マッピングがない場合: 共通のカラム名を使用

        var sourceCols = source.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var targetCols = target.Columns.Cast<DataColumn>().Select(c => c.ColumnName).ToHashSet(StringComparer.OrdinalIgnoreCase);
        
        List<string> compareCols = new List<string>();
        Dictionary<string, string> effectiveMapping = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        if (columnMapping != null && columnMapping.Count > 0)
        {
            foreach (var kvp in columnMapping)
            {
                var sCol = kvp.Key;
                var tCol = kvp.Value;

                if (sourceCols.Contains(sCol) && targetCols.Contains(tCol))
                {
                    if (!ignoreColumns.Contains(sCol, StringComparer.OrdinalIgnoreCase))
                    {
                        compareCols.Add(sCol);
                        effectiveMapping[sCol] = tCol;
                    }
                }
            }
            // マッピングに含まれていないが共通の名前を持つカラムも含めるべきか？
            // 要件としては「マッピング機能を追加」なので、マッピング指定時はマッピングのみ、あるいはマッピング優先で他も含むなどが考えられる。
            // ここではシンプルに「マッピングが指定されたらマッピングされたものだけ比較」とするか、「マッピング + 共通名」とするか。
            // 通常、マッピングを使うときはスキーマが違うので、明示的なものだけにするのが安全。
            // しかし、一部だけ名前が違い、他は同じというケースも多い。
            // ユーザーフレンドリーにするため、「マッピング指定があるものはそれに従い、ないものは同名比較」とする。

            foreach (var sCol in sourceCols)
            {
                if (effectiveMapping.ContainsKey(sCol)) continue; // すでにマッピング済み

                if (targetCols.Contains(sCol) && !ignoreColumns.Contains(sCol, StringComparer.OrdinalIgnoreCase))
                {
                    compareCols.Add(sCol);
                    effectiveMapping[sCol] = sCol;
                }
            }
        }
        else
        {
            compareCols = sourceCols.Intersect(targetCols).Where(c => !ignoreColumns.Contains(c, StringComparer.OrdinalIgnoreCase)).ToList();
            foreach (var col in compareCols) effectiveMapping[col] = col;
        }

        result.Columns = compareCols;

        // キーによるインデックス化
        var sourceMap = new Dictionary<string, DataRow>();
        var targetMap = new Dictionary<string, DataRow>();

        string GetKey(DataRow row, bool isSource)
        {
            var sb = new StringBuilder();
            foreach (var key in keys)
            {
                // Sourceの場合はそのままKey、Targetの場合はMappingを使ってKeyに対応するTargetカラムを探す必要がある
                // ただし、Keysは通常Source側のカラム名で指定される前提
                string colName = key;
                if (!isSource)
                {
                    // Targetの場合、SourceのKeyに対応するTargetのカラム名を取得
                    if (effectiveMapping.TryGetValue(key, out var mappedName))
                    {
                        colName = mappedName;
                    }
                    else
                    {
                        // マッピングにない場合、同名とみなす
                        colName = key;
                    }
                }

                if (row.Table.Columns.Contains(colName))
                    sb.Append(row[colName]?.ToString() ?? "NULL").Append("|");
            }
            return sb.ToString();
        }

        foreach (DataRow row in source.Rows) sourceMap[GetKey(row, true)] = row;
        foreach (DataRow row in target.Rows) targetMap[GetKey(row, false)] = row;

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
                foreach (var col in compareCols)
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

                foreach (var col in compareCols)
                {
                    var targetColName = effectiveMapping[col];

                    var sVal = sRow[col];
                    var tVal = tRow[targetColName];
                    
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

                // KeyValuesはSource基準のカラム名で入れる必要がある
                // しかし、Deleted行はTargetにしかない。
                // Keyに対応するTargetカラムの値を取得してセットする
                foreach (var k in keys)
                {
                     var targetColName = effectiveMapping.ContainsKey(k) ? effectiveMapping[k] : k;
                     if (tRow.Table.Columns.Contains(targetColName))
                        diffRow.KeyValues[k] = tRow[targetColName];
                }

                foreach (var col in compareCols)
                {
                    var targetColName = effectiveMapping[col];
                    diffRow.ColumnDiffs[col] = new CellDiff { OldValue = tRow[targetColName], IsChanged = true };
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
