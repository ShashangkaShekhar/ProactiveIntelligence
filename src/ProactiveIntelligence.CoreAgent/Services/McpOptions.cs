namespace ProactiveIntelligence.CoreAgent.Services;

public sealed class McpOptions
{
    public string ServerUrl { get; set; } = "http://localhost:8080";

    public string AuthToken { get; set; } = string.Empty;
}