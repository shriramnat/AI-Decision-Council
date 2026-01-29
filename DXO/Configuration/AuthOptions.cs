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
    
    /// <summary>
    /// Client secret for confidential client flow. 
    /// When null or empty, PKCE (Proof Key for Code Exchange) is used instead,
    /// allowing authentication without storing a client secret.
    /// </summary>
    public string? ClientSecret { get; set; } = null;
    
    public string TenantId { get; set; } = string.Empty;
    public string Instance { get; set; } = "https://login.microsoftonline.com/";
    public string CallbackPath { get; set; } = "/signin-oidc";
    
    /// <summary>
    /// When true, uses PKCE (Proof Key for Code Exchange) for secure authentication
    /// without requiring a client secret. This is the recommended approach for
    /// public clients or when you don't want to store secrets.
    /// Default: true (uses PKCE when ClientSecret is not provided)
    /// </summary>
    public bool UsePkce { get; set; } = true;
    
    /// <summary>
    /// The scopes to request during authentication. 
    /// Default includes openid, profile, and email for ID token claims.
    /// </summary>
    public List<string> Scopes { get; set; } = new() { "openid", "profile", "email" };
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
