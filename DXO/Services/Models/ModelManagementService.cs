using DXO.Data;
using DXO.Models;
using DXO.Services.Security;
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
    Task<List<ConfiguredModel>> GetAllModelsAsync(string userEmail);
    Task<ConfiguredModel?> GetModelByNameAsync(string userEmail, string modelName);
    Task<ConfiguredModel> AddModelAsync(string userEmail, string modelName, string endpoint, ModelProvider provider, string? apiKey = null, string? displayName = null);
    Task<ConfiguredModel> UpdateModelAsync(string userEmail, int id, string modelName, string endpoint, ModelProvider provider, string? apiKey = null, string? displayName = null);
    Task<bool> DeleteModelAsync(string userEmail, int id);
    Task<bool> ModelExistsAsync(string userEmail, string modelName, int? excludeId = null);
    
    /// <summary>
    /// Gets complete configuration for a model (endpoint, API key, type)
    /// </summary>
    Task<ModelConfiguration?> GetModelConfigurationAsync(string userEmail, string modelName);
}

public class ModelManagementService : IModelManagementService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IApiKeyEncryptionService _encryptionService;
    private readonly ILogger<ModelManagementService> _logger;

    public ModelManagementService(
        IServiceScopeFactory scopeFactory,
        IApiKeyEncryptionService encryptionService,
        ILogger<ModelManagementService> logger)
    {
        _scopeFactory = scopeFactory;
        _encryptionService = encryptionService;
        _logger = logger;
    }

    public async Task<List<ConfiguredModel>> GetAllModelsAsync(string userEmail)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        return await context.ConfiguredModels
            .Where(m => m.UserEmail == userEmail)
            .OrderBy(m => m.ModelName)
            .ToListAsync();
    }

    public async Task<ConfiguredModel?> GetModelByNameAsync(string userEmail, string modelName)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        return await context.ConfiguredModels
            .FirstOrDefaultAsync(m => m.UserEmail == userEmail && m.ModelName == modelName);
    }

    public async Task<ConfiguredModel> AddModelAsync(string userEmail, string modelName, string endpoint, ModelProvider provider, string? apiKey = null, string? displayName = null)
    {
        // Check for duplicates for this user
        if (await ModelExistsAsync(userEmail, modelName))
        {
            throw new InvalidOperationException($"A model with the name '{modelName}' already exists for this user.");
        }

        // Encrypt API key if provided
        string? encryptedApiKey = null;
        if (!string.IsNullOrWhiteSpace(apiKey))
        {
            encryptedApiKey = _encryptionService.Encrypt(apiKey);
            _logger.LogInformation("Encrypted API key for model: {ModelName}", modelName);
        }

        var model = new ConfiguredModel
        {
            UserEmail = userEmail,
            ModelName = modelName,
            DisplayName = displayName,
            Endpoint = endpoint,
            Provider = provider,
            ApiKey = encryptedApiKey,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        context.ConfiguredModels.Add(model);
        await context.SaveChangesAsync();

        _logger.LogInformation("Added model {ModelName} for user {UserEmail}", modelName, userEmail);
        return model;
    }

    public async Task<ConfiguredModel> UpdateModelAsync(string userEmail, int id, string modelName, string endpoint, ModelProvider provider, string? apiKey = null, string? displayName = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        
        var model = await context.ConfiguredModels
            .FirstOrDefaultAsync(m => m.Id == id && m.UserEmail == userEmail);
        
        if (model == null)
        {
            throw new InvalidOperationException($"Model with ID {id} not found for this user.");
        }

        // Check for duplicate name (excluding current model)
        if (model.ModelName != modelName && await ModelExistsAsync(userEmail, modelName, id))
        {
            throw new InvalidOperationException($"A model with the name '{modelName}' already exists for this user.");
        }

        model.ModelName = modelName;
        model.DisplayName = displayName;
        model.Endpoint = endpoint;
        model.Provider = provider;
        model.UpdatedAt = DateTime.UtcNow;

        // Update API key if provided (null means don't update, empty string means clear)
        if (apiKey != null)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                model.ApiKey = null;
                _logger.LogInformation("Cleared API key for model {ModelName}", modelName);
            }
            else
            {
                model.ApiKey = _encryptionService.Encrypt(apiKey);
                _logger.LogInformation("Updated API key for model {ModelName}", modelName);
            }
        }

        await context.SaveChangesAsync();

        _logger.LogInformation("Updated model {ModelName} for user {UserEmail}", modelName, userEmail);
        return model;
    }

    public async Task<bool> DeleteModelAsync(string userEmail, int id)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        
        var model = await context.ConfiguredModels
            .FirstOrDefaultAsync(m => m.Id == id && m.UserEmail == userEmail);
        
        if (model == null)
        {
            return false;
        }

        context.ConfiguredModels.Remove(model);
        await context.SaveChangesAsync();

        _logger.LogInformation("Deleted model {ModelName} for user {UserEmail}", model.ModelName, userEmail);
        return true;
    }

    public async Task<bool> ModelExistsAsync(string userEmail, string modelName, int? excludeId = null)
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
        
        var query = context.ConfiguredModels
            .Where(m => m.UserEmail == userEmail && m.ModelName == modelName);
        
        if (excludeId.HasValue)
        {
            query = query.Where(m => m.Id != excludeId.Value);
        }

        return await query.AnyAsync();
    }

    /// <summary>
    /// Gets complete configuration for a model including decrypted API key from database
    /// </summary>
    public async Task<ModelConfiguration?> GetModelConfigurationAsync(string userEmail, string modelName)
    {
        _logger.LogDebug("GetModelConfigurationAsync called for user: {UserEmail}, model: {ModelName}", userEmail, modelName);
        
        var model = await GetModelByNameAsync(userEmail, modelName);
        if (model == null)
        {
            _logger.LogDebug("Model '{ModelName}' not found for user {UserEmail}", modelName, userEmail);
            return null;
        }

        // Decrypt API key if present
        string? decryptedApiKey = null;
        if (!string.IsNullOrEmpty(model.ApiKey))
        {
            try
            {
                decryptedApiKey = _encryptionService.Decrypt(model.ApiKey);
                _logger.LogDebug("Successfully decrypted API key for model {ModelName}", modelName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to decrypt API key for model {ModelName}", modelName);
            }
        }

        return new ModelConfiguration
        {
            ModelName = model.ModelName,
            Endpoint = model.Endpoint,
            ApiKey = decryptedApiKey,
            Provider = model.Provider
        };
    }
}