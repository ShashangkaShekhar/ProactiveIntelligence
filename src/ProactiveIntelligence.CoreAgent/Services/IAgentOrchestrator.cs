using ProactiveIntelligence.CoreAgent.Models;

namespace ProactiveIntelligence.CoreAgent.Services;

public interface IAgentOrchestrator
{
    Task<AgentRunResponse> RunAsync(
        AgentRunRequest request,
        string runId,
        CancellationToken cancellationToken = default);
}