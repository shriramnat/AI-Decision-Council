namespace DXO.Configuration;

public class AuthOptions
{
    public const string SectionName = "Authentication";

    /// <summary>
    /// When true, full authentication with OIDC providers is required.
    /// When false, users can access the app with a test account for development/testing.
    /// Default: true (authentication required)
    /// </summary>
    public bool Enabled { get; set; } = true;

    public EntraIdOptions EntraId { get; set; } = new();
    public MicrosoftAccountOptions MicrosoftAccount { get; set; } = new();
    public GoogleOptions Google { get; set; } = new();
    public CookieOptions Cookie { get; set; } = new();
}

public class EntraIdOptions
{
    public bool Enabled { get; set; } = true;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string TenantId { get; set; } = string.Empty;
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string CallbackPath { get; set; } = "/signin-oidc";
}

public class MicrosoftAccountOptions
{
    public bool Enabled { get; set; } = true;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string CallbackPath { get; set; } = "/signin-microsoft";
}

public class GoogleOptions
{
    public bool Enabled { get; set; } = true;
    public string ClientId { get; set; } = string.Empty;
    public string ClientSecret { get; set; } = string.Empty;
    public string CallbackPath { get; set; } = "/signin-google";
}

public class CookieOptions
{
    public int ExpireTimeMinutes { get; set; } = 30;
}

public class ApprovedListOptions
{
    public List<string> ApprovedUsers { get; set; } = new();
    public string? LastModified { get; set; }
    public string? Comments { get; set; }
    
    public bool IsWildcard => ApprovedUsers.Any(u => u.Trim() == "*");
}
