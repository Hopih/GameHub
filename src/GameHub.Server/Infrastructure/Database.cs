using Npgsql;

namespace GameHub.Server.Infrastructure;

public class Database : IAsyncDisposable
{
    private readonly NpgsqlDataSource _dataSource;

    public Database(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _dataSource = NpgsqlDataSource.Create(connectionString);
    }

    public async Task<NpgsqlConnection> OpenConnectionAsync()
    {
        return await _dataSource.OpenConnectionAsync();
    }

    public async Task<bool> CheckConnectionAsync()
    {
        await using var connection = await OpenConnectionAsync();
        await using var command = new NpgsqlCommand("SELECT 1", connection);
        object? result = await command.ExecuteScalarAsync();

        return result is int value && value == 1;
    }

    public ValueTask DisposeAsync()
    {
        return _dataSource.DisposeAsync();
    }
}
