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
        try
        {
            var existingModels = await _modelService.GetAllModelsAsync();
            
            // If there are already models configured, skip initialization
            if (existingModels.Any())
            {
                _logger.LogInformation("Models already configured. Skipping initialization.");
                return;
            }

            _logger.LogInformation("Initializing models from appsettings.json...");

            // Add all models from the Models configuration array
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
                        // Parse provider from string
                        _logger.LogInformation(
                            "Parsing provider string: '{ProviderString}' for model: {ModelName}",
                            modelConfig.Provider ?? "(null)",
                            modelConfig.ModelName
                        );
                        
                        var provider = ModelProviderExtensions.FromString(modelConfig.Provider);
                        
                        _logger.LogInformation(
                            "Parsed provider enum: {ProviderEnum} ({ProviderInt}) for model: {ModelName}",
                            provider,
                            (int)provider,
                            modelConfig.ModelName
                        );
                        
                        await _modelService.AddModelAsync(
                            modelName: modelConfig.ModelName,
                            endpoint: modelConfig.Endpoint,
                            provider: provider,
                            displayName: modelConfig.DisplayName
                        );
                        
                        _logger.LogInformation(
                            "Added {Provider} model: {ModelName}", 
                            provider.ToDisplayString(), 
                            modelConfig.ModelName
                        );
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to add model {ModelName}", modelConfig.ModelName);
                    }
                }
            }
            else
            {
                _logger.LogWarning("No models configured in appsettings.json");
            }

            _logger.LogInformation("Model initialization complete.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during model initialization");
        }
    }
}
