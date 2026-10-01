using System.ComponentModel.DataAnnotations;

namespace ProactiveIntelligence.CoreAgent.Models;

public sealed record AgentRunRequest(
    [param: Required] string Instruction,
    string? Context,
    string? RequestedBy,
    string? Delivery);