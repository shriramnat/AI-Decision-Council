using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using DXO.Configuration;
using DXO.Models;
using DXO.Services.Models;

namespace DXO.Pages;

[IgnoreAntiforgeryToken]
public class SettingsModel : PageModel
{
    private readonly IModelManagementService _modelService;
    private readonly IModelInitializationService _modelInitService;
    private readonly DxoOptions _options;

    public SettingsModel(
        IModelManagementService modelService,
        IModelInitializationService modelInitService,
        IOptions<DxoOptions> options)
    {
        _modelService = modelService;
        _modelInitService = modelInitService;
        _options = options.Value;
    }

    public List<ConfiguredModel> ConfiguredModels { get; set; } = new();
    public string? StatusMessage { get; set; }
    public bool IsSuccess { get; set; }

    public async Task OnGetAsync()
    {
        var userEmail = GetUserEmail();
        if (userEmail == null)
        {
            StatusMessage = "User email not found";
            IsSuccess = false;
            return;
        }

        // Initialize default models for user if they have none
        await _modelInitService.InitializeDefaultModelsForUserAsync(userEmail);
        
        ConfiguredModels = await _modelService.GetAllModelsAsync(userEmail);
    }

    public string GetMaskedApiKey(string modelName)
    {
        var userEmail = GetUserEmail();
        if (userEmail == null)
            return string.Empty;
        
        var config = _modelService.GetModelConfigurationAsync(userEmail, modelName).Result;
        var apiKey = config?.ApiKey;
        
        if (string.IsNullOrEmpty(apiKey))
        {
            return string.Empty;
        }
        
        // API key is decrypted by ModelManagementService, so we can mask it normally
        // Show first 7 chars and last 4 chars for verification
        if (apiKey.Length > 11)
        {
            return apiKey.Substring(0, 7) + "..." + apiKey.Substring(apiKey.Length - 4);
        }
        return "••••••••";
    }
    
    private string? GetUserEmail()
    {
        return User.FindFirst(ClaimTypes.Email)?.Value 
            ?? User.FindFirst("preferred_username")?.Value 
            ?? User.FindFirst("email")?.Value
            ?? User.FindFirst(ClaimTypes.Name)?.Value;
    }

    /// <summary>
    /// Export all model configurations to a JSON file (API keys excluded per requirement)
    /// </summary>
    public async Task<IActionResult> OnGetExportAsync()
    {
        try
        {
            var userEmail = GetUserEmail();
            if (userEmail == null)
            {
                return BadRequest(new { error = "User email not found" });
            }

            var models = await _modelService.GetAllModelsAsync(userEmail);
            var exportData = new List<ModelExportData>();

            foreach (var model in models)
            {
                exportData.Add(new ModelExportData
                {
                    ModelName = model.ModelName,
                    Endpoint = model.Endpoint,
                    ApiKey = string.Empty, // DO NOT export API keys (requirement #3)
                    Provider = model.Provider.ToDisplayString()
                });
            }

            var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });

            var bytes = System.Text.Encoding.UTF8.GetBytes(json);
            return File(bytes, "application/json", "DXO.modelconfig");
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Export failed: {ex.Message}" });
        }
    }

    /// <summary>
    /// Import model configurations from a JSON file (requirement #2)
    /// </summary>
    public async Task<IActionResult> OnPostImportAsync()
    {
        try
        {
            var userEmail = GetUserEmail();
            if (userEmail == null)
            {
                return BadRequest(new { error = "User email not found" });
            }

            var file = Request.Form.Files.FirstOrDefault();
            if (file == null || file.Length == 0)
            {
                return BadRequest(new { error = "No file uploaded" });
            }

            if (!file.FileName.EndsWith(".modelconfig", StringComparison.OrdinalIgnoreCase))
            {
                return BadRequest(new { error = "Invalid file extension. Expected *.modelconfig" });
            }

            string jsonContent;
            using (var reader = new StreamReader(file.OpenReadStream()))
            {
                jsonContent = await reader.ReadToEndAsync();
            }

            List<ModelExportData>? importData;
            try
            {
                importData = System.Text.Json.JsonSerializer.Deserialize<List<ModelExportData>>(jsonContent);
            }
            catch (System.Text.Json.JsonException jsonEx)
            {
                return BadRequest(new { error = $"Invalid JSON format: {jsonEx.Message}. Please ensure the file is a valid JSON array with proper syntax (commas between objects, etc.)" });
            }
            
            if (importData == null || importData.Count == 0)
            {
                return BadRequest(new { error = "Invalid or empty configuration file" });
            }

            // Get existing models for this user
            var existingModels = await _modelService.GetAllModelsAsync(userEmail);
            var importedModelNames = importData.Select(m => m.ModelName).ToHashSet();

            // Delete models that are not in the import file
            foreach (var existingModel in existingModels)
            {
                if (!importedModelNames.Contains(existingModel.ModelName))
                {
                    await _modelService.DeleteModelAsync(userEmail, existingModel.Id);
                }
            }

            // Add or update models from the import file
            foreach (var modelData in importData)
            {
                var provider = ModelProviderExtensions.FromString(modelData.Provider);
                
                var existingModel = await _modelService.GetModelByNameAsync(userEmail, modelData.ModelName);
                
                if (existingModel != null)
                {
                    // Update existing model - update API key if provided
                    await _modelService.UpdateModelAsync(
                        userEmail,
                        existingModel.Id,
                        modelData.ModelName,
                        modelData.Endpoint,
                        provider,
                        !string.IsNullOrEmpty(modelData.ApiKey) ? modelData.ApiKey : null, // Update API key if provided
                        null
                    );
                }
                else
                {
                    // Add new model
                    await _modelService.AddModelAsync(
                        userEmail,
                        modelData.ModelName,
                        modelData.Endpoint,
                        provider,
                        !string.IsNullOrEmpty(modelData.ApiKey) ? modelData.ApiKey : null, // Add API key if provided
                        null
                    );
                }
            }

            return new JsonResult(new { success = true, message = $"Successfully imported {importData.Count} model(s)" });
        }
        catch (Exception ex)
        {
            return BadRequest(new { error = $"Import failed: {ex.Message}" });
        }
    }
}

/// <summary>
/// Model export/import data structure
/// </summary>
public class ModelExportData
{
    public string ModelName { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string ApiKey { get; set; } = string.Empty;
    public string Provider { get; set; } = "OpenAI";
}