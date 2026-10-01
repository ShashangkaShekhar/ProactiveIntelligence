using Microsoft.AspNetCore.Mvc;
using ProactiveIntelligence.CoreAgent.Models;
using ProactiveIntelligence.CoreAgent.Services;

namespace ProactiveIntelligence.CoreAgent.Controllers;

[ApiController]
[Route("api/agent")]
public sealed class AgentController(ILogger<AgentController> logger) : ControllerBase
{
    [HttpGet("tools")]
    public ActionResult<IReadOnlyList<string>> Tools([FromServices] IMcpClient mcpClient) =>
        Ok(mcpClient.ToolNames);

    [HttpPost("run")]
    public async Task<ActionResult<AgentRunResponse>> Run(
        AgentRunRequest request,
        [FromServices] IAgentOrchestrator orchestrator,
        CancellationToken cancellationToken)
    {
        var runId = Guid.NewGuid().ToString("N");
        logger.LogInformation("Accepted agent run {RunId}.", runId);

        return Ok(await orchestrator.RunAsync(request, runId, cancellationToken));
    }
}