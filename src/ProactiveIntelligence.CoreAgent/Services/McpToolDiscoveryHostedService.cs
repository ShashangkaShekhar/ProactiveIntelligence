namespace ProactiveIntelligence.CoreAgent.Services;

public sealed class McpToolDiscoveryHostedService(
    IMcpClient mcpClient,
    ILogger<McpToolDiscoveryHostedService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            await mcpClient.DiscoverToolsAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "MCP server tool discovery failed; CoreAgent will continue without MCP tools.");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}