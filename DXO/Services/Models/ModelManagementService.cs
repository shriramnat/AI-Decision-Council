using DXO.Data;
using DXO.Models;
using Microsoft.EntityFrameworkCore;

namespace DXO.Services.Models;

/// <summary>
/// Model configuration including API key
/// </summary>
public class ModelConfiguration
{
    public string ModelName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string? ApiKey { get; set; }
    public ModelProvider Provider { get; set; } = ModelProvider.OpenAI;
    
    /// <summary>
    /// [DEPRECATED] Use Provider instead. Kept for backward compatibility.
    /// </summary>
    [Obsolete("Use Provider property instead")]
    public bool IsAzureModel 
    { 
        get => Provider == ModelProvider.Azure;
        set => Provider = value ? ModelProvider.Azure : ModelProvider.OpenAI;
    }
}

/// <summary>
/// Service for managing configured AI models - Single source of truth for model configuration
/// </summary>
public interface IModelManagementService
{
    Task<List<ConfiguredModel>> GetAllModelsAsync();
    Task<ConfiguredModel?> GetModelByNameAsync(string modelName);
    Task<ConfiguredModel> AddModelAsync(string modelName, string endpoint, ModelProvider provider, string? displayName = null);
    Task<ConfiguredModel> UpdateModelAsync(int id, string modelName, string endpoint, ModelProvider provider, string? displayName = null);
    Task<bool> DeleteModelAsync(int id);
    Task<bool> ModelExistsAsync(string modelName, int? excludeId = null);
    void SetApiKeyForModel(string modelName, string? apiKey);
    
    /// <summary>
    /// Gets complete configuration for a model (endpoint, API key, type)
    /// </summary>
    Task<ModelConfiguration?> GetModelConfigurationAsync(string modelName);
}

public class ModelManagementService : IModelManagementService
{
    private readonly IServiceScopeFactory _scopeFactory;
    // In-memory storage for API keys (not persisted to database for security)
    private readonly Dictionary<string, string> _apiKeys = new();

    public ModelManagementService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
    }

    public async Task<List<ConfiguredModel>> GetAllModelsAsync()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        return await context.ConfiguredModels
            .OrderBy(m => m.ModelName)
            .ToListAsync();
    }

    public async Task<ConfiguredModel?> GetModelByNameAsync(string modelName)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        return await context.ConfiguredModels
            .FirstOrDefaultAsync(m => m.ModelName == modelName);
    }

    public async Task<ConfiguredModel> AddModelAsync(string modelName, string endpoint, ModelProvider provider, string? displayName = null)
    {
        // Check for duplicates
        if (await ModelExistsAsync(modelName))
        {
            throw new InvalidOperationException($"A model with the name '{modelName}' already exists.");
        }

        var model = new ConfiguredModel
        {
            ModelName = modelName,
            DisplayName = displayName,
            Endpoint = endpoint,
            Provider = provider,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        context.ConfiguredModels.Add(model);
        await context.SaveChangesAsync();

        return model;
    }

    public async Task<ConfiguredModel> UpdateModelAsync(int id, string modelName, string endpoint, ModelProvider provider, string? displayName = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        
        var model = await context.ConfiguredModels.FindAsync(id);
        if (model == null)
        {
            throw new InvalidOperationException($"Model with ID {id} not found.");
        }

        // Check for duplicate name (excluding current model)
        if (model.ModelName != modelName && await ModelExistsAsync(modelName, id))
        {
            throw new InvalidOperationException($"A model with the name '{modelName}' already exists.");
        }

        // If model name is changing, update the API key dictionary
        if (model.ModelName != modelName && _apiKeys.ContainsKey(model.ModelName))
        {
            var apiKey = _apiKeys[model.ModelName];
            _apiKeys.Remove(model.ModelName);
            _apiKeys[modelName] = apiKey;
        }

        model.ModelName = modelName;
        model.DisplayName = displayName;
        model.Endpoint = endpoint;
        model.Provider = provider;
        model.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        return model;
    }

    public async Task<bool> DeleteModelAsync(int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        
        var model = await context.ConfiguredModels.FindAsync(id);
        if (model == null)
        {
            return false;
        }

        // Remove associated API key
        if (_apiKeys.ContainsKey(model.ModelName))
        {
            _apiKeys.Remove(model.ModelName);
        }

        context.ConfiguredModels.Remove(model);
        await context.SaveChangesAsync();

        return true;
    }

    public async Task<bool> ModelExistsAsync(string modelName, int? excludeId = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        
        var query = context.ConfiguredModels.Where(m => m.ModelName == modelName);
        
        if (excludeId.HasValue)
        {
            query = query.Where(m => m.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    public void SetApiKeyForModel(string modelName, string? apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            _apiKeys.Remove(modelName);
            Console.WriteLine($"[ModelManagement] Removed API key for model: {modelName}");
        }
        else
        {
            _apiKeys[modelName] = apiKey;
            Console.WriteLine($"[ModelManagement] Set API key for model: {modelName} (length: {apiKey.Length})");
            Console.WriteLine($"[ModelManagement] Current keys in memory: {string.Join(", ", _apiKeys.Keys)}");
        }
    }

    /// <summary>
    /// Gets complete configuration for a model including API key from memory
    /// </summary>
    public async Task<ModelConfiguration?> GetModelConfigurationAsync(string modelName)
    {
        Console.WriteLine($"[ModelManagement] GetModelConfigurationAsync called for: {modelName}");
        
        var model = await GetModelByNameAsync(modelName);
        if (model == null)
        {
            Console.WriteLine($"[ModelManagement] Model '{modelName}' not found in database");
            return null;
        }

        var hasApiKey = _apiKeys.TryGetValue(modelName, out var apiKey);
        Console.WriteLine($"[ModelManagement] Model '{modelName}' found. Has API key: {hasApiKey}");
        Console.WriteLine($"[ModelManagement] All keys in memory: {string.Join(", ", _apiKeys.Keys)}");

        return new ModelConfiguration
        {
            ModelName = model.ModelName,
            Endpoint = model.Endpoint,
            ApiKey = apiKey,
            Provider = model.Provider
        };
    }
}
