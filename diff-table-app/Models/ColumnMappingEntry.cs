using CommunityToolkit.Mvvm.ComponentModel;

namespace diff_table_app.Models;

public partial class ColumnMappingEntry : ObservableObject
{
    [ObservableProperty] private string _sourceColumn = string.Empty;
    [ObservableProperty] private string _targetColumn = string.Empty;
}
