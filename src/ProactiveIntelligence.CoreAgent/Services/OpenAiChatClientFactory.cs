using System.Runtime.CompilerServices;
using System.ClientModel;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Options;
using OpenAI;

namespace ProactiveIntelligence.CoreAgent.Services;

public sealed class OpenAiChatClientFactory(IOptions<LlmOptions> options) : IChatClient
{
    private readonly IChatClient innerClient = CreateClient(options.Value);

    public Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default) =>
        innerClient.GetResponseAsync(messages, options, cancellationToken);

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        await foreach (var update in innerClient.GetStreamingResponseAsync(messages, options, cancellationToken))
        {
            yield return update;
        }
    }

    public object? GetService(Type serviceType, object? serviceKey = null) =>
        innerClient.GetService(serviceType, serviceKey);

    public void Dispose() => innerClient.Dispose();

    private static IChatClient CreateClient(LlmOptions options)
    {
        if (string.IsNullOrWhiteSpace(options.Endpoint)
            || string.IsNullOrWhiteSpace(options.ApiKey)
            || string.IsNullOrWhiteSpace(options.Model))
        {
            throw new InvalidOperationException("Llm:Endpoint, Llm:ApiKey, and Llm:Model must be configured.");
        }

        var client = new OpenAIClient(
            new ApiKeyCredential(options.ApiKey),
            new OpenAIClientOptions { Endpoint = new Uri(options.Endpoint) });

        return client.GetChatClient(options.Model).AsIChatClient();
    }
}