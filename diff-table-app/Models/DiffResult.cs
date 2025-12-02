namespace diff_table_app.Models;

public enum RowStatus
{
    Unchanged,
    Added,
    Deleted,
    Modified
}

public class DiffResult
{
    public List<DiffRow> Rows { get; set; } = new();
    public List<string> Columns { get; set; } = new();
    public int AddedCount => Rows.Count(r => r.Status == RowStatus.Added);
    public int DeletedCount => Rows.Count(r => r.Status == RowStatus.Deleted);
    public int ModifiedCount => Rows.Count(r => r.Status == RowStatus.Modified);
}

public class DiffRow
{
    public RowStatus Status { get; set; }
    public Dictionary<string, object?> KeyValues { get; set; } = new();
    public Dictionary<string, CellDiff> ColumnDiffs { get; set; } = new();
}

public class CellDiff
{
    public object? OldValue { get; set; }
    public object? NewValue { get; set; }
    public bool IsChanged { get; set; }
}
