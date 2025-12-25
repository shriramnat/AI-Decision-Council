using DXO.Models;
using DXO.Services.OpenAI;
using DXO.Services.AzureAIFoundry;
using DXO.Services.XAI;
using DXO.Services.Models;

namespace DXO.Services;

/// <summary>
/// Factory for creating appropriate model provider services based on the provider type
/// </summary>
public interface IModelProviderFactory
{
    /// <summary>
    /// Gets the appropriate provider service for the specified model
    /// </summary>
    Task<IModelProviderService> GetProviderServiceAsync(string modelName);
    
    /// <summary>
    /// Gets the provider service for a specific provider type
    /// </summary>
    IModelProviderService GetProviderService(ModelProvider provider);
}

public class ModelProviderFactory : IModelProviderFactory
{
    private readonly IModelManagementService _modelManagementService;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<ModelProviderFactory> _logger;

    public ModelProviderFactory(
        IModelManagementService modelManagementService,
        IServiceProvider serviceProvider,
        ILogger<ModelProviderFactory> logger)
    {
        _modelManagementService = modelManagementService;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public async Task<IModelProviderService> GetProviderServiceAsync(string modelName)
    {
        var config = await _modelManagementService.GetModelConfigurationAsync(modelName);
        if (config == null)
        {
            throw new InvalidOperationException($"Model '{modelName}' is not configured. Please add it in Settings.");
        }

        var model = await _modelManagementService.GetModelByNameAsync(modelName);
        if (model == null)
        {
            throw new InvalidOperationException($"Model '{modelName}' not found.");
        }

        _logger.LogDebug("Routing model '{ModelName}' to provider '{Provider}'", modelName, model.Provider);
        
        return GetProviderService(model.Provider);
    }

    public IModelProviderService GetProviderService(ModelProvider provider)
    {
        return provider switch
        {
            ModelProvider.OpenAI => new OpenAIProviderService(
                _serviceProvider.GetRequiredService<IModelManagementService>(),
                _serviceProvider.GetRequiredService<ILogger<OpenAIProviderService>>()),
            
            ModelProvider.Azure => new AzureProviderService(
                _serviceProvider.GetRequiredService<IAzureAIFoundryService>()),
            
            ModelProvider.XAI => new XAIProviderService(
                _serviceProvider.GetRequiredService<IXAIService>()),
            
            ModelProvider.Google => throw new NotImplementedException("Google provider is not yet implemented. Coming soon!"),
            ModelProvider.Anthropic => throw new NotImplementedException("Anthropic provider is not yet implemented. Coming soon!"),
            
            _ => throw new NotSupportedException($"Provider '{provider}' is not supported")
        };
    }
}

/// <summary>
/// OpenAI provider service implementation
/// </summary>
public class OpenAIProviderService : IModelProviderService
{
    private readonly IModelManagementService _modelManagementService;
    private readonly ILogger<OpenAIProviderService> _logger;

    public OpenAIProviderService(
        IModelManagementService modelManagementService,
        ILogger<OpenAIProviderService> logger)
    {
        _modelManagementService = modelManagementService;
        _logger = logger;
    }

    public bool HasApiKey(string model)
    {
        Console.WriteLine($"[OpenAIProviderService] HasApiKey called for model: {model}");
        var config = _modelManagementService.GetModelConfigurationAsync(model).Result;
        var hasKey = config != null && !string.IsNullOrWhiteSpace(config.ApiKey);
        Console.WriteLine($"[OpenAIProviderService] Config found: {config != null}, Has API key: {hasKey}");
        return hasKey;
    }

    public async Task<ChatCompletionResponse> GetChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        var config = await _modelManagementService.GetModelConfigurationAsync(request.Model);
        if (config == null || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new OpenAIException($"Model '{request.Model}' is not configured or missing API key.");
        }

        _logger.LogDebug("Sending chat completion request to OpenAI model {Model}", request.Model);

        var client = new global::OpenAI.OpenAIClient(config.ApiKey);
        var chatClient = client.GetChatClient(request.Model);

        var messages = request.Messages.Select(m => CreateChatMessage(m.Role, m.Content)).ToList();

        var chatOptions = new global::OpenAI.Chat.ChatCompletionOptions
        {
            Temperature = (float?)request.Temperature,
            MaxOutputTokenCount = request.MaxTokens,
            TopP = (float?)request.TopP,
            PresencePenalty = (float?)request.PresencePenalty,
            FrequencyPenalty = (float?)request.FrequencyPenalty
        };

        var completion = await chatClient.CompleteChatAsync(messages, chatOptions, cancellationToken);

        return new ChatCompletionResponse
        {
            Id = completion.Value.Id ?? string.Empty,
            Model = completion.Value.Model ?? request.Model,
            Choices = new List<ChatChoice>
            {
                new ChatChoice
                {
                    Index = 0,
                    Message = new ChatMessageDto
                    {
                        Role = "assistant",
                        Content = completion.Value.Content.FirstOrDefault()?.Text ?? string.Empty
                    },
                    FinishReason = completion.Value.FinishReason.ToString()
                }
            },
            Usage = new UsageInfo
            {
                PromptTokens = completion.Value.Usage?.InputTokenCount ?? 0,
                CompletionTokens = completion.Value.Usage?.OutputTokenCount ?? 0,
                TotalTokens = completion.Value.Usage?.TotalTokenCount ?? 0
            }
        };
    }

    public async IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        var config = await _modelManagementService.GetModelConfigurationAsync(request.Model);
        if (config == null || string.IsNullOrWhiteSpace(config.ApiKey))
        {
            throw new OpenAIException($"Model '{request.Model}' is not configured or missing API key.");
        }

        _logger.LogDebug("Starting streaming chat completion request to OpenAI model {Model}", request.Model);

        var client = new global::OpenAI.OpenAIClient(config.ApiKey);
        var chatClient = client.GetChatClient(request.Model);

        var messages = request.Messages.Select(m => CreateChatMessage(m.Role, m.Content)).ToList();

        var chatOptions = new global::OpenAI.Chat.ChatCompletionOptions
        {
            Temperature = (float?)request.Temperature,
            MaxOutputTokenCount = request.MaxTokens,
            TopP = (float?)request.TopP,
            PresencePenalty = (float?)request.PresencePenalty,
            FrequencyPenalty = (float?)request.FrequencyPenalty
        };

        var streamingUpdates = chatClient.CompleteChatStreamingAsync(messages, chatOptions, cancellationToken);

        await foreach (var update in streamingUpdates.WithCancellation(cancellationToken))
        {
            if (update.ContentUpdate.Count > 0)
            {
                var content = string.Join("", update.ContentUpdate.Select(c => c.Text));
                if (!string.IsNullOrEmpty(content))
                {
                    yield return new ChatCompletionChunk
                    {
                        Id = update.CompletionId ?? string.Empty,
                        Model = update.Model ?? request.Model,
                        Choices = new List<ChunkChoice>
                        {
                            new ChunkChoice
                            {
                                Index = 0,
                                Delta = new ChunkDelta { Content = content },
                                FinishReason = update.FinishReason?.ToString()
                            }
                        }
                    };
                }
            }
        }
    }

    private static global::OpenAI.Chat.ChatMessage CreateChatMessage(string role, string content)
    {
        return role.ToLowerInvariant() switch
        {
            "system" => global::OpenAI.Chat.ChatMessage.CreateSystemMessage(content),
            "user" => global::OpenAI.Chat.ChatMessage.CreateUserMessage(content),
            "assistant" => global::OpenAI.Chat.ChatMessage.CreateAssistantMessage(content),
            _ => global::OpenAI.Chat.ChatMessage.CreateUserMessage(content)
        };
    }
}

/// <summary>
/// xAI provider service implementation (wraps xAI service)
/// </summary>
public class XAIProviderService : IModelProviderService
{
    private readonly IXAIService _xaiService;

    public XAIProviderService(IXAIService xaiService)
    {
        _xaiService = xaiService;
    }

    public bool HasApiKey(string model)
    {
        return _xaiService.HasApiKey(model);
    }

    public Task<ChatCompletionResponse> GetChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        return _xaiService.GetChatCompletionAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        return _xaiService.StreamChatCompletionAsync(request, cancellationToken);
    }
}

/// <summary>
/// Azure provider service implementation (wraps existing Azure service)
/// </summary>
public class AzureProviderService : IModelProviderService
{
    private readonly IAzureAIFoundryService _azureService;

    public AzureProviderService(IAzureAIFoundryService azureService)
    {
        _azureService = azureService;
    }

    public bool HasApiKey(string model)
    {
        return _azureService.HasApiKey(model);
    }

    public Task<ChatCompletionResponse> GetChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        return _azureService.GetChatCompletionAsync(request, cancellationToken);
    }

    public IAsyncEnumerable<ChatCompletionChunk> StreamChatCompletionAsync(
        ChatCompletionRequest request,
        CancellationToken cancellationToken = default)
    {
        return _azureService.StreamChatCompletionAsync(request, cancellationToken);
    }
}
