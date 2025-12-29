# Production Logging Implementation Guide

## Overview

This guide provides a comprehensive implementation plan for adding production-grade logging to the AI Decision Council application using modern .NET logging practices.

## Current State

**What exists:**
- Basic ASP.NET Core logging (`ILogger<T>`)
- Console logging
- Log levels configured in `appsettings.json`
- Some custom logging in authorization handler

**What's missing:**
- Structured logging
- Persistent log storage
- Centralized log aggregation
- Request correlation
- Performance metrics
- Health checks

---

## Recommended Implementation: Serilog + Application Insights

### Phase 1: Add Serilog (2-3 hours)

#### Step 1: Install NuGet Packages

```bash
cd DXO
dotnet add package Serilog.AspNetCore
dotnet add package Serilog.Sinks.Console
dotnet add package Serilog.Sinks.File
dotnet add package Serilog.Enrichers.Environment
dotnet add package Serilog.Enrichers.Thread
dotnet add package Serilog.Settings.Configuration
dotnet add package Serilog.Expressions
```

#### Step 2: Update Program.cs

Add at the top of `Program.cs` (before `var builder = WebApplication.CreateBuilder(args);`):

```csharp
using Serilog;
using Serilog.Events;

// Configure Serilog
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithThreadId()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/dxo-.log",
        rollingInterval: RollingInterval.Day,
        rollOnFileSizeLimit: true,
        fileSizeLimitBytes: 10485760, // 10MB
        retainedFileCountLimit: 30,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

try
{
    Log.Information("Starting AI Decision Council application");
```

Replace the builder creation with:

```csharp
var builder = WebApplication.CreateBuilder(args);

// Add Serilog
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .ReadFrom.Services(services)
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .WriteTo.File(
        path: "logs/dxo-.log",
        rollingInterval: RollingInterval.Day,
        rollOnFileSizeLimit: true,
        fileSizeLimitBytes: 10485760,
        retainedFileCountLimit: 30));
```

At the end of `Program.cs`, wrap `app.Run()`:

```csharp
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}
```

Add request logging middleware before `app.MapRazorPages()`:

```csharp
// Add Serilog request logging
app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.0000} ms";
    options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
    {
        diagnosticContext.Set("RequestHost", httpContext.Request.Host.Value);
        diagnosticContext.Set("RequestScheme", httpContext.Request.Scheme);
        diagnosticContext.Set("UserAgent", httpContext.Request.Headers["User-Agent"].ToString());
        
        if (httpContext.User.Identity?.IsAuthenticated == true)
        {
            diagnosticContext.Set("UserEmail", 
                httpContext.User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value ?? 
                httpContext.User.FindFirst("preferred_username")?.Value ?? "Unknown");
        }
    };
});
```

#### Step 3: Update appsettings.json

Add Serilog configuration section:

```json
{
  "Serilog": {
    "Using": [ "Serilog.Sinks.Console", "Serilog.Sinks.File" ],
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "Microsoft.AspNetCore": "Warning",
        "Microsoft.EntityFrameworkCore": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      {
        "Name": "Console",
        "Args": {
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}"
        }
      },
      {
        "Name": "File",
        "Args": {
          "path": "logs/dxo-.log",
          "rollingInterval": "Day",
          "rollOnFileSizeLimit": true,
          "fileSizeLimitBytes": 10485760,
          "retainedFileCountLimit": 30,
          "outputTemplate": "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] {Message:lj}{NewLine}{Exception}"
        }
      }
    ],
    "Enrich": [ "FromLogContext", "WithMachineName", "WithThreadId" ]
  }
}
```

#### Step 4: Add .gitignore entry for logs

Add to `.gitignore`:

```
# Logs
logs/
*.log
```

---

### Phase 2: Add Structured Logging to Services (3-4 hours)

#### Authentication Events

Update `Landing.cshtml.cs`:

```csharp
public async Task<IActionResult> OnPostAccessApp(string? returnUrl = null)
{
    if (_authOptions.Enabled)
    {
        _logger.LogWarning("Unauthorized attempt to use test account when authentication is enabled from IP {IPAddress}", 
            HttpContext.Connection.RemoteIpAddress);
        return RedirectToPage("/Landing", new { authError = "forbidden" });
    }

    _logger.LogInformation("Test user authentication - signing in as {Email}", "unauthenticateduser@decision.council");

    // ... rest of the code

    _logger.LogInformation("Test user {Email} successfully authenticated", "unauthenticateduser@decision.council");
    return LocalRedirect(returnUrl);
}
```

#### Authorization Events

Update `ApprovedListAuthorizationHandler.cs`:

```csharp
protected override async Task HandleRequirementAsync(
    AuthorizationHandlerContext context,
    ApprovedListRequirement requirement)
{
    var email = context.User.FindFirst(ClaimTypes.Email)?.Value 
        ?? context.User.FindFirst("email")?.Value 
        ?? context.User.FindFirst("preferred_username")?.Value;

    if (string.IsNullOrEmpty(email))
    {
        _logger.LogWarning("Authorization failed - no email claim found for user {UserName}", 
            context.User.Identity?.Name ?? "Unknown");
        return;
    }

    var approvedList = await _approvedListService.GetApprovedListAsync();

    if (approvedList.IsWildcard)
    {
        _logger.LogDebug("User {Email} authorized via wildcard (*)", email);
        context.Succeed(requirement);
        return;
    }

    if (approvedList.ApprovedUsers.Any(u => 
        string.Equals(u, email, StringComparison.OrdinalIgnoreCase)))
    {
        _logger.LogInformation("User {Email} authorized - found in approved list", email);
        context.Succeed(requirement);
    }
    else
    {
        _logger.LogWarning("User {Email} denied - not in approved list. IP: {IPAddress}", 
            email, 
            _httpContextAccessor.HttpContext?.Connection.RemoteIpAddress);
    }
}
```

#### Orchestration Service

Update `OrchestrationService.cs` to add logging:

```csharp
public async Task<Session> CreateSessionAsync(CreateSessionRequest request, CancellationToken ct)
{
    _logger.LogInformation("Creating new session with {ReviewerCount} reviewers, MaxIterations: {MaxIterations}", 
        request.Reviewers?.Count ?? 0, 
        request.MaxIterations);

    // ... existing code

    await _dbContext.SaveChangesAsync(ct);
    
    _logger.LogInformation("Session {SessionId} created successfully", session.Id);
    return session;
}

public async Task StartSessionAsync(Guid sessionId, CancellationToken ct)
{
    _logger.LogInformation("Starting session {SessionId}", sessionId);
    
    // ... existing code
    
    _logger.LogInformation("Session {SessionId} started with initial prompt length: {PromptLength}", 
        sessionId, 
        session.InitialPrompt?.Length ?? 0);
}
```

#### Model API Calls

Update `OpenAIService.cs`:

```csharp
public async Task<string> CallModelAsync(string model, string systemPrompt, string userPrompt, CancellationToken ct)
{
    var startTime = DateTime.UtcNow;
    
    _logger.LogInformation("Calling OpenAI model {Model}, Prompt length: {PromptLength}", 
        model, 
        userPrompt.Length);

    try
    {
        // ... existing API call code
        
        var elapsed = (DateTime.UtcNow - startTime).TotalMilliseconds;
        _logger.LogInformation("Model {Model} responded in {ElapsedMs}ms, Response length: {ResponseLength}", 
            model, 
            elapsed, 
            response.Length);
        
        return response;
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Failed to call model {Model} after {ElapsedMs}ms", 
            model, 
            (DateTime.UtcNow - startTime).TotalMilliseconds);
        throw;
    }
}
```

---

### Phase 3: Add Application Insights (Optional, 1-2 hours)

#### For Azure-hosted applications:

```bash
dotnet add package Microsoft.ApplicationInsights.AspNetCore
```

Add to `Program.cs`:

```csharp
builder.Services.AddApplicationInsightsTelemetry(options =>
{
    options.ConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
});
```

Add to `appsettings.json`:

```json
{
  "ApplicationInsights": {
    "ConnectionString": "InstrumentationKey=your-key-here;IngestionEndpoint=https://..."
  }
}
```

---

### Phase 4: Add Health Checks (1 hour)

#### Install Package:

```bash
dotnet add package Microsoft.Extensions.Diagnostics.HealthChecks.EntityFrameworkCore
```

#### Add to Program.cs:

```csharp
// Before builder.Build()
builder.Services.AddHealthChecks()
    .AddDbContextCheck<DxoDbContext>("database")
    .AddCheck("self", () => HealthCheckResult.Healthy())
    .AddUrlGroup(new Uri("https://api.openai.com"), "OpenAI API", HealthStatus.Degraded);
```

After `app.UseAuthorization()`:

```csharp
app.MapHealthChecks("/health", new HealthCheckOptions
{
    ResponseWriter = async (context, report) =>
    {
        context.Response.ContentType = "application/json";
        var result = JsonSerializer.Serialize(new
        {
            status = report.Status.ToString(),
            checks = report.Entries.Select(e => new
            {
                name = e.Key,
                status = e.Value.Status.ToString(),
                description = e.Value.Description,
                duration = e.Value.Duration.ToString()
            })
        });
        await context.Response.WriteAsync(result);
    }
});

// Separate endpoint for liveness probe
app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = check => check.Name == "self"
});
```

---

## Log Analysis & Monitoring

### Local Development

View logs in:
- Console output
- `logs/` directory (rotating daily)

### Production Options

**Option 1: Seq (Recommended for self-hosted)**
- Free for single user
- Web-based log viewer
- Powerful querying
- Install: https://datalust.co/seq

```bash
dotnet add package Serilog.Sinks.Seq
```

Add to Serilog configuration:
```json
{
  "Name": "Seq",
  "Args": {
    "serverUrl": "http://localhost:5341"
  }
}
```

**Option 2: Application Insights (Azure)**
- Already integrated if using Azure
- Built-in dashboards
- Alerting capabilities
- View in Azure Portal

**Option 3: Elasticsearch + Kibana**
- Enterprise-grade
- Powerful analytics
- Self-hosted or cloud

---

## Security Considerations

### Sensitive Data Masking

Create a custom enricher to mask sensitive data:

```csharp
public class SensitiveDataEnricher : ILogEventEnricher
{
    public void Enrich(LogEvent logEvent, ILogEventPropertyFactory propertyFactory)
    {
        if (logEvent.Properties.TryGetValue("ApiKey", out var apiKeyValue))
        {
            var masked = MaskApiKey(apiKeyValue.ToString());
            logEvent.AddOrUpdateProperty(propertyFactory.CreateProperty("ApiKey", masked));
        }
    }
    
    private string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrEmpty(apiKey) || apiKey.Length < 8)
            return "***";
        return apiKey.Substring(0, 4) + "***" + apiKey.Substring(apiKey.Length - 4);
    }
}
```

### What NOT to Log

❌ **Never log:**
- API keys (mask them)
- Client secrets
- Passwords
- Full authentication tokens
- Credit card numbers
- Personal identifiable information (PII) without consent

✅ **Safe to log:**
- User email (if approved list check passed)
- Session IDs
- Request paths
- Response status codes
- Performance metrics
- Error messages (sanitized)

---

## Performance Considerations

### Async Logging

Serilog already uses async logging internally. For high-throughput scenarios:

```csharp
.WriteTo.Async(a => a.File("logs/dxo-.log", 
    rollingInterval: RollingInterval.Day,
    buffered: true))
```

### Log Level Guidelines

- **Trace**: Very detailed, rarely used
- **Debug**: Detailed flow for debugging (dev only)
- **Information**: General application flow
- **Warning**: Unexpected but recoverable issues
- **Error**: Failures that need attention
- **Critical/Fatal**: Application crash scenarios

### Production Log Levels

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    }
  }
}
```

---

## Testing the Implementation

### 1. Test Log Output

```csharp
// In any service
_logger.LogInformation("Test structured log: {Property1}, {Property2}", "value1", 123);
```

### 2. Verify Files

Check that logs appear in:
- Console
- `logs/dxo-YYYYMMDD.log`

### 3. Test Correlation

Make a request and verify all related logs share a correlation ID.

### 4. Test Health Checks

Visit:
- `https://localhost:5001/health` (full health check)
- `https://localhost:5001/health/live` (liveness only)

---

## Monitoring & Alerting

### Key Metrics to Monitor

1. **Authentication Failures**: > 10 per minute
2. **Authorization Denials**: Any spike
3. **API Call Failures**: > 5% failure rate
4. **Response Time**: > 2 seconds average
5. **Memory Usage**: > 80% of available
6. **Database Errors**: Any occurrence

### Sample Alert Queries (Seq)

```sql
-- Failed authentications
select count(*) from stream
where @Level = 'Warning' 
  and @Message like '%authentication failed%'
group by time(5m)
having count(*) > 10

-- Slow API calls
select * from stream
where @Message like '%Model%responded%'
  and ElapsedMs > 5000
```

---

## Deployment Checklist

- [ ] Serilog packages installed
- [ ] Program.cs updated with Serilog configuration
- [ ] appsettings.json configured for appropriate log levels
- [ ] All services have structured logging added
- [ ] Sensitive data is masked
- [ ] Health checks implemented
- [ ] Log retention policy configured
- [ ] Monitoring/alerting set up (if applicable)
- [ ] Team trained on accessing logs
- [ ] Log analysis tool selected and configured

---

## Maintenance

### Log Rotation

- Daily rotation configured
- 30 days retention (configurable)
- 10MB max file size

### Regular Reviews

- Weekly: Check error logs
- Monthly: Review log volume and storage
- Quarterly: Update log retention policy
- Annually: Review logging strategy

---

## Cost Estimates

### Self-Hosted (Serilog + Files)
- Storage: ~100MB/day = 3GB/month
- Cost: Minimal (local storage)

### Seq
- Free: Single user
- Team: $80/year
- Enterprise: Custom pricing

### Application Insights
- First 5GB/month: Free
- After: ~$2.30/GB
- Typical small app: ~$10-30/month

### Elasticsearch
- Self-hosted: Infrastructure costs
- Elastic Cloud: Starting at $45/month

---

## Additional Resources

- [Serilog Documentation](https://serilog.net/)
- [Application Insights](https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview)
- [ASP.NET Core Logging](https://learn.microsoft.com/aspnet/core/fundamentals/logging/)
- [Health Checks](https://learn.microsoft.com/aspnet/core/host-and-deploy/health-checks)

---

## Summary

This implementation provides:
- ✅ Structured logging with Serilog
- ✅ File-based log storage with rotation
- ✅ Request correlation
- ✅ Performance tracking
- ✅ Security event logging
- ✅ Health monitoring
- ✅ Production-ready configuration

**Estimated Implementation Time:** 6-10 hours
**Maintenance Overhead:** ~1 hour/month

**Next Steps:**
1. Start with Phase 1 (Serilog basics)
2. Test in development
3. Add service-specific logging (Phase 2)
4. Deploy to staging
5. Add monitoring solution (Phase 3/4)
6. Deploy to production
