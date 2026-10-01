using System.Text.Json;
using System.Collections;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using ProactiveIntelligence.CoreAgent.Models;

namespace ProactiveIntelligence.CoreAgent.Services;

public sealed class AgentOrchestrator(
    IMcpClient mcpClient,
    IChatClient chatClient,
    IOptions<AgentOptions> options,
    ILogger<AgentOrchestrator> logger) : IAgentOrchestrator
{
    private const int MaximumToolIterations = 5;
    private readonly AgentOptions agentOptions = options.Value;

    public async Task<AgentRunResponse> RunAsync(
        AgentRunRequest request,
        string runId,
        CancellationToken cancellationToken = default)
    {
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(TimeSpan.FromSeconds(GetTimeoutSeconds()));

        try
        {
            return await RunCoreAsync(request, runId, timeoutSource.Token);
        }
        catch (OperationCanceledException) when (timeoutSource.IsCancellationRequested)
        {
            logger.LogWarning("Agent run {RunId} reached its timeout.", runId);
            return CreateFailureResponse(runId, "Agent run timed out before the LLM produced a final insight.");
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Agent run {RunId} failed during LLM orchestration.", runId);
            return CreateFailureResponse(runId, "The LLM orchestration failed.");
        }
    }

    private async Task<AgentRunResponse> RunCoreAsync(
        AgentRunRequest request,
        string runId,
        CancellationToken cancellationToken)
    {
        var messages = new List<ChatMessage>
        {
            new(ChatRole.System, SystemPrompt),
            new(ChatRole.User, BuildUserPrompt(request))
        };
        var toolResults = new List<object?>();
        var recordedCallIds = new HashSet<string>(StringComparer.Ordinal);
        var invokedToolNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var chatOptions = new ChatOptions
        {
            Tools = mcpClient.Tools.Cast<AITool>().ToList()
        };

        for (var parseAttempt = 0; parseAttempt < 2; parseAttempt++)
        {
            logger.LogInformation(
                "Agent run {RunId} LLM attempt {Attempt}: sending prompt with {ToolCount} tools.",
                runId,
                parseAttempt + 1,
                mcpClient.Tools.Count);

            var finalText = await RunToolCallingLoopAsync(
                messages,
                chatOptions,
                toolResults,
                recordedCallIds,
                invokedToolNames,
                runId,
                cancellationToken);

            if (TryParseInsight(finalText, out var insight))
            {
                logger.LogInformation("Agent run {RunId} produced a valid structured insight.", runId);
                return CreateSuccessResponse(runId, insight!, invokedToolNames, toolResults);
            }

            if (parseAttempt == 0)
            {
                logger.LogWarning("Agent run {RunId} returned malformed insight JSON; requesting a correction.", runId);
                messages.Add(new ChatMessage(
                    ChatRole.User,
                    "Your previous response was not valid JSON. Return ONLY the JSON object. "
                    + "No markdown. No prose. Use this exact schema: "
                    + "{\"summary\":\"string\",\"critical_issues\":[\"string\"],\"root_causes\":[\"string\"],\"recommendations\":[\"string\"],\"data_sources\":[\"string\"]}."));
            }
        }

        logger.LogError("Agent run {RunId} returned malformed insight JSON after one correction retry.", runId);
        return CreateFailureResponse(runId, "The LLM returned malformed structured insight data.", invokedToolNames, toolResults);
    }

    private async Task<string> RunToolCallingLoopAsync(
        List<ChatMessage> messages,
        ChatOptions chatOptions,
        List<object?> toolResults,
        ISet<string> recordedCallIds,
        ISet<string> invokedToolNames,
        string runId,
        CancellationToken cancellationToken)
    {
        for (var iteration = 1; iteration <= MaximumToolIterations; iteration++)
        {
            logger.LogInformation("Agent run {RunId} tool loop iteration {Iteration}.", runId, iteration);
            var response = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
            messages.AddRange(response.Messages);

            var functionCalls = response.Messages
                .SelectMany(message => message.Contents)
                .OfType<FunctionCallContent>()
                .ToArray();

            if (functionCalls.Length == 0)
            {
                return string.Concat(response.Messages
                    .SelectMany(message => message.Contents)
                    .OfType<TextContent>()
                    .Select(content => content.Text));
            }

            foreach (var functionCall in functionCalls)
            {
                var function = mcpClient.Tools.FirstOrDefault(tool =>
                    string.Equals(tool.Name, functionCall.Name, StringComparison.OrdinalIgnoreCase));
                var functionName = function?.Name ?? functionCall.Name;
                invokedToolNames.Add(functionName);
                object? result = function is null
                    ? $"MCP tool '{functionCall.Name}' was not found."
                    : await function.InvokeAsync(new AIFunctionArguments(functionCall.Arguments), cancellationToken);

                if (recordedCallIds.Add(functionCall.CallId))
                {
                    toolResults.Add(ExtractRawToolResult(result));
                }
                logger.LogInformation(
                    "Agent run {RunId} executed LLM-requested MCP tool {ToolName}.",
                    runId,
                    functionName);
                messages.Add(new ChatMessage(
                    ChatRole.Tool,
                    [new FunctionResultContent(functionCall.CallId, result)]));

                // Detect the "missing sql argument" error and correct the LLM.
                var resultText = result?.ToString() ?? string.Empty;
                if (resultText.Contains("requires a 'sql' argument", StringComparison.OrdinalIgnoreCase))
                {
                    logger.LogWarning(
                        "Agent run {RunId} detected bad run_sql arguments; injecting correction.",
                        runId);
                    messages.Add(new ChatMessage(
                        ChatRole.User,
                        "run_sql takes ONE argument named 'sql' whose value is a SQL SELECT string. "
                        + "Do NOT pass summary, critical_issues, root_causes, recommendations, or "
                        + "data_sources to run_sql. Example: run_sql({ \"sql\": \"SELECT * FROM device LIMIT 5\" })"));
                }
            }
        }

        logger.LogWarning(
            "Agent run {RunId} reached tool-loop cap ({Max}); forcing final answer.",
            runId, MaximumToolIterations);
        messages.Add(new ChatMessage(
            ChatRole.User,
            "Stop calling tools. You have enough information. Return ONLY the final JSON insight now. "
            + "No markdown. No prose. Schema: "
            + "{\"summary\":\"string\",\"critical_issues\":[\"string\"],\"root_causes\":[\"string\"],\"recommendations\":[\"string\"],\"data_sources\":[\"string\"]}."));

        var finalResponse = await chatClient.GetResponseAsync(messages, chatOptions, cancellationToken);
        messages.AddRange(finalResponse.Messages);

        return string.Concat(finalResponse.Messages
            .SelectMany(message => message.Contents)
            .OfType<TextContent>()
            .Select(content => content.Text));
    }

    private static bool TryParseInsight(string responseText, out Insight? insight)
    {
        insight = null;
        var json = responseText.Trim();

        // Strip markdown code fences if present.
        if (json.StartsWith("```") && json.EndsWith("```"))
        {
            json = json[3..^3].Trim();
            if (json.StartsWith("json", StringComparison.OrdinalIgnoreCase))
            {
                json = json[4..].Trim();
            }
        }

        // Trim any leading/trailing prose around the JSON object.
        var firstBrace = json.IndexOf('{');
        var lastBrace = json.LastIndexOf('}');
        if (firstBrace >= 0 && lastBrace > firstBrace)
        {
            json = json[firstBrace..(lastBrace + 1)];
        }

        try
        {
            insight = JsonSerializer.Deserialize<Insight>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            return insight is not null
                && !string.IsNullOrWhiteSpace(insight.Summary)
                && insight.CriticalIssues is not null
                && insight.RootCauses is not null
                && insight.Recommendations is not null;
        }
        catch (JsonException)
        {
            return false;
        }
    }

    private static string BuildUserPrompt(AgentRunRequest request) =>
        string.IsNullOrWhiteSpace(request.Context)
            ? request.Instruction
            : $"Instruction: {request.Instruction}\nContext: {request.Context}";

    private int GetTimeoutSeconds() =>
        agentOptions.TimeoutSeconds > 0 ? agentOptions.TimeoutSeconds : 120;

    private static AgentRunResponse CreateSuccessResponse(
        string runId,
        Insight insight,
        IEnumerable<string> invokedToolNames,
        IReadOnlyList<object?> toolResults) => new(
            RunId: runId,
            Summary: insight.Summary,
            CriticalIssues: EnsureValues(insight.CriticalIssues, "No critical issues were identified from the available data."),
            RootCauses: EnsureValues(insight.RootCauses, "No root cause was established from the available data."),
            Recommendations: EnsureValues(insight.Recommendations, "Continue monitoring the available data sources for meaningful changes."),
            DataSources: invokedToolNames.ToArray(),
            ToolResults: toolResults,
            GeneratedAt: DateTimeOffset.UtcNow,
            Status: "success");

    private static AgentRunResponse CreateFailureResponse(
        string runId,
        string summary,
        IEnumerable<string>? dataSources = null,
        IReadOnlyList<object?>? toolResults = null) => new(
            RunId: runId,
            Summary: summary,
            CriticalIssues: [],
            RootCauses: [],
            Recommendations: [],
            DataSources: dataSources?.ToArray() ?? [],
            ToolResults: toolResults ?? [],
            GeneratedAt: DateTimeOffset.UtcNow,
            Status: "failed");

    private static object? ExtractRawToolResult(object? result)
    {
        if (result is null)
        {
            return null;
        }

        var content = result.GetType().GetProperty("Content")?.GetValue(result) as IEnumerable;
        if (content is null)
        {
            return result;
        }

        var textValues = content
            .Cast<object>()
            .Select(item => item.GetType().GetProperty("Text")?.GetValue(item) as string)
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .ToArray();

        if (textValues.Length == 1)
        {
            try
            {
                using var document = JsonDocument.Parse(textValues[0]!);
                return document.RootElement.Clone();
            }
            catch (JsonException)
            {
                return textValues[0];
            }
        }

        return textValues;
    }

    private static IReadOnlyList<string> EnsureValues(
        IReadOnlyList<string>? values,
        string fallback) =>
        values is { Count: > 0 } ? values : [fallback];

    private const string SystemPrompt = """
        You are a proactive intelligence agent. You have access to MCP tools through function calling.
        Use the available tools to answer the user's instruction and retrieve relevant data before analyzing it.
        Count exact values from tool results. Never approximate, infer, or hallucinate counts.
        Populate data_sources with the names of every MCP tool you invoked.
        Populate critical_issues, root_causes, and recommendations even for exploratory queries.
        If no issue or root cause is established, say so explicitly in a non-empty array.

        CRITICAL RULES FOR TOOL CALLS:
        - run_sql takes exactly ONE argument named "sql", whose value is a SQL SELECT string.
        - NEVER pass summary, critical_issues, root_causes, recommendations,
          or data_sources to run_sql. Those fields belong ONLY in the final JSON.
        - When you have enough data to answer the instruction, STOP calling tools
          and return the final structured JSON insight.

        WORKED EXAMPLE:
          Tool call:  run_sql({ "sql": "SELECT * FROM device LIMIT 5" })
          Final JSON: { "summary": "...", "critical_issues": ["..."],
                        "root_causes": ["..."], "recommendations": ["..."],
                        "data_sources": ["run_sql"] }

        Return only a structured JSON insight with exactly these fields:
        summary (string), critical_issues (array of strings), root_causes (array of strings),
        recommendations (array of strings), and data_sources (array of strings).
        Do not wrap the JSON in Markdown.
        """;
}