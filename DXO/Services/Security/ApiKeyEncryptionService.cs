using Microsoft.AspNetCore.DataProtection;

namespace DXO.Services.Security;

/// <summary>
/// Service for encrypting and decrypting API keys using ASP.NET Core Data Protection API
/// </summary>
public interface IApiKeyEncryptionService
{
    /// <summary>
    /// Encrypts a plain text API key
    /// </summary>
    string Encrypt(string plainText);
    
    /// <summary>
    /// Decrypts an encrypted API key
    /// </summary>
    string Decrypt(string cipherText);
}

public class ApiKeyEncryptionService : IApiKeyEncryptionService
{
    private readonly IDataProtector _protector;
    private readonly ILogger<ApiKeyEncryptionService> _logger;

    public ApiKeyEncryptionService(
        IDataProtectionProvider provider,
        ILogger<ApiKeyEncryptionService> logger)
    {
        // Create a purpose-specific protector for API keys
        // This ensures keys encrypted for this purpose cannot be decrypted by other purposes
        _protector = provider.CreateProtector("DXO.ApiKeys.v1");
        _logger = logger;
    }

    public string Encrypt(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
        {
            return string.Empty;
        }

        try
        {
            var encrypted = _protector.Protect(plainText);
            _logger.LogDebug("Successfully encrypted API key (length: {Length})", plainText.Length);
            return encrypted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to encrypt API key");
            throw new InvalidOperationException("Failed to encrypt API key", ex);
        }
    }

    public string Decrypt(string cipherText)
    {
        if (string.IsNullOrEmpty(cipherText))
        {
            return string.Empty;
        }

        try
        {
            var decrypted = _protector.Unprotect(cipherText);
            _logger.LogDebug("Successfully decrypted API key");
            return decrypted;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to decrypt API key - key may be corrupted or encrypted with different protection key");
            throw new InvalidOperationException("Failed to decrypt API key", ex);
        }
    }
}