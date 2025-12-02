using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using diff_table_app.Data;
using diff_table_app.Services.Interfaces;
using System.Windows;

namespace diff_table_app.ViewModels;

public enum DatabaseType
{
    PostgreSQL,
    Oracle
}

public partial class ConnectionViewModel : ObservableObject
{
    [ObservableProperty] private DatabaseType _selectedType;
    [ObservableProperty] private string _host = "localhost";
    [ObservableProperty] private int _port = 5432;
    [ObservableProperty] private string _user = "postgres";
    [ObservableProperty] private string _password = "";
    [ObservableProperty] private string _database = "postgres";
    
    // Oracle用 (ServiceName or SID)
    [ObservableProperty] private string _serviceName = "ORCL";

    public IDatabaseClient CreateClient()
    {
        if (SelectedType == DatabaseType.PostgreSQL)
        {
            return new PostgresClient();
        }
        else
        {
            return new OracleClient();
        }
    }

    public string GetConnectionString()
    {
        if (SelectedType == DatabaseType.PostgreSQL)
        {
            return $"Host={Host};Port={Port};Username={User};Password={Password};Database={Database}";
        }
        else
        {
            // Oracle Connection String (簡易版)
            return $"User Id={User};Password={Password};Data Source={Host}:{Port}/{ServiceName}";
        }
    }

    [RelayCommand]
    private async Task TestConnection()
    {
        try
        {
            using var client = CreateClient();
            await client.ConnectAsync(GetConnectionString());
            MessageBox.Show("接続成功", "Info", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"接続失敗: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }
}
