using System.Text.Json.Serialization;

namespace ProactiveIntelligence.CoreAgent.Models;

public sealed record Insight(
    string Summary,
    [property: JsonPropertyName("critical_issues")] IReadOnlyList<string> CriticalIssues,
    [property: JsonPropertyName("root_causes")] IReadOnlyList<string> RootCauses,
    IReadOnlyList<string> Recommendations,
    [property: JsonPropertyName("data_sources")] IReadOnlyList<string>? DataSources);