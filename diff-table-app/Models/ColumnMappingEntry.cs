using CommunityToolkit.Mvvm.ComponentModel;

namespace diff_table_app.Models;

public partial class ColumnMappingEntry : ObservableObject
{
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceColumnDisplay))]
    private string _sourceColumn = string.Empty;

    [ObservableProperty] private string _targetColumn = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceColumnDisplay))]
    private string _sourceDataType = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SourceColumnDisplay))]
    private bool _isPrimaryKey;

    public string SourceColumnDisplay
    {
        get
        {
            var pk = IsPrimaryKey ? "[PK] " : "";
            var type = string.IsNullOrEmpty(SourceDataType) ? "" : $" ({SourceDataType})";
            return $"{pk}{SourceColumn}{type}";
        }
    }
}
