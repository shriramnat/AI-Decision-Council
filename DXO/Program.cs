using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Authentication.MicrosoftAccount;
using Microsoft.AspNetCore.Authorization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Web;
using Polly;
using Polly.Extensions.Http;
using DXO.Configuration;
using DXO.Data;
using DXO.Hubs;
using DXO.Models;
using DXO.Services;
using DXO.Services.Authorization;
using DXO.Services.AzureAIFoundry;
using DXO.Services.XAI;
using DXO.Services.OpenAI;
using DXO.Services.Orchestration;
using DXO.Services.Models;
using DXO.Services.NativeAgent;
using DXO.Services.Security;

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

// Configure Data Protection for API key encryption
// Use D:\home\keys on Azure Windows, otherwise local keys directory
var keysPath = builder.Environment.IsProduction() 
    ? Path.Combine("D:", "home", "keys")
    : Path.Combine(Directory.GetCurrentDirectory(), "keys");

// Ensure directory exists
if (!Directory.Exists(keysPath))
{
    Directory.CreateDirectory(keysPath);
}

builder.Services.AddDataProtection()
    .PersistKeysToFileSystem(new DirectoryInfo(keysPath))
    .SetApplicationName("DXO")
    .SetDefaultKeyLifetime(TimeSpan.FromDays(90));

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
builder.Services.AddSingleton<IApiKeyEncryptionService, ApiKeyEncryptionService>();
builder.Services.AddSingleton<IModelManagementService, ModelManagementService>();
builder.Services.AddSingleton<IModelProviderFactory, ModelProviderFactory>();
builder.Services.AddSingleton<IAzureAIFoundryService, AzureAIFoundryService>();
builder.Services.AddSingleton<IXAIService, XAIService>();
builder.Services.AddSingleton<IOpenAIService, OpenAIService>();
builder.Services.AddScoped<IModelInitializationService, ModelInitializationService>();
builder.Services.AddScoped<IOrchestrationService, OrchestrationService>();
builder.Services.AddScoped<IReviewerRecommendationService, ReviewerRecommendationService>();

// Register authorization services
builder.Services.AddSingleton<IApprovedListService, FileApprovedListService>();
builder.Services.AddSingleton<IAuthorizationHandler, ApprovedListAuthorizationHandler>();

// Configure authentication options
builder.Services.Configure<AuthOptions>(builder.Configuration.GetSection(AuthOptions.SectionName));
var authConfig = builder.Configuration.GetSection(AuthOptions.SectionName).Get<AuthOptions>() ?? new AuthOptions();

// Configure authentication
var authBuilder = builder.Services.AddAuthentication(options =>
{
    options.DefaultScheme = CookieAuthenticationDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = CookieAuthenticationDefaults.AuthenticationScheme;
});

if (authConfig.Enabled)
{
    // Full authentication with OIDC providers
    builder.Services.AddLogging(logging => 
        logging.AddConsole().AddDebug().SetMinimumLevel(LogLevel.Information));
    
    var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Startup");
    logger.LogInformation("Authentication is ENABLED - configuring OIDC providers");

    // Define common cookie options configuration to ensure consistency
    Action<Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationOptions> configureCookie = options =>
    {
        options.Cookie.Name = "DXO.Auth";
        options.LoginPath = "/Landing";
        options.LogoutPath = "/Logout";
        // Don't set AccessDeniedPath here - we handle 403s manually in middleware to prevent redirect loops
        options.ExpireTimeSpan = TimeSpan.FromMinutes(authConfig.Cookie.ExpireTimeMinutes);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() 
            ? CookieSecurePolicy.SameAsRequest 
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        
        // Override the OnRedirectToAccessDenied event to prevent redirect loops
        options.Events.OnRedirectToAccessDenied = context =>
        {
            // Sign out the user and redirect to landing with error
            context.Response.Redirect("/Landing?authError=denied");
            return Task.CompletedTask;
        };
    };

    // Add Entra ID (Azure AD) authentication
    if (authConfig.EntraId.Enabled)
    {
        // This also adds Cookie authentication automatically using the provided options
        authBuilder.AddMicrosoftIdentityWebApp(options =>
        {
            options.Instance = authConfig.EntraId.Instance;
            options.TenantId = authConfig.EntraId.TenantId;
            options.ClientId = authConfig.EntraId.ClientId;
            options.ClientSecret = authConfig.EntraId.ClientSecret;
            options.CallbackPath = authConfig.EntraId.CallbackPath;
            options.SignedOutCallbackPath = "/signout-callback-oidc";
            options.SaveTokens = false;
            
            // Use authorization code flow (not hybrid flow)
            // This avoids the need for id_token response type to be enabled
            options.ResponseType = Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectResponseType.Code;
        }, configureCookie, "EntraId");
    }
    else
    {
        // If Entra ID is disabled, we must manually configure the cookie authentication
        // because AddMicrosoftIdentityWebApp won't be called to do it for us.
        authBuilder.AddCookie(configureCookie);
    }

    // Add Microsoft Account authentication
    if (authConfig.MicrosoftAccount.Enabled)
    {
        authBuilder.AddMicrosoftAccount("Microsoft", options =>
        {
            options.ClientId = authConfig.MicrosoftAccount.ClientId;
            options.ClientSecret = authConfig.MicrosoftAccount.ClientSecret;
            options.CallbackPath = authConfig.MicrosoftAccount.CallbackPath;
            options.SaveTokens = false;
            
            // Use /consumers endpoint for personal Microsoft accounts only
            // This matches the app registration's "PersonalMicrosoftAccount" audience
            options.AuthorizationEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/authorize";
            options.TokenEndpoint = "https://login.microsoftonline.com/consumers/oauth2/v2.0/token";
            
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
        });
    }

    // Add Google authentication
    if (authConfig.Google.Enabled)
    {
        authBuilder.AddGoogle(options =>
        {
            options.ClientId = authConfig.Google.ClientId;
            options.ClientSecret = authConfig.Google.ClientSecret;
            options.CallbackPath = authConfig.Google.CallbackPath;
            options.SaveTokens = false;
            options.Scope.Add("openid");
            options.Scope.Add("profile");
            options.Scope.Add("email");
        });
    }
}
else
{
    // Simplified authentication for development/testing
    var logger = LoggerFactory.Create(config => config.AddConsole()).CreateLogger("Startup");
    logger.LogWarning("⚠️  Authentication is DISABLED - using test account for development. DO NOT USE IN PRODUCTION!");
    
    // Add cookie authentication only (no OIDC providers)
    authBuilder.AddCookie(options =>
    {
        options.Cookie.Name = "DXO.Auth";
        options.LoginPath = "/Landing";
        options.LogoutPath = "/Logout";
        // Don't set AccessDeniedPath here - we handle 403s manually in middleware to prevent redirect loops
        options.ExpireTimeSpan = TimeSpan.FromMinutes(authConfig.Cookie.ExpireTimeMinutes);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = builder.Environment.IsDevelopment() 
            ? CookieSecurePolicy.SameAsRequest 
            : CookieSecurePolicy.Always;
        options.Cookie.SameSite = SameSiteMode.Lax;
        
        // Override the OnRedirectToAccessDenied event to prevent redirect loops
        options.Events.OnRedirectToAccessDenied = context =>
        {
            // Sign out the user and redirect to landing with error
            context.Response.Redirect("/Landing?authError=denied");
            return Task.CompletedTask;
        };
    });
}

// Configure authorization
builder.Services.AddAuthorization(options =>
{
    // Default policy requires authentication + approved list
    options.FallbackPolicy = new AuthorizationPolicyBuilder()
        .RequireAuthenticatedUser()
        .AddRequirements(new ApprovedListRequirement())
        .Build();
    
    // Named policy for explicit use
    options.AddPolicy("ApprovedUser", policy =>
    {
        policy.RequireAuthenticatedUser();
        policy.AddRequirements(new ApprovedListRequirement());
    });
});

// Add SignalR with balanced timeouts for long-running operations
builder.Services.AddSignalR(options =>
{
    options.EnableDetailedErrors = builder.Environment.IsDevelopment();
    options.MaximumReceiveMessageSize = 1024 * 1024; // 1MB
    
    // Balanced timeout settings for stability
    options.ClientTimeoutInterval = TimeSpan.FromMinutes(2); // How long server waits for client ping
    options.KeepAliveInterval = TimeSpan.FromSeconds(15); // How often server pings client
    options.HandshakeTimeout = TimeSpan.FromSeconds(30); // Initial connection timeout
});

// Add Razor Pages
builder.Services.AddRazorPages();

// Configure JSON serialization to handle string enums
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// Configure rate limiting - Policy-based for API endpoints only
builder.Services.AddRateLimiter(options =>
{
    // Policy for API endpoints - 50 requests per minute per user/IP
    options.AddPolicy("ApiPolicy", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: GetUserEmail(context) ?? context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 50,
                Window = TimeSpan.FromSeconds(60),
                QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                QueueLimit = 10
            }));
    
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    
    // Add logging for rate limit rejections
    options.OnRejected = async (context, token) =>
    {
        var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
        var userEmail = GetUserEmail(context.HttpContext);
        var ipAddress = context.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
        
        logger.LogWarning(
            "⚠️ Rate limit exceeded: Path={Path}, User={User}, IP={IP}, Time={Time}",
            context.HttpContext.Request.Path,
            userEmail ?? "anonymous",
            ipAddress,
            DateTime.UtcNow);
        
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        await context.HttpContext.Response.WriteAsync(
            "Too many requests. Please try again later.", 
            cancellationToken: token);
    };
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

// Validate Native Agent configuration at startup
var loggerFactory = LoggerFactory.Create(config => config.AddConsole());
var startupLogger = loggerFactory.CreateLogger("Startup");

ValidateNativeAgentConfiguration(dxoConfig, startupLogger);



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
            "default-src 'self'; " +
            "script-src 'self' 'unsafe-inline' 'unsafe-eval' https://cdnjs.cloudflare.com; " +
            "style-src 'self' 'unsafe-inline' https://fonts.googleapis.com; " +
            "font-src 'self' https://fonts.gstatic.com; " +
            "img-src 'self' data:; " +
            "connect-src 'self' ws: wss:;");
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
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Handle authorization failures - sign out user and redirect to Landing with error
app.Use(async (context, next) =>
{
    await next();
    
    if (context.Response.StatusCode == 403 && !context.Response.HasStarted)
    {
        // Sign out the user to prevent redirect loop
        if (context.User?.Identity?.IsAuthenticated == true)
        {
            var logger = context.RequestServices.GetRequiredService<ILogger<Program>>();
            var userEmail = context.User.FindFirst(ClaimTypes.Email)?.Value 
                ?? context.User.FindFirst("email")?.Value 
                ?? "unknown";
            
            logger.LogInformation("Signing out unauthorized user: {Email}", userEmail);
            
            await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(
                context, 
                CookieAuthenticationDefaults.AuthenticationScheme);
        }
        
        context.Response.Redirect("/Landing?authError=denied");
    }
});

app.MapRazorPages();
app.MapHub<DxoHub>("/hubs/dxo");

// Map API endpoints with rate limiting
app.MapPost("/api/session/create", async (HttpContext httpContext, CreateSessionRequest request, IOrchestrationService orchestration, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    request.UserEmail = userEmail;
    var session = await orchestration.CreateSessionAsync(request, ct);
    return Results.Ok(session);
}).RequireRateLimiting("ApiPolicy");

app.MapGet("/api/session/{id:guid}", async (Guid id, IOrchestrationService orchestration, CancellationToken ct) =>
{
    var session = await orchestration.GetSessionAsync(id, ct);
    return session != null ? Results.Ok(SessionDto.FromSession(session)) : Results.NotFound();
}).RequireRateLimiting("ApiPolicy");

app.MapGet("/api/sessions", async (IOrchestrationService orchestration, CancellationToken ct) =>
{
    var sessions = await orchestration.GetSessionsAsync(ct);
    return Results.Ok(sessions);
}).RequireRateLimiting("ApiPolicy");

app.MapPost("/api/session/{id:guid}/start", async (HttpContext httpContext, Guid id, IOrchestrationService orchestration, IModelManagementService modelService, CancellationToken ct) =>
{
    try
    {
        var userEmail = GetUserEmail(httpContext);
        if (userEmail == null)
            return Results.Unauthorized();

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

        // Validate keys for used models with user context
        var missing = new List<string>();
        foreach (var modelName in models)
        {
            var config = await modelService.GetModelConfigurationAsync(userEmail, modelName);
            if (config == null || string.IsNullOrWhiteSpace(config.ApiKey))
            {
                missing.Add(modelName);
            }
        }
        
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
}).RequireRateLimiting("ApiPolicy");

app.MapPost("/api/session/{id:guid}/step", async (HttpContext httpContext, Guid id, IOrchestrationService orchestration, IModelManagementService modelService, CancellationToken ct) =>
{
    try
    {
        var userEmail = GetUserEmail(httpContext);
        if (userEmail == null)
            return Results.Unauthorized();

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

        // Validate keys for used models with user context
        var missing = new List<string>();
        foreach (var modelName in models)
        {
            var config = await modelService.GetModelConfigurationAsync(userEmail, modelName);
            if (config == null || string.IsNullOrWhiteSpace(config.ApiKey))
            {
                missing.Add(modelName);
            }
        }
        
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
}).RequireRateLimiting("ApiPolicy");

app.MapPost("/api/session/{id:guid}/stop", async (Guid id, IOrchestrationService orchestration, CancellationToken ct) =>
{
    await orchestration.StopSessionAsync(id, ct);
    return Results.Ok(new { success = true });
}).RequireRateLimiting("ApiPolicy");

app.MapDelete("/api/session/{id:guid}", async (Guid id, IOrchestrationService orchestration, CancellationToken ct) =>
{
    await orchestration.DeleteSessionAsync(id, ct);
    return Results.Ok(new { success = true });
}).RequireRateLimiting("ApiPolicy");

app.MapPost("/api/session/{id:guid}/reset-memory/{persona}", async (Guid id, string persona, IOrchestrationService orchestration, CancellationToken ct) =>
{
    if (!Enum.TryParse<DXO.Models.Persona>(persona, true, out var personaEnum))
    {
        return Results.BadRequest(new { error = "Invalid persona. Use 'Creator', 'Reviewer1', or 'Reviewer2'." });
    }
    
    await orchestration.ResetPersonaMemoryAsync(id, personaEnum, ct);
    return Results.Ok(new { success = true });
}).RequireRateLimiting("ApiPolicy");

app.MapGet("/api/session/{id:guid}/feedback-rounds", async (Guid id, IOrchestrationService orchestration, CancellationToken ct) =>
{
    var session = await orchestration.GetSessionAsync(id, ct);
    if (session == null)
    {
        return Results.NotFound(new { error = "Session not found" });
    }
    
    var feedbackRounds = await orchestration.GetFeedbackRoundsAsync(id, ct);
    return Results.Ok(feedbackRounds);
}).RequireRateLimiting("ApiPolicy");

app.MapPost("/api/session/{id:guid}/feedback", async (Guid id, DXO.Models.SubmitFeedbackRequest? request, IOrchestrationService orchestration, CancellationToken ct) =>
{
    if (request == null)
    {
        return Results.BadRequest(new { error = "Request body is required" });
    }
    
    if (string.IsNullOrWhiteSpace(request.Feedback))
    {
        return Results.BadRequest(new { error = "Feedback cannot be empty" });
    }
    
    if (request.Iteration < 1)
    {
        return Results.BadRequest(new { error = "Iteration must be greater than 0" });
    }
    
    try
    {
        await orchestration.SubmitUserFeedbackAsync(id, request.Iteration, request.Feedback, ct);
        return Results.Ok(new { success = true });
    }
    catch (InvalidOperationException ex)
    {
        return Results.NotFound(new { error = ex.Message });
    }
}).RequireRateLimiting("ApiPolicy");

app.MapPost("/api/session/{id:guid}/iterate-with-feedback", async (HttpContext httpContext, Guid id, IterateWithFeedbackRequest? request, IOrchestrationService orchestration, IModelManagementService modelService, CancellationToken ct) =>
{
    if (request == null)
    {
        return Results.BadRequest(new { error = "Request body is required" });
    }
    
    if (string.IsNullOrWhiteSpace(request.Comments))
    {
        return Results.BadRequest(new { error = "Feedback comments cannot be empty" });
    }
    
    if (request.MaxAdditionalIterations < 1 || request.MaxAdditionalIterations > 3)
    {
        return Results.BadRequest(new { error = "Max additional iterations must be between 1 and 3" });
    }
    
    try
    {
        var userEmail = GetUserEmail(httpContext);
        if (userEmail == null)
            return Results.Unauthorized();

        var session = await orchestration.GetSessionAsync(id, ct);
        if (session == null)
            return Results.NotFound(new { error = "Session not found" });

        // Validate session is in completed state
        if (session.Status != SessionStatus.Completed)
        {
            return Results.BadRequest(new { error = "Can only iterate on completed sessions" });
        }

        // Collect distinct models used by this session
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
            return Results.BadRequest(new { error = $"Unable to parse session configuration: {ex.Message}" });
        }

        // Validate API keys for used models with user context
        var missing = new List<string>();
        foreach (var modelName in models)
        {
            var config = await modelService.GetModelConfigurationAsync(userEmail, modelName);
            if (config == null || string.IsNullOrWhiteSpace(config.ApiKey))
            {
                missing.Add(modelName);
            }
        }
        
        if (missing.Any())
        {
            return Results.BadRequest(new
            {
                error = $"Missing API key(s) for models: {string.Join(", ", missing)}. Please configure corresponding API key(s) in Settings."
            });
        }

        // Process the feedback iteration request
        var updatedSession = await orchestration.IterateWithFeedbackAsync(
            id,
            request.Comments,
            request.Tone,
            request.Length,
            request.Audience,
            request.MaxAdditionalIterations,
            ct
        );

        // Return DTO to avoid circular reference
        return Results.Ok(SessionDto.FromSession(updatedSession));
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
});

app.MapGet("/api/config", async (HttpContext httpContext, IOptions<DxoOptions> options, IModelManagementService modelService) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    var config = options.Value;
    var models = await modelService.GetAllModelsAsync(userEmail);
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
}).RequireRateLimiting("ApiPolicy");

// Model Management API endpoints
app.MapGet("/api/models", async (HttpContext httpContext, IModelManagementService modelService, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    var models = await modelService.GetAllModelsAsync(userEmail);
    return Results.Ok(models);
}).RequireRateLimiting("ApiPolicy");

app.MapGet("/api/models/status", async (HttpContext httpContext, IModelManagementService modelService, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    var models = await modelService.GetAllModelsAsync(userEmail);
    
    // Count models that have all required fields configured
    var configuredCount = models.Count(m => 
        !string.IsNullOrWhiteSpace(m.ModelName) && 
        !string.IsNullOrWhiteSpace(m.Endpoint) && 
        !string.IsNullOrWhiteSpace(m.ApiKey));
    
    var totalCount = models.Count;
    
    return Results.Ok(new 
    { 
        configured = configuredCount,
        total = totalCount,
        allConfigured = configuredCount == totalCount && totalCount > 0
    });
}).RequireRateLimiting("ApiPolicy");

app.MapGet("/api/models/{id:int}", async (HttpContext httpContext, int id, IModelManagementService modelService, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    var models = await modelService.GetAllModelsAsync(userEmail);
    var model = models.FirstOrDefault(m => m.Id == id);
    return model != null ? Results.Ok(model) : Results.NotFound();
}).RequireRateLimiting("ApiPolicy");

app.MapPost("/api/models", async (HttpContext httpContext, AddModelRequest request, IModelManagementService modelService, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    try
    {
        // Support both Provider (new) and IsAzureModel (legacy) for backward compatibility
        var provider = !string.IsNullOrWhiteSpace(request.Provider)
            ? ModelProviderExtensions.FromString(request.Provider)
            : (request.IsAzureModel ? ModelProvider.Azure : ModelProvider.OpenAI);
        
        var model = await modelService.AddModelAsync(
            userEmail,
            request.ModelName,
            request.Endpoint,
            provider,
            request.ApiKey,
            request.DisplayName
        );

        return Results.Ok(model);
    }
    catch (InvalidOperationException ex)
    {
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("ApiPolicy");

app.MapPut("/api/models/{id:int}", async (HttpContext httpContext, int id, UpdateModelRequest request, IModelManagementService modelService, ILogger<Program> logger, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    logger.LogInformation("[API] PUT /api/models/{Id} called for user {UserEmail}. ModelName: '{ModelName}', HasApiKey: {HasApiKey}", 
        id, userEmail, request.ModelName, !string.IsNullOrWhiteSpace(request.ApiKey));
    
    try
    {
        // Support both Provider (new) and IsAzureModel (legacy) for backward compatibility
        var provider = !string.IsNullOrWhiteSpace(request.Provider)
            ? ModelProviderExtensions.FromString(request.Provider)
            : (request.IsAzureModel ? ModelProvider.Azure : ModelProvider.OpenAI);
        
        var model = await modelService.UpdateModelAsync(
            userEmail,
            id,
            request.ModelName,
            request.Endpoint,
            provider,
            request.ApiKey,
            request.DisplayName
        );

        logger.LogInformation("[API] Model updated successfully. ID: {Id}, Name: '{ModelName}' for user {UserEmail}", 
            model.Id, model.ModelName, userEmail);

        return Results.Ok(model);
    }
    catch (InvalidOperationException ex)
    {
        logger.LogError("[API] Failed to update model for user {UserEmail}: {Error}", userEmail, ex.Message);
        return Results.BadRequest(new { error = ex.Message });
    }
}).RequireRateLimiting("ApiPolicy");

app.MapDelete("/api/models/{id:int}", async (HttpContext httpContext, int id, IModelManagementService modelService, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    var deleted = await modelService.DeleteModelAsync(userEmail, id);
    return deleted ? Results.Ok(new { success = true }) : Results.NotFound();
}).RequireRateLimiting("ApiPolicy");

// Native Agent Override endpoints
app.MapPost("/api/native-agent/override", async (HttpContext httpContext, SaveNativeAgentOverrideRequest request, IModelManagementService modelService, DxoDbContext dbContext, ILogger<Program> logger, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    try
    {
        // Update or create user settings with the selected model
        var userSettings = await dbContext.UserSettings.FirstOrDefaultAsync(u => u.UserId == userEmail, ct);
        if (userSettings == null)
        {
            userSettings = new UserSettings { UserId = userEmail, NativeAgentModelId = request.ModelId };
            dbContext.UserSettings.Add(userSettings);
        }
        else
        {
            userSettings.NativeAgentModelId = request.ModelId;
        }
        
        await dbContext.SaveChangesAsync(ct);
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error saving Native Agent override for user {UserEmail}", userEmail);
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
}).RequireRateLimiting("ApiPolicy");

app.MapDelete("/api/native-agent/override", async (HttpContext httpContext, DxoDbContext dbContext, ILogger<Program> logger, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    try
    {
        var userSettings = await dbContext.UserSettings.FirstOrDefaultAsync(u => u.UserId == userEmail, ct);
        if (userSettings != null)
        {
            userSettings.NativeAgentModelId = null;
            await dbContext.SaveChangesAsync(ct);
        }
        
        return Results.Ok(new { success = true });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error removing Native Agent override for user {UserEmail}", userEmail);
        return Results.BadRequest(new { success = false, error = ex.Message });
    }
}).RequireRateLimiting("ApiPolicy");

app.MapPost("/api/native-agent/test", async (HttpContext httpContext, IReviewerRecommendationService recommendationService, ILogger<Program> logger, CancellationToken ct) =>
{
    var userEmail = GetUserEmail(httpContext);
    if (userEmail == null)
        return Results.Unauthorized();
    
    try
    {
        // Try to get Native Agent status - if it returns configured=true, connection is valid
        var (configured, usingDefault, modelName) = await recommendationService.GetNativeAgentStatusAsync(userEmail);
        
        if (!configured)
        {
            return Results.Ok(new { success = false, error = "Native Agent is not configured" });
        }
        
        return Results.Ok(new { success = true, modelName });
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Error testing Native Agent connection for user {UserEmail}", userEmail);
        return Results.Ok(new { success = false, error = ex.Message });
    }
}).RequireRateLimiting("ApiPolicy");

app.Run();
// Helper method to extract user email from claims
static string? GetUserEmail(HttpContext context)
{
    return context.User.FindFirst(ClaimTypes.Email)?.Value 
        ?? context.User.FindFirst("preferred_username")?.Value 
        ?? context.User.FindFirst("email")?.Value
        ?? context.User.FindFirst(ClaimTypes.Name)?.Value;
}

// Retry policy for transient HTTP errors
static IAsyncPolicy<HttpResponseMessage> GetRetryPolicy(int maxRetries)
{
    return HttpPolicyExtensions
        .HandleTransientHttpError()
        .OrResult(msg => msg.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
        .WaitAndRetryAsync(maxRetries, retryAttempt => 
            TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
}

// Validate Native Agent configuration at startup
static void ValidateNativeAgentConfiguration(DxoOptions options, ILogger logger)
{
    if (!options.NativeAgent.Enabled)
    {
        logger.LogInformation("ℹ️  Native Agent is disabled");
        return;
    }

    var issues = new List<string>();

    if (string.IsNullOrWhiteSpace(options.NativeAgent.Endpoint))
    {
        issues.Add("Endpoint is not configured");
    }

    if (string.IsNullOrWhiteSpace(options.NativeAgent.ModelName))
    {
        issues.Add("ModelName is not configured");
    }

    // Validate ModelProvider enum value
    try
    {
        var provider = ModelProviderExtensions.FromString(options.NativeAgent.ModelProvider);
        logger.LogInformation("ℹ️  Native Agent configured with provider: {Provider}", provider.ToDisplayString());
    }
    catch
    {
        issues.Add($"Invalid ModelProvider: {options.NativeAgent.ModelProvider}");
    }

    if (issues.Any())
    {
        logger.LogWarning(
            "⚠️  Native Agent configuration incomplete - AI reviewer recommendations will not be available. Issues: {Issues}",
            string.Join("; ", issues));
    }
    else
    {
        logger.LogInformation("✅ Native Agent configuration validated successfully");
    }
}

// Helper classes for API requests
class SaveNativeAgentOverrideRequest
{
    public int ModelId { get; set; }
}
