using DXO.Configuration;
using DXO.Models;
using Microsoft.Extensions.Options;

namespace DXO.Services.Models;

/// <summary>
/// Service to initialize models from appsettings.json into the database
/// </summary>
public interface IModelInitializationService
{
    Task InitializeModelsAsync();
    Task InitializeDefaultModelsForUserAsync(string userEmail);
}

public class ModelInitializationService : IModelInitializationService
{
    private readonly IModelManagementService _modelService;
    private readonly DxoOptions _options;
    private readonly ILogger<ModelInitializationService> _logger;

    public ModelInitializationService(
        IModelManagementService modelService,
        IOptions<DxoOptions> options,
        ILogger<ModelInitializationService> logger)
    {
        _modelService = modelService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task InitializeModelsAsync()
    {
        // This method is now deprecated - initialization happens per-user
        _logger.LogInformation("Global model initialization skipped - models are now initialized per-user");
    }

    public async Task InitializeDefaultModelsForUserAsync(string userEmail)
    {
        try
        {
            var existingModels = await _modelService.GetAllModelsAsync(userEmail);
            
            // If user already has models configured, skip initialization
            if (existingModels.Any())
            {
                _logger.LogInformation("User {UserEmail} already has models configured. Skipping initialization.", userEmail);
                return;
            }

            _logger.LogInformation("Initializing default models for user {UserEmail} from appsettings.json...", userEmail);

            // Add all models from the Models configuration array for this user
            if (_options.Models != null && _options.Models.Length > 0)
            {
                foreach (var modelConfig in _options.Models)
                {
                    if (string.IsNullOrWhiteSpace(modelConfig.ModelName) || 
                        string.IsNullOrWhiteSpace(modelConfig.Endpoint))
                    {
                        _logger.LogWarning("Skipping model with missing ModelName or Endpoint");
                        continue;
                    }

                    try
                    {
                        var provider = ModelProviderExtensions.FromString(modelConfig.Provider);
                        
                        await _modelService.AddModelAsync(
                            userEmail: userEmail,
                            modelName: modelConfig.ModelName,
                            endpoint: modelConfig.Endpoint,
                            provider: provider,
                            apiKey: null, // No API key - user must provide
                            displayName: modelConfig.DisplayName
                        );
                        
                        _logger.LogInformation(
                            "Added {Provider} model: {ModelName} for user {UserEmail}", 
                            provider.ToDisplayString(), 
                            modelConfig.ModelName,
                            userEmail
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to add model {ModelName} for user {UserEmail}", modelConfig.ModelName, userEmail);
                    }
                }
            }
            else
            {
                _logger.LogWarning("No models configured in appsettings.json");
            }

            _logger.LogInformation("Default model initialization complete for user {UserEmail}.", userEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during model initialization for user {UserEmail}", userEmail);
        }
    }
}