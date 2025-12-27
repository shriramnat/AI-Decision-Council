using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using DXO.Configuration;
using DXO.Services.OpenAI;
using DXO.Services.Models;

namespace DXO.Services.AzureAIFoundry;

/// <summary>
/// Service for interacting with Azure AI Foundry models (DeepSeek, etc.)
/// </summary>
public interface IAzureAIFoundryService
{
    /// <summary>
    /// Checks if an API key is configured for the given model
    /// </summary>
    bool HasApiKey(string? model = null);

    /// <summary>
    /// Sends a chat completion request to Azure AI Foundry
    /// </summary>
    Task<ChatCompletionResponse> GetChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a chat completion response from Azure AI Foundry
    /// </summary>
    IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}

public class AzureAIFoundryService : IAzureAIFoundryService
{
    private readonly IModelManagementService _modelManagementService;
    private readonly ILogger<AzureAIFoundryService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public AzureAIFoundryService(
        IModelManagementService modelManagementService,
        IHttpClientFactory httpClientFactory,
        ILogger<AzureAIFoundryService> logger)
    {
        _logger = logger;
        _modelManagementService = modelManagementService;
        _httpClientFactory = httpClientFactory;
    }

    public bool HasApiKey(string? model = null)
    {
        if (string.IsNullOrEmpty(model))
        {
            return false;
        }

        // Check if model has configuration with API key
        var config = _modelManagementService.GetModelConfigurationAsync(model).Result;
        return config != null && !string.IsNullOrWhiteSpace(config.ApiKey);
    }

    public async Task<ChatCompletionResponse> GetChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Sending chat completion request to Azure AI Foundry model {Model}", request.Model);

        // Get model configuration from ModelManagementService
        var config = await _modelManagementService.GetModelConfigurationAsync(request.Model);
        if (config == null || string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.Endpoint))
        {
            throw new AzureAIFoundryException($"Azure model '{request.Model}' is not properly configured. Please set API key and endpoint in Settings.");
        }

        var apiKey = config.ApiKey;
        var endpoint = config.Endpoint;

        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

        var requestBody = new
        {
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            top_p = request.TopP,
            presence_penalty = request.PresencePenalty,
            frequency_penalty = request.FrequencyPenalty,
            stream = false
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var httpResponse = await httpClient.PostAsync(endpoint, content, cancellationToken);
        var responseContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            _logger.LogError("Azure AI Foundry API error: {StatusCode} - {Content}", httpResponse.StatusCode, responseContent);
            throw new AzureAIFoundryException($"Azure AI Foundry API error: {httpResponse.StatusCode}", responseContent);
        }

        var jsonResponse = JsonDocument.Parse(responseContent);
        var root = jsonResponse.RootElement;

        var response = new ChatCompletionResponse
        {
            Id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
            Model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() ?? request.Model : request.Model,
            Choices = new List<ChatChoice>()
        };

        if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];
            var message = choice.GetProperty("message");

            response.Choices.Add(new ChatChoice
            {
                Index = 0,
                Message = new ChatMessageDto
                {
                    Role = message.TryGetProperty("role", out var roleProp) ? roleProp.GetString() ?? "assistant" : "assistant",
                    Content = message.TryGetProperty("content", out var contentProp) ? contentProp.GetString() ?? string.Empty : string.Empty
                },
                FinishReason = choice.TryGetProperty("finish_reason", out var finishProp) ? finishProp.GetString() : null
            });
        }

        if (root.TryGetProperty("usage", out var usage))
        {
            response.Usage = new UsageInfo
            {
                PromptTokens = usage.TryGetProperty("prompt_tokens", out var promptTokens) ? promptTokens.GetInt32() : 0,
                CompletionTokens = usage.TryGetProperty("completion_tokens", out var completionTokens) ? completionTokens.GetInt32() : 0,
                TotalTokens = usage.TryGetProperty("total_tokens", out var totalTokens) ? totalTokens.GetInt32() : 0
            };
        }

        _logger.LogDebug("Received Azure AI Foundry chat completion response with {Tokens} tokens", response.Usage?.TotalTokens ?? 0);

        return response;
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Starting streaming chat completion request to Azure AI Foundry model {Model}", request.Model);

        // Get model configuration from ModelManagementService
        var config = await _modelManagementService.GetModelConfigurationAsync(request.Model);
        if (config == null || string.IsNullOrWhiteSpace(config.ApiKey) || string.IsNullOrWhiteSpace(config.Endpoint))
        {
            throw new AzureAIFoundryException($"Azure model '{request.Model}' is not properly configured. Please set API key and endpoint in Settings.");
        }

        var apiKey = config.ApiKey;
        var endpoint = config.Endpoint;

        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        httpClient.Timeout = TimeSpan.FromMinutes(5);

        var requestBody = new
        {
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            top_p = request.TopP,
            presence_penalty = request.PresencePenalty,
            frequency_penalty = request.FrequencyPenalty,
            stream = true
        };

        var jsonContent = JsonSerializer.Serialize(requestBody);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };

        var httpResponse = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("Azure AI Foundry API streaming error: {StatusCode} - {Content}", httpResponse.StatusCode, errorContent);
            throw new AzureAIFoundryException($"Azure AI Foundry API error: {httpResponse.StatusCode}", errorContent);
        }

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrEmpty(line))
                continue;

            if (!line.StartsWith("data: "))
                continue;

            var data = line.Substring(6);

            if (data == "[DONE]")
                break;

            ChatCompletionChunk? chunk = null;

            try
            {
                using var jsonDoc = JsonDocument.Parse(data);
                var root = jsonDoc.RootElement;

                if (root.TryGetProperty("choices", out var choices) && choices.GetArrayLength() > 0)
                {
                    var choice = choices[0];

                    if (choice.TryGetProperty("delta", out var delta) &&
                        delta.TryGetProperty("content", out var contentProp))
                    {
                        var chunkContent = contentProp.GetString();
                        if (!string.IsNullOrEmpty(chunkContent))
                        {
                            chunk = new ChatCompletionChunk
                            {
                                Id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
                                Model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() ?? request.Model : request.Model,
                                Choices = new List<ChunkChoice>
                                {
                                    new ChunkChoice
                                    {
                                        Index = 0,
                                        Delta = new ChunkDelta
                                        {
                                            Content = chunkContent
                                        },
                                        FinishReason = choice.TryGetProperty("finish_reason", out var finishProp) &&
                                                       finishProp.ValueKind != JsonValueKind.Null ? finishProp.GetString() : null
                                    }
                                }
                            };
                        }
                    }
                }
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex, "Failed to parse streaming response chunk: {Data}", data);
            }

            if (chunk != null)
            {
                yield return chunk;
            }
        }
    }
}

/// <summary>
/// Exception thrown when Azure AI Foundry API returns an error
/// </summary>
public class AzureAIFoundryException : Exception
{
    public string? ResponseContent { get; }

    public AzureAIFoundryException(string message, string? responseContent = null)
        : base(message)
    {
        ResponseContent = responseContent;
    }
}
