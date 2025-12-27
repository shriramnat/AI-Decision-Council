using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace DXO.Pages;

[AllowAnonymous]
public class LogoutModel : PageModel
{
    private readonly ILogger<LogoutModel> _logger;

    public LogoutModel(ILogger<LogoutModel> logger)
    {
        _logger = logger;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        _logger.LogInformation("User logout initiated");

        // Sign out from cookie authentication
        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        
        // If signed in with OIDC providers, sign out from them too
        var schemes = new[] { "EntraId", "Microsoft", "Google" };
        foreach (var scheme in schemes)
        {
            try
            {
                await HttpContext.SignOutAsync(scheme);
            }
            catch
            {
                // Ignore errors if not signed in with this scheme
            }
        }

        _logger.LogInformation("User logged out successfully");

        return RedirectToPage("/Landing");
    }

    public async Task<IActionResult> OnPostAsync()
    {
        return await OnGetAsync();
    }
}
