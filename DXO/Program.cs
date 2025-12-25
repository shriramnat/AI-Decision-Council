using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using DXO.Configuration;
using DXO.Data;
using DXO.Hubs;
using DXO.Models;
using DXO.Services;
using DXO.Services.AzureAIFoundry;
using DXO.Services.XAI;
using DXO.Services.OpenAI;
using DXO.Services.Orchestration;
using DXO.Services.Settings;
using DXO.Services.Models;
using Polly;
using Polly.Extensions.Http;

var builder = WebApplication.CreateBuilder(args);

// Configure DXO options
builder.Services.Configure<DxoOptions>(builder.Configuration.GetSection(DxoOptions.SectionName));

// Get OpenAI API key from environment variable first, then config
var openAiApiKey = Environment.GetEnvironmentVariable("DXO_OPENAI_API_KEY")
    ?? builder.Configuration.GetValue<string>("DXO:OpenAI:ApiKey")
    ?? string.Empty;

var dxoConfig = builder.Configuration.GetSection(DxoOptions.SectionName).Get<DxoOptions>() ?? new DxoOptions();

// Configure Entity Framework with SQLite
if (dxoConfig.Persistence.Enabled)
{
    builder.Services.AddDbContext<DxoDbContext>(options =>
        options.UseSqlite(dxoConfig.Persistence.ConnectionString));
}
else
{
    builder.Services.AddDbContext<DxoDbContext>(options =>
        options.UseInMemoryDatabase("DxoInMemory"));
}

// Add HttpClientFactory for general HTTP calls (used by DeepSeek)
builder.Services.AddHttpClient();

// Configure HttpClient for OpenAI with retry policy
// Note: BaseUrl and Authorization will be set per-request since models may have different endpoints
builder.Services.AddHttpClient("OpenAI", client =>
{
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", openAiApiKey);
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
    client.Timeout = TimeSpan.FromSeconds(dxoConfig.RequestTimeoutSeconds);
})
.AddPolicyHandler(GetRetryPolicy(dxoConfig.MaxRetries));

// Register services
builder.Services.AddSingleton<ISettingsService, SettingsService>();
builder.Services.AddSingleton<IModelManagementService, ModelManagementService>();
builder.Services.AddSingleton<IModelProviderFactory, ModelProviderFactory>();
builder.Services.AddSingleton<IAzureAIFoundryService, AzureAIFoundryService>();
builder.Services.AddSingleton<IXAIService, XAIService>();
builder.Services.AddSingleton<IOpenAIService, OpenAIService>();
builder.Services.AddScoped<IModelInitializationService, ModelInitializationService>();
builder.Services.AddScoped<IOrchestrationService, OrchestrationService>();

// Add SignalR
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
});

// Add Razor Pages
builder.Services.AddRazorPages();

// Configure JSON serialization to handle string enums
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Configure rate limiting
builder.Services.AddRateLimiter(options =>
{
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = dxoConfig.RateLimiting.PermitLimit,
                Window = TimeSpan.FromSeconds(dxoConfig.RateLimiting.WindowSeconds),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 2
            }));
    
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});

// Add CORS for development
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.AllowAnyOrigin()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

var app = builder.Build();

// Ensure database is created and initialize models
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<DxoDbContext>();
    // Delete existing database to apply schema changes (IMPORTANT: Remove this line after first successful run)
    // dbContext.Database.EnsureDeleted();
    dbContext.Database.EnsureCreated();
    
    // Initialize models from appsettings.json
    var modelInitService = scope.ServiceProvider.GetRequiredService<IModelInitializationService>();
    await modelInitService.InitializeModelsAsync();
}

// Configure security headers
app.Use(async (context, next) =>
{
    // Security headers
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("X-XSS-Protection", "1; mode=block");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("Permissions-Policy", "accelerometer=(), camera=(), geolocation=(), gyroscope=(), magnetometer=(), microphone=(), payment=(), usb=()");
    
    if (!app.Environment.IsDevelopment())
    {
        context.Response.Headers.Append("Content-Security-Policy", 
            "default-src 'self'; script-src 'self' 'unsafe-inline' 'unsafe-eval'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; font-src 'self'; connect-src 'self' ws: wss:;");
    }
    
    await next();
});

// Configure the HTTP request pipeline
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();
app.UseCors();
app.UseRateLimiter();

app.MapRazorPages();
app.MapHub<DxoHub>("/hubs/dxo");

// Map API endpoints
app.MapPost("/api/session/create", async (CreateSessionRequest request, IOrchestrationService orchestration, CancellationToken ct) =>
{
    var session = await orchestration.CreateSessionAsync(request, ct);
    return Results.Ok(session);
});

app.MapGet("/api/session/{id:guid}", async (Guid id, IOrchestrationService orchestration, CancellationToken ct) =>
{
    var session = await orchestration.GetSessionAsync(id, ct);
    return session != null ? Results.Ok(session) : Results.NotFound();
});

app.MapGet("/api/sessions", async (IOrchestrationService orchestration, CancellationToken ct) =>
{
    var sessions = await orchestration.GetSessionsAsync(ct);
    return Results.Ok(sessions);
});

app.MapPost("/api/session/{id:guid}/start", async (Guid id, IOrchestrationService orchestration, IOpenAIService openAIService, CancellationToken ct) =>
{
    try
    {
        var session = await orchestration.GetSessionAsync(id, ct);
        if (session == null)
            return Results.NotFound();

        // Collect distinct models used by this session (creator + reviewers)
        var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var creator = JsonSerializer.Deserialize<PersonaConfig>(session.CreatorConfigJson);
            if (!string.IsNullOrWhiteSpace(creator?.Model))
                models.Add(creator.Model);

            var reviewers = JsonSerializer.Deserialize<List<ReviewerConfig>>(session.ReviewersConfigJson) ?? new List<ReviewerConfig>();
            foreach (var r in reviewers)
            {
                if (!string.IsNullOrWhiteSpace(r.Model))
                    models.Add(r.Model);
            }
        }
        catch (JsonException ex)
        {
            // If session configs can't be parsed, return error - we need to know which models to validate
            return Results.BadRequest(new { error = $"Unable to parse session configuration: {ex.Message}" });
        }

        // Validate keys only for used models
        var missing = models.Where(m => !openAIService.HasApiKey(m)).ToList();
        if (missing.Any())
        {
            return Results.BadRequest(new
            {
                error = $"Missing API key(s) for models: {string.Join(", ", missing)}. Please configure corresponding API key(s) in Settings."
            });
        }

        await orchestration.StartSessionAsync(id, ct);
        return Results.Ok(new { success = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/session/{id:guid}/step", async (Guid id, IOrchestrationService orchestration, IOpenAIService openAIService, CancellationToken ct) =>
{
    try
    {
        var session = await orchestration.GetSessionAsync(id, ct);
        if (session == null)
            return Results.NotFound();

        // Collect distinct models used by this session (creator + reviewers)
        var models = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            var creator = JsonSerializer.Deserialize<PersonaConfig>(session.CreatorConfigJson);
            if (!string.IsNullOrWhiteSpace(creator?.Model))
                models.Add(creator.Model);

            var reviewers = JsonSerializer.Deserialize<List<ReviewerConfig>>(session.ReviewersConfigJson) ?? new List<ReviewerConfig>();
            foreach (var r in reviewers)
            {
                if (!string.IsNullOrWhiteSpace(r.Model))
                    models.Add(r.Model);
            }
        }
        catch (JsonException ex)
        {
            // If session configs can't be parsed, return error - we need to know which models to validate
            return Results.BadRequest(new { error = $"Unable to parse session configuration: {ex.Message}" });
        }

        // Validate keys only for used models
        var missing = models.Where(m => !openAIService.HasApiKey(m)).ToList();
        if (missing.Any())
        {
            return Results.BadRequest(new
            {
                error = $"Missing API key(s) for models: {string.Join(", ", missing)}. Please configure corresponding API key(s) in Settings."
            });
        }

        await orchestration.StepSessionAsync(id, ct);
        return Results.Ok(new { success = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPost("/api/session/{id:guid}/stop", async (Guid id, IOrchestrationService orchestration, CancellationToken ct) =>
{
    await orchestration.StopSessionAsync(id, ct);
    return Results.Ok(new { success = true });
});

app.MapDelete("/api/session/{id:guid}", async (Guid id, IOrchestrationService orchestration, CancellationToken ct) =>
{
    await orchestration.DeleteSessionAsync(id, ct);
    return Results.Ok(new { success = true });
});

app.MapPost("/api/session/{id:guid}/reset-memory/{persona}", async (Guid id, string persona, IOrchestrationService orchestration, CancellationToken ct) =>
{
    if (!Enum.TryParse<DXO.Models.Persona>(persona, true, out var personaEnum))
    {
        return Results.BadRequest(new { error = "Invalid persona. Use 'Creator', 'Reviewer1', or 'Reviewer2'." });
    }
    
    await orchestration.ResetPersonaMemoryAsync(id, personaEnum, ct);
    return Results.Ok(new { success = true });
});

app.MapGet("/api/config", async (IOptions<DxoOptions> options, IModelManagementService modelService) =>
{
    var config = options.Value;
    var models = await modelService.GetAllModelsAsync();
    var modelNames = models.Select(m => m.ModelName).ToList();
    
    return Results.Ok(new
    {
        allowedModels = modelNames,
        defaultModelCreator = config.DefaultModelCreator,
        defaultModelReviewer = config.DefaultModelReviewer,
        defaultMaxIterations = config.Orchestration.DefaultMaxIterations,
        defaultStopMarker = config.Orchestration.DefaultStopMarker,
        maxPromptChars = config.Orchestration.MaxPromptChars,
        maxDraftChars = config.Orchestration.MaxDraftChars
    });
});

// Model Management API endpoints
app.MapGet("/api/models", async (IModelManagementService modelService, CancellationToken ct) =>
{
    var models = await modelService.GetAllModelsAsync();
    return Results.Ok(models);
});

app.MapGet("/api/models/{id:int}", async (int id, IModelManagementService modelService, CancellationToken ct) =>
{
    var models = await modelService.GetAllModelsAsync();
    var model = models.FirstOrDefault(m => m.Id == id);
    return model != null ? Results.Ok(model) : Results.NotFound();
});

app.MapPost("/api/models", async (AddModelRequest request, IModelManagementService modelService, CancellationToken ct) =>
{
    try
    {
        // Support both Provider (new) and IsAzureModel (legacy) for backward compatibility
        var provider = !string.IsNullOrWhiteSpace(request.Provider)
            ? ModelProviderExtensions.FromString(request.Provider)
            : (request.IsAzureModel ? ModelProvider.Azure : ModelProvider.OpenAI);
        
        var model = await modelService.AddModelAsync(
            request.ModelName,
            request.Endpoint,
            provider,
            request.DisplayName
        );

        // Set API key if provided
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            modelService.SetApiKeyForModel(request.ModelName, request.ApiKey);
        }

        return Results.Ok(model);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapPut("/api/models/{id:int}", async (int id, UpdateModelRequest request, IModelManagementService modelService, ILogger<Program> logger, CancellationToken ct) =>
{
    logger.LogInformation($"[API] PUT /api/models/{id} called. ModelName: '{request.ModelName}', HasApiKey: {!string.IsNullOrWhiteSpace(request.ApiKey)}");
    
    try
    {
        // Support both Provider (new) and IsAzureModel (legacy) for backward compatibility
        var provider = !string.IsNullOrWhiteSpace(request.Provider)
            ? ModelProviderExtensions.FromString(request.Provider)
            : (request.IsAzureModel ? ModelProvider.Azure : ModelProvider.OpenAI);
        
        var model = await modelService.UpdateModelAsync(
            id,
            request.ModelName,
            request.Endpoint,
            provider,
            request.DisplayName
        );

        logger.LogInformation($"[API] Model updated successfully. ID: {model.Id}, Name: '{model.ModelName}'");

        // Set API key if provided
        if (!string.IsNullOrWhiteSpace(request.ApiKey))
        {
            logger.LogInformation($"[API] Setting API key for model '{request.ModelName}' (length: {request.ApiKey.Length})");
            modelService.SetApiKeyForModel(request.ModelName, request.ApiKey);
        }
        else
        {
            logger.LogInformation($"[API] No API key provided in request for model '{request.ModelName}'");
        }

        return Results.Ok(model);
    }
    catch (InvalidOperationException ex)
    {
        logger.LogError($"[API] Failed to update model: {ex.Message}");
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapDelete("/api/models/{id:int}", async (int id, IModelManagementService modelService, CancellationToken ct) =>
{
    var deleted = await modelService.DeleteModelAsync(id);
    return deleted ? Results.Ok(new { success = true }) : Results.NotFound();
});

app.MapPost("/api/models/api-keys", (Dictionary<string, string> apiKeys, IModelManagementService modelService) =>
{
    foreach (var kvp in apiKeys)
    {
        modelService.SetApiKeyForModel(kvp.Key, kvp.Value);
    }
    return Results.Ok(new { success = true, count = apiKeys.Count });
});

app.Run();

// Retry policy for transient HTTP errors
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(int maxRetries)
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(maxRetries, retryAttempt => 
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}
