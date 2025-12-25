using System.ClientModel;
using Microsoft.Extensions.Options;
using OpenAI;
using OpenAI.Chat;
using DXO.Configuration;
using DXO.Services.Settings;
using DXO.Services.AzureAIFoundry;
using DXO.Services.Models;

namespace DXO.Services.OpenAI;

/// <summary>
/// Service for interacting with OpenAI API and delegating to other providers as needed
/// </summary>
public interface IOpenAIService
{
    /// <summary>
    /// Sends a chat completion request to the appropriate provider based on the model
    /// </summary>
    Task<ChatCompletionResponse> GetChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a chat completion response from the appropriate provider based on the model
    /// </summary>
    IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Checks if an API key is configured for the given model
    /// </summary>
    bool HasApiKey(string? model = null);
}

public class OpenAIService : IOpenAIService
{
    private readonly IModelProviderFactory _providerFactory;
    private readonly ILogger<OpenAIService> _logger;

    public OpenAIService(
        IModelProviderFactory providerFactory,
        ILogger<OpenAIService> logger)
    {
        _logger = logger;
        _providerFactory = providerFactory;
    }

    public bool HasApiKey(string? model = null)
    {
        Console.WriteLine($"[OpenAIService] HasApiKey called for model: {model}");
        
        if (string.IsNullOrEmpty(model))
        {
            Console.WriteLine($"[OpenAIService] Model name is null or empty, returning false");
            return false;
        }

        try
        {
            var providerService = _providerFactory.GetProviderServiceAsync(model).Result;
            var hasKey = providerService.HasApiKey(model);
            Console.WriteLine($"[OpenAIService] Provider service returned HasApiKey={hasKey} for model: {model}");
            return hasKey;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[OpenAIService] Exception checking API key for model '{model}': {ex.Message}");
            return false;
        }
    }

    public async Task<ChatCompletionResponse> GetChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Routing chat completion request for model {Model}", request.Model);
        
        var providerService = await _providerFactory.GetProviderServiceAsync(request.Model);
        return await providerService.GetChatCompletionAsync(request, cancellationToken);
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Routing streaming chat completion request for model {Model}", request.Model);
        
        var providerService = await _providerFactory.GetProviderServiceAsync(request.Model);
        
        await foreach (var chunk in providerService.StreamChatCompletionAsync(request, cancellationToken))
        {
            yield return chunk;
        }
    }
}

/// <summary>
/// Exception thrown when OpenAI API returns an error
/// </summary>
public class OpenAIException : Exception
{
    public string? ResponseContent { get; }

    public OpenAIException(string message, string? responseContent = null)
        : base(message)
    {
        ResponseContent = responseContent;
    }
}

#region Request/Response DTOs (shared between services)

public class ChatCompletionRequest
{
    public string Model { get; set; } = "gpt-4o";
    public List<ChatMessageDto> Messages { get; set; } = new();
    public double? Temperature { get; set; }
    public int? MaxTokens { get; set; }
    public double? TopP { get; set; }
    public double? PresencePenalty { get; set; }
    public double? FrequencyPenalty { get; set; }
    public bool? Stream { get; set; }
}

public class ChatMessageDto
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;

    public ChatMessageDto() { }

    public ChatMessageDto(string role, string content)
    {
        Role = role;
        Content = content;
    }

    public static ChatMessageDto System(string content) => new("system", content);
    public static ChatMessageDto User(string content) => new("user", content);
    public static ChatMessageDto Assistant(string content) => new("assistant", content);
}

public class ChatCompletionResponse
{
    public string Id { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<ChatChoice> Choices { get; set; } = new();
    public UsageInfo? Usage { get; set; }
}

public class ChatChoice
{
    public int Index { get; set; }
    public ChatMessageDto? Message { get; set; }
    public string? FinishReason { get; set; }
}

public class UsageInfo
{
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
    public int TotalTokens { get; set; }
}

public class ChatCompletionChunk
{
    public string Id { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public List<ChunkChoice> Choices { get; set; } = new();
}

public class ChunkChoice
{
    public int Index { get; set; }
    public ChunkDelta? Delta { get; set; }
    public string? FinishReason { get; set; }
}

public class ChunkDelta
{
    public string? Content { get; set; }
}

#endregion
