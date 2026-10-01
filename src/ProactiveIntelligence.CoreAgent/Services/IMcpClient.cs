using Microsoft.Extensions.AI;

namespace ProactiveIntelligence.CoreAgent.Services;

public interface IMcpClient
{
    IReadOnlyList<string> ToolNames { get; }

    IReadOnlyList<AIFunction> Tools { get; }

    Task DiscoverToolsAsync(CancellationToken cancellationToken = default);
}