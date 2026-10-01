using System.Net.Http.Headers;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ModelContextProtocol.Client;

namespace ProactiveIntelligence.CoreAgent.Services;

public sealed class HttpMcpClient(
    IOptions<McpOptions> options,
    ILoggerFactory loggerFactory) : IMcpClient, IAsyncDisposable
{
    private readonly McpOptions mcpOptions = options.Value;
    private readonly ILogger<HttpMcpClient> logger = loggerFactory.CreateLogger<HttpMcpClient>();
    private readonly object syncLock = new();
    private McpClient? client;
    private HttpClient? httpClient;
    private IReadOnlyList<string> toolNames = [];
    private IReadOnlyList<AIFunction> tools = [];

    public IReadOnlyList<string> ToolNames
    {
        get
        {
            lock (syncLock)
            {
                return toolNames;
            }
        }
    }

    public IReadOnlyList<AIFunction> Tools
    {
        get
        {
            lock (syncLock)
            {
                return tools;
            }
        }
    }

    public async Task DiscoverToolsAsync(CancellationToken cancellationToken = default)
    {
        var serverUri = new Uri(mcpOptions.ServerUrl);
        var discoveredHttpClient = new HttpClient();
        try
        {
            if (!string.IsNullOrWhiteSpace(mcpOptions.AuthToken))
            {
                discoveredHttpClient.DefaultRequestHeaders.Authorization =
                    new AuthenticationHeaderValue("Bearer", mcpOptions.AuthToken);
            }

            var transportOptions = new HttpClientTransportOptions
            {
                Endpoint = serverUri
            };
            var transport = new HttpClientTransport(transportOptions, discoveredHttpClient, loggerFactory, ownsHttpClient: false);
            var discoveredClient = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
            var discoveredTools = await discoveredClient.ListToolsAsync(cancellationToken: cancellationToken);
            var functions = discoveredTools
                .Select(CreateAIFunction)
                .ToArray();

            lock (syncLock)
            {
                client = discoveredClient;
                httpClient = discoveredHttpClient;
                toolNames = discoveredTools.Select(tool => tool.Name).ToArray();
                tools = functions;
            }
        }
        catch
        {
            discoveredHttpClient.Dispose();
            throw;
        }

        logger.LogInformation(
            "Discovered {ToolCount} MCP tools from {ServerUrl}: {ToolNames}.",
            toolNames.Count,
            serverUri,
            string.Join(", ", toolNames));
    }

    public async ValueTask DisposeAsync()
    {
        if (client is not null)
        {
            await client.DisposeAsync();
        }

        httpClient?.Dispose();
    }

    private AIFunction CreateAIFunction(McpClientTool tool)
    {
        return AIFunctionFactory.Create(
            async (AIFunctionArguments arguments, CancellationToken cancellationToken) =>
            {
                var activeClient = client ?? throw new InvalidOperationException("MCP client is not connected.");
                var result = await activeClient.CallToolAsync(
                    tool.Name,
                    arguments.ToDictionary(argument => argument.Key, argument => argument.Value),
                    cancellationToken: cancellationToken);
                return result;
            },
            tool.Name,
            tool.Description);
    }
}