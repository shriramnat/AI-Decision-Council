using System.Text;
using System.Text.Json;
using DXO.Services.OpenAI;
using DXO.Services.Models;

namespace DXO.Services.XAI;

/// <summary>
/// Service for interacting with xAI API (Grok models)
/// </summary>
public interface IXAIService
{
    /// <summary>
    /// Checks if an API key is configured for the given model
    /// </summary>
    bool HasApiKey(string? model = null);

    /// <summary>
    /// Sends a chat completion request to xAI
    /// </summary>
    Task<ChatCompletionResponse> GetChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Streams a chat completion response from xAI
    /// </summary>
    IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default);
}

public class XAIService : IXAIService
{
    private readonly IModelManagementService _modelManagementService;
    private readonly ILogger<XAIService> _logger;
    private readonly IHttpClientFactory _httpClientFactory;

    public XAIService(
        IModelManagementService modelManagementService,
        IHttpClientFactory httpClientFactory,
        ILogger<XAIService> logger)
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
        _logger.LogDebug("Sending chat completion request to xAI model {Model}", request.Model);

        // Get model configuration from ModelManagementService
        var config = await _modelManagementService.GetModelConfigurationAsync(request.Model);
        if (config == null || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new XAIException($"xAI model '{request.Model}' is not properly configured. Please set API key in Settings.");
        }

        var apiKey = config.ApiKey;
        // Use configured endpoint or default to xAI's OpenAI-compatible endpoint
        var endpoint = !string.IsNullOrWhiteSpace(config.Endpoint) 
            ? config.Endpoint 
            : "https://api.x.ai/v1/chat/completions";

        _logger.LogInformation("[XAI] Using endpoint: {Endpoint}", endpoint);
        _logger.LogInformation("[XAI] API Key length: {KeyLength}", apiKey.Length);

        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");

        // xAI supports OpenAI-compatible format with "messages"
        // Note: xAI does not support presence_penalty and frequency_penalty parameters
        var requestBody = new
        {
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            model = request.Model,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            top_p = request.TopP,
            stream = false
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
        _logger.LogInformation("[XAI] Request body:\n{RequestBody}", jsonContent);

        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var httpResponse = await httpClient.PostAsync(endpoint, content, cancellationToken);
        var responseContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);

        _logger.LogInformation("[XAI] Response status: {StatusCode}", httpResponse.StatusCode);
        _logger.LogInformation("[XAI] Response headers: {Headers}", string.Join(", ", httpResponse.Headers.Select(h => $"{h.Key}={string.Join(",", h.Value)}")));
        _logger.LogInformation("[XAI] Response content length: {Length}", responseContent?.Length ?? 0);
        _logger.LogInformation("[XAI] Raw response content:\n{ResponseContent}", responseContent);

        if (!httpResponse.IsSuccessStatusCode)
        {
            _logger.LogError("[XAI] API error: {StatusCode} - {Content}", httpResponse.StatusCode, responseContent);
            throw new XAIException($"xAI API error: {httpResponse.StatusCode}", responseContent);
        }

        if (string.IsNullOrWhiteSpace(responseContent))
        {
            _logger.LogError("[XAI] Received empty response from xAI API");
            throw new XAIException("xAI API returned an empty response");
        }

        JsonDocument jsonResponse;
        try
        {
            jsonResponse = JsonDocument.Parse(responseContent);
        }
        catch (JsonException ex)
        {
            _logger.LogError(ex, "[XAI] Failed to parse JSON response");
            throw new XAIException($"Failed to parse xAI response: {ex.Message}", responseContent);
        }

        var root = jsonResponse.RootElement;
        _logger.LogInformation("[XAI] Parsed JSON root element kind: {Kind}", root.ValueKind);

        var response = new ChatCompletionResponse
        {
            Id = root.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? string.Empty : string.Empty,
            Model = root.TryGetProperty("model", out var modelProp) ? modelProp.GetString() ?? request.Model : request.Model,
            Choices = new List<ChatChoice>()
        };

        _logger.LogInformation("[XAI] Response ID: {Id}, Model: {Model}", response.Id, response.Model);

        if (root.TryGetProperty("choices", out var choices))
        {
            _logger.LogInformation("[XAI] Found 'choices' property with {Count} items", choices.GetArrayLength());
            
            if (choices.GetArrayLength() > 0)
            {
                var choice = choices[0];
                _logger.LogInformation("[XAI] Processing first choice");
                
                if (choice.TryGetProperty("message", out var message))
                {
                    var role = message.TryGetProperty("role", out var roleProp) ? roleProp.GetString() ?? "assistant" : "assistant";
                    var messageContent = message.TryGetProperty("content", out var contentProp) ? contentProp.GetString() ?? string.Empty : string.Empty;
                    
                    _logger.LogInformation("[XAI] Message role: {Role}, Content length: {Length}", role, messageContent.Length);

                    response.Choices.Add(new ChatChoice
                    {
                        Index = 0,
                        Message = new ChatMessageDto
                        {
                            Role = role,
                            Content = messageContent
                        },
                        FinishReason = choice.TryGetProperty("finish_reason", out var finishProp) ? finishProp.GetString() : null
                    });
                }
                else
                {
                    _logger.LogWarning("[XAI] Choice does not have 'message' property");
                }
            }
        }
        else
        {
            _logger.LogWarning("[XAI] Response does not have 'choices' property");
        }

        if (root.TryGetProperty("usage", out var usage))
        {
            response.Usage = new UsageInfo
            {
                PromptTokens = usage.TryGetProperty("prompt_tokens", out var promptTokens) ? promptTokens.GetInt32() : 0,
                CompletionTokens = usage.TryGetProperty("completion_tokens", out var completionTokens) ? completionTokens.GetInt32() : 0,
                TotalTokens = usage.TryGetProperty("total_tokens", out var totalTokens) ? totalTokens.GetInt32() : 0
            };
            _logger.LogInformation("[XAI] Usage - Prompt: {Prompt}, Completion: {Completion}, Total: {Total}", 
                response.Usage.PromptTokens, response.Usage.CompletionTokens, response.Usage.TotalTokens);
        }

        _logger.LogInformation("[XAI] Completed processing response with {ChoiceCount} choices", response.Choices.Count);

        return response;
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("[XAI] Starting streaming chat completion request to xAI model {Model}", request.Model);

        // Get model configuration from ModelManagementService
        var config = await _modelManagementService.GetModelConfigurationAsync(request.Model);
        if (config == null || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new XAIException($"xAI model '{request.Model}' is not properly configured. Please set API key in Settings.");
        }

        var apiKey = config.ApiKey;
        // Use configured endpoint or default to xAI's OpenAI-compatible endpoint
        var endpoint = !string.IsNullOrWhiteSpace(config.Endpoint) 
            ? config.Endpoint 
            : "https://api.x.ai/v1/chat/completions";

        _logger.LogInformation("[XAI] Streaming - Using endpoint: {Endpoint}", endpoint);
        _logger.LogInformation("[XAI] Streaming - API Key length: {KeyLength}", apiKey.Length);

        using var httpClient = _httpClientFactory.CreateClient();
        httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {apiKey}");
        httpClient.Timeout = TimeSpan.FromMinutes(60); // xAI suggests 3600 seconds timeout

        // xAI supports OpenAI-compatible format with "messages"
        // Note: xAI does not support presence_penalty and frequency_penalty parameters
        var requestBody = new
        {
            messages = request.Messages.Select(m => new { role = m.Role, content = m.Content }).ToArray(),
            model = request.Model,
            temperature = request.Temperature,
            max_tokens = request.MaxTokens,
            top_p = request.TopP,
            stream = true
        };

        var jsonContent = JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { WriteIndented = true });
        _logger.LogInformation("[XAI] Streaming request body:\n{RequestBody}", jsonContent);

        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var requestMessage = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = content
        };

        var httpResponse = await httpClient.SendAsync(requestMessage, HttpCompletionOption.ResponseHeadersRead, cancellationToken);

        _logger.LogInformation("[XAI] Streaming response status: {StatusCode}", httpResponse.StatusCode);

        if (!httpResponse.IsSuccessStatusCode)
        {
            var errorContent = await httpResponse.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogError("[XAI] API streaming error: {StatusCode} - {Content}", httpResponse.StatusCode, errorContent);
            throw new XAIException($"xAI API error: {httpResponse.StatusCode}", errorContent);
        }

        using var stream = await httpResponse.Content.ReadAsStreamAsync(cancellationToken);
        using var reader = new StreamReader(stream);

        _logger.LogInformation("[XAI] Starting to read streaming response");
        var chunkCount = 0;

        while (!reader.EndOfStream && !cancellationToken.IsCancellationRequested)
        {
            var line = await reader.ReadLineAsync(cancellationToken);

            if (string.IsNullOrEmpty(line))
                continue;

            _logger.LogDebug("[XAI] Received line: {Line}", line);

            if (!line.StartsWith("data: "))
                continue;

            var data = line.Substring(6);

            if (data == "[DONE]")
            {
                _logger.LogInformation("[XAI] Received [DONE] marker, completing stream");
                break;
            }

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
                            chunkCount++;
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
                _logger.LogWarning(ex, "[XAI] Failed to parse streaming response chunk: {Data}", data);
            }

            if (chunk != null)
            {
                yield return chunk;
            }
        }

        _logger.LogInformation("[XAI] Streaming completed with {ChunkCount} chunks", chunkCount);
    }
}

/// <summary>
/// Exception thrown when xAI API returns an error
/// </summary>
public class XAIException : Exception
{
    public string? ResponseContent { get; }

    public XAIException(string message, string? responseContent = null)
        : base(message)
    {
        ResponseContent = responseContent;
    }
}
