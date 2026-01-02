using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Options;
using DXO.Configuration;

namespace DXO.Pages;

[AllowAnonymous]
[IgnoreAntiforgeryToken]
public class LandingModel : PageModel
{
    private readonly ILogger<LandingModel> _logger;
    private readonly AuthOptions _authOptions;

    [TempData]
    public string? ErrorMessage { get; set; }

    public bool AuthenticationEnabled { get; set; }
    public bool EntraIdEnabled { get; set; }
    public bool MicrosoftAccountEnabled { get; set; }
    public bool GoogleEnabled { get; set; }

    public LandingModel(ILogger<LandingModel> logger, IOptions<AuthOptions> authOptions)
    {
        _logger = logger;
        _authOptions = authOptions.Value;
    }

    public async Task OnGetAsync()
    {
        AuthenticationEnabled = _authOptions.Enabled;
        EntraIdEnabled = _authOptions.EntraId.Enabled;
        MicrosoftAccountEnabled = _authOptions.MicrosoftAccount.Enabled;
        GoogleEnabled = _authOptions.Google.Enabled;
        
        // Check if there's an auth error in query string
        if (Request.Query.ContainsKey("authError"))
        {
            // If user is authenticated but got access denied, sign them out to prevent redirect loop
            if (User?.Identity?.IsAuthenticated == true)
            {
                var userEmail = User.FindFirst(ClaimTypes.Email)?.Value 
                    ?? User.FindFirst("email")?.Value 
                    ?? User.FindFirst("preferred_username")?.Value 
                    ?? "unknown";
                
                await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                
                _logger.LogInformation("User signed out due to authorization failure: {Email}", userEmail);
            }
            
            // Clear all cookies to ensure clean state for next authentication attempt
            foreach (var cookie in Request.Cookies.Keys)
            {
                Response.Cookies.Delete(cookie);
            }
            
            ErrorMessage = "You are not authorized to access this application. If you believe this is an error, please contact the administrator.";
        }
    }

    public async Task<IActionResult> OnPostAccessApp(string? returnUrl = null)
    {
        // Only allow this if authentication is disabled
        if (_authOptions.Enabled)
        {
            _logger.LogWarning("Attempt to use test account when authentication is enabled");
            return RedirectToPage("/Landing", new { authError = "forbidden" });
        }

        returnUrl ??= Url.Content("~/");

        // Create claims for the test user
        var claims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, "Test User"),
            new Claim(ClaimTypes.Email, "unauthenticateduser@decision.council"),
            new Claim("preferred_username", "unauthenticateduser@decision.council")
        };

        var claimsIdentity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);

        // Sign in the user
        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            claimsPrincipal,
            new AuthenticationProperties
            {
                IsPersistent = false,
                ExpiresUtc = DateTimeOffset.UtcNow.AddMinutes(_authOptions.Cookie.ExpireTimeMinutes)
            });

        _logger.LogInformation("Test user signed in: unauthenticateduser@decision.council");

        return LocalRedirect(returnUrl);
    }

    public IActionResult OnPostSignInEntraId(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        
        var properties = new AuthenticationProperties
        {
            RedirectUri = returnUrl
        };

        return Challenge(properties, "EntraId");
    }

    public IActionResult OnPostSignInMicrosoft(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        
        var properties = new AuthenticationProperties
        {
            RedirectUri = returnUrl
        };

        return Challenge(properties, "Microsoft");
    }

    public IActionResult OnPostSignInGoogle(string? returnUrl = null)
    {
        returnUrl ??= Url.Content("~/");
        
        var properties = new AuthenticationProperties
        {
            RedirectUri = returnUrl
        };

        return Challenge(properties, "Google");
    }
}