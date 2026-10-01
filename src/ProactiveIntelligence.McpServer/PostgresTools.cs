using System.ComponentModel;
using System.Data.Common;
using ModelContextProtocol.Server;
using Npgsql;

[McpServerToolType]
internal sealed class PostgresTools(
    PostgresConnectionFactory connectionFactory,
    ILogger<PostgresTools> logger)
{
    [McpServerTool, Description("Execute a read-only SQL SELECT statement and return the result rows.")]
    public async Task<object> RunSql(
        [Description("A SQL statement that must begin with SELECT.")] string sql,
        CancellationToken cancellationToken = default)
    {
        if (!SqlQueryValidator.IsSelectStatement(sql))
        {
            return "Only SELECT statements are allowed.";
        }

        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var rows = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                rows.Add(MapRow(reader));
            }

            return rows;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "run_sql failed.");
            return exception.Message;
        }
    }

    [McpServerTool, Description("Return all base tables in the public PostgreSQL schema.")]
    public async Task<object> ListTables(CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'public'
              AND table_type = 'BASE TABLE'
            ORDER BY table_name;
            """;

        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var tables = new List<string>();
            while (await reader.ReadAsync(cancellationToken))
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "list_tables failed.");
            return exception.Message;
        }
    }

    [McpServerTool, Description("Describe the columns of a table in the public PostgreSQL schema.")]
    public async Task<object> DescribeTable(
        [Description("The table name in the public schema.")] string tableName,
        CancellationToken cancellationToken = default)
    {
        const string sql = """
            SELECT column_name, data_type, is_nullable
            FROM information_schema.columns
            WHERE table_schema = 'public'
              AND table_name = @table_name
            ORDER BY ordinal_position;
            """;

        try
        {
            await using var connection = await connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = new NpgsqlCommand(sql, connection);
            command.Parameters.AddWithValue("table_name", tableName);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);

            var columns = new List<Dictionary<string, object?>>();
            while (await reader.ReadAsync(cancellationToken))
            {
                columns.Add(MapColumn(reader));
            }

            return columns;
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "describe_table failed for {TableName}.", tableName);
            return exception.Message;
        }
    }

    internal static Dictionary<string, object?> MapRow(DbDataReader reader)
    {
        var row = new Dictionary<string, object?>(reader.FieldCount, StringComparer.OrdinalIgnoreCase);
        for (var columnIndex = 0; columnIndex < reader.FieldCount; columnIndex++)
        {
            row[reader.GetName(columnIndex)] = reader.IsDBNull(columnIndex)
                ? null
                : reader.GetValue(columnIndex);
        }

        return row;
    }

    internal static Dictionary<string, object?> MapColumn(DbDataReader reader) => new()
    {
        ["column_name"] = reader.GetString(0),
        ["data_type"] = reader.GetString(1),
        ["is_nullable"] = reader.GetString(2)
    };
}