using Xunit;
using diff_table_app.Services;
using System.Data;
using System.Collections.Generic;
using diff_table_app.Models;

namespace diff_table_app.Tests;

public class DiffServiceTests
{
    private DiffService _service = new();

    [Fact]
    public void Compare_WithMapping_ShouldMapColumns()
    {
        // Arrange
        var source = new DataTable("Source");
        source.Columns.Add("ID", typeof(int));
        source.Columns.Add("Name_S", typeof(string));
        source.Rows.Add(1, "Alice");

        var target = new DataTable("Target");
        target.Columns.Add("ID", typeof(int));
        target.Columns.Add("Name_T", typeof(string));
        target.Rows.Add(1, "Alice");

        var mapping = new Dictionary<string, string>
        {
            { "Name_S", "Name_T" }
        };

        // Act
        var result = _service.Compare(source, target, new List<string> { "ID" }, new List<string>(), mapping);

        // Assert
        Assert.Single(result.Rows);
        Assert.Equal(RowStatus.Unchanged, result.Rows[0].Status);
        Assert.Contains("Name_S", result.Columns);
        // Name_S should be used as column name in result, mapped from Name_T
    }

    [Fact]
    public void Compare_WithMapping_ShouldDetectDiff()
    {
        // Arrange
        var source = new DataTable("Source");
        source.Columns.Add("ID", typeof(int));
        source.Columns.Add("Name_S", typeof(string));
        source.Rows.Add(1, "Alice");

        var target = new DataTable("Target");
        target.Columns.Add("ID", typeof(int));
        target.Columns.Add("Name_T", typeof(string));
        target.Rows.Add(1, "Bob");

        var mapping = new Dictionary<string, string>
        {
            { "Name_S", "Name_T" }
        };

        // Act
        var result = _service.Compare(source, target, new List<string> { "ID" }, new List<string>(), mapping);

        // Assert
        Assert.Single(result.Rows);
        Assert.Equal(RowStatus.Modified, result.Rows[0].Status);

        var cellDiff = result.Rows[0].ColumnDiffs["Name_S"];
        Assert.True(cellDiff.IsChanged);
        Assert.Equal("Bob", cellDiff.OldValue);
        Assert.Equal("Alice", cellDiff.NewValue);
    }
}
