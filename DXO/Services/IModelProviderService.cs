using DXO.Services.OpenAI;

namespace DXO.Services;

/// <summary>
/// Interface that all model provider services must implement
/// </summary>
public interface IModelProviderService
{
    /// <summary>
    /// Sends a chat completion request to the provider
    /// </summary>
    Task<ChatCompletionResponse> GetChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a chat completion response from the provider
    /// </summary>
    IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an API key is configured for the given model
    /// </summary>
    bool HasApiKey(string model);
}
