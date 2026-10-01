using Npgsql;
using Microsoft.Extensions.Options;

internal sealed class PostgresConnectionFactory(IOptions<DatabaseOptions> options)
{
    private readonly DatabaseOptions databaseOptions = options.Value;

    public int TimeoutSeconds => databaseOptions.TimeoutSeconds > 0 ? databaseOptions.TimeoutSeconds : 30;

    public async Task<NpgsqlConnection> OpenConnectionAsync(CancellationToken cancellationToken = default)
    {
        var connectionStringBuilder = new NpgsqlConnectionStringBuilder
        {
            Host = databaseOptions.Host,
            Port = databaseOptions.Port > 0 ? databaseOptions.Port : 5432,
            Database = databaseOptions.Name,
            Username = databaseOptions.User,
            Password = databaseOptions.Password,
            Timeout = TimeoutSeconds,
            CommandTimeout = TimeoutSeconds
        };

        var connection = new NpgsqlConnection(connectionStringBuilder.ConnectionString);

        try
        {
            await connection.OpenAsync(cancellationToken);

            await using var command = connection.CreateCommand();
            command.CommandText = $"SET statement_timeout = {TimeoutSeconds * 1000}";
            await command.ExecuteNonQueryAsync(cancellationToken);

            return connection;
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }
}