using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;

namespace DXO.Services.Authorization;

/// <summary>
/// Authorization handler that checks if user is in the approved list.
/// Includes comprehensive debug logging for troubleshooting.
/// </summary>
public class ApprovedListAuthorizationHandler : AuthorizationHandler<ApprovedListRequirement>
{
    private readonly IApprovedListService _approvedListService;
    private readonly ILogger<ApprovedListAuthorizationHandler> _logger;

    public ApprovedListAuthorizationHandler(
        IApprovedListService approvedListService,
        ILogger<ApprovedListAuthorizationHandler> logger)
    {
        _approvedListService = approvedListService;
        _logger = logger;
    }

    protected override async Task HandleRequirementAsync(
        AuthorizationHandlerContext context,
        ApprovedListRequirement requirement)
    {
        // Extract user identity information for debugging
        var user = context.User;
        
        if (user?.Identity?.IsAuthenticated != true)
        {
            _logger.LogWarning("[AUTH-DEBUG] User is not authenticated");
            context.Fail();
            return;
        }

        // Determine the identity provider
        var identityProvider = GetIdentityProvider(user);
        
        // Try to extract email from claims with fallback chain
        var (claimType, email) = ExtractEmailFromClaims(user);

        // Log comprehensive debug information
        LogAuthorizationAttempt(identityProvider, claimType, email);

        // Check if email was found
        if (string.IsNullOrWhiteSpace(email))
        {
            _logger.LogWarning(
                "[AUTH-DEBUG] Authorization FAILED - No email claim found\n" +
                "  Provider: {Provider}\n" +
                "  Claim Type: MISSING\n" +
                "  Result: FAIL - No email claim",
                identityProvider
            );
            context.Fail();
            return;
        }

        // Check if user is approved
        var isWildcard = await _approvedListService.IsWildcardEnabledAsync();
        var isApproved = await _approvedListService.IsUserApprovedAsync(email);

        // Log the result
        LogAuthorizationResult(identityProvider, claimType, email, isWildcard, isApproved);

        if (isApproved)
        {
            context.Succeed(requirement);
        }
        else
        {
            context.Fail();
        }
    }

    private string GetIdentityProvider(ClaimsPrincipal user)
    {
        // Try to determine the identity provider from claims
        var idpClaim = user.FindFirst("idp")?.Value 
            ?? user.FindFirst("http://schemas.microsoft.com/identity/claims/identityprovider")?.Value;

        if (!string.IsNullOrEmpty(idpClaim))
        {
            // Map common IdP values to friendly names
            if (idpClaim.Contains("microsoft", StringComparison.OrdinalIgnoreCase) ||
                idpClaim.Contains("live.com", StringComparison.OrdinalIgnoreCase))
                return "Microsoft Account";
            
            if (idpClaim.Contains("google", StringComparison.OrdinalIgnoreCase))
                return "Google";
            
            if (idpClaim.Contains("login.microsoftonline", StringComparison.OrdinalIgnoreCase))
                return "Entra ID";
                
            return idpClaim;
        }

        // Fallback: check the authentication scheme
        var authType = user.Identity?.AuthenticationType;
        if (!string.IsNullOrEmpty(authType))
        {
            if (authType.Contains("Google", StringComparison.OrdinalIgnoreCase))
                return "Google";
            if (authType.Contains("Microsoft", StringComparison.OrdinalIgnoreCase))
                return "Microsoft Account";
            if (authType.Contains("OpenIdConnect", StringComparison.OrdinalIgnoreCase))
                return "Entra ID (OIDC)";
                
            return authType;
        }

        return "Unknown";
    }

    private (string ClaimType, string? Email) ExtractEmailFromClaims(ClaimsPrincipal user)
    {
        // Priority order for email claim types
        var emailClaimTypes = new[]
        {
            ClaimTypes.Email,
            "email",
            "preferred_username",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress",
            "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/upn",
            ClaimTypes.Upn
        };

        foreach (var claimType in emailClaimTypes)
        {
            var claim = user.FindFirst(claimType);
            if (claim != null && !string.IsNullOrWhiteSpace(claim.Value))
            {
                // Validate it looks like an email
                if (claim.Value.Contains('@'))
                {
                    return (claimType, claim.Value);
                }
            }
        }

        return ("MISSING", null);
    }

    private void LogAuthorizationAttempt(string provider, string claimType, string? email)
    {
        var emailDisplay = string.IsNullOrWhiteSpace(email) ? "N/A" : email;
        
        _logger.LogInformation(
            "[AUTH-DEBUG] User authorization attempt\n" +
            "  Provider: {Provider}\n" +
            "  Claim Type: {ClaimType}\n" +
            "  Email: {Email}",
            provider,
            claimType,
            emailDisplay
        );
    }

    private void LogAuthorizationResult(
        string provider,
        string claimType,
        string email,
        bool isWildcard,
        bool isApproved)
    {
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var result = isApproved ? "PASS" : "FAIL - Not in approved list";
        
        _logger.LogInformation(
            "[AUTH-DEBUG] Authorization result\n" +
            "  Provider: {Provider}\n" +
            "  Claim Type: {ClaimType}\n" +
            "  Email (Raw): {RawEmail}\n" +
            "  Email (Normalized): {NormalizedEmail}\n" +
            "  Wildcard Enabled: {Wildcard}\n" +
            "  In Approved List: {InList}\n" +
            "  Result: {Result}",
            provider,
            claimType,
            email,
            normalizedEmail,
            isWildcard,
            isApproved,
            result
        );
    }
}
