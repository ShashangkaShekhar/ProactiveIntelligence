using ModelContextProtocol.AspNetCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((context, _, loggerConfiguration) => loggerConfiguration
    .ReadFrom.Configuration(context.Configuration)
    .WriteTo.Console());

builder.Services.Configure<DatabaseOptions>(builder.Configuration.GetSection("Database"));
builder.Services.Configure<McpOptions>(builder.Configuration.GetSection("Mcp"));
builder.Services.AddSingleton<PostgresConnectionFactory>();
builder.Services.AddMcpServer()
    .WithHttpTransport()
    .WithTools<PostgresTools>();

var httpPort = builder.Configuration.GetValue<int?>("Mcp:HttpPort") ?? 8080;
builder.WebHost.ConfigureKestrel(options => options.ListenAnyIP(httpPort));

var app = builder.Build();

var databaseLogger = app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("DatabaseStartup");
var connectionFactory = app.Services.GetRequiredService<PostgresConnectionFactory>();

try
{
    await using var connection = await connectionFactory.OpenConnectionAsync();
    databaseLogger.LogInformation(
        "Database connection succeeded for {DatabaseName}; statement timeout configured to {TimeoutSeconds} seconds.",
        connection.Database,
        connectionFactory.TimeoutSeconds);
}
catch (Exception exception)
{
    databaseLogger.LogError(exception, "Database connection failed during startup.");
}

app.UseMiddleware<McpAuthenticationMiddleware>();
app.UseMiddleware<McpToolArgumentMiddleware>();
app.MapGet("/health", () => Results.Ok());
app.MapMcp();

app.Run();

public partial class Program;

internal sealed class DatabaseOptions
{
    public string? Host { get; set; }
    public int Port { get; set; }
    public string? Name { get; set; }
    public string? User { get; set; }
    public string? Password { get; set; }
    public int TimeoutSeconds { get; set; } = 30;
}

internal sealed class McpOptions
{
    public int HttpPort { get; set; }
    public string? AuthToken { get; set; }
}
