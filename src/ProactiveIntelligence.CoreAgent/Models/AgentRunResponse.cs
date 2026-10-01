using System.Text.Json.Serialization;

namespace ProactiveIntelligence.CoreAgent.Models;

public sealed record AgentRunResponse(
    [property: JsonPropertyName("run_id")] string RunId,
    string Summary,
    [property: JsonPropertyName("critical_issues")] IReadOnlyList<string> CriticalIssues,
    [property: JsonPropertyName("root_causes")] IReadOnlyList<string> RootCauses,
    IReadOnlyList<string> Recommendations,
    [property: JsonPropertyName("data_sources")] IReadOnlyList<string> DataSources,
    [property: JsonPropertyName("tool_results")] IReadOnlyList<object?> ToolResults,
    [property: JsonPropertyName("generated_at")] DateTimeOffset GeneratedAt,
    string Status);