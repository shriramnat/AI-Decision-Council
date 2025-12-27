# Authentication & Authorization Setup Guide

This guide walks you through setting up authentication and authorization for the AI Decision Council application.

## Overview

The application now supports:
- **Three Authentication Providers:**
  - Microsoft Entra ID (Azure AD) - Work/School accounts
  - Microsoft Account - Personal Microsoft accounts
  - Google - Personal Google accounts
- **Approved User List:** File-based authorization with hot-reload support
- **Debug Logging:** Comprehensive authentication debug information

> **⚠️ IMPORTANT:** Authentication secrets should **NOT** be stored in `appsettings.json`. 
> 
> 📖 **See [USER_SECRETS_SETUP.md](USER_SECRETS_SETUP.md)** for detailed instructions on:
> - Setting up User Secrets for local development
> - Configuring Azure App Service for production
> - Managing and rotating secrets securely

## Table of Contents

1. [Quick Start](#quick-start)
2. [Provider Registration](#provider-registration)
3. [Configuration](#configuration)
4. [Approved Users Setup](#approved-users-setup)
5. [Testing](#testing)
6. [Troubleshooting](#troubleshooting)
7. [Security Best Practices](#security-best-practices)

---

## Quick Start

### 1. Install Required NuGet Packages

The following packages have been added to the project:

```bash
dotnet add package Microsoft.AspNetCore.Authentication.OpenIdConnect
dotnet add package Microsoft.AspNetCore.Authentication.Google
dotnet add package Microsoft.AspNetCore.Authentication.MicrosoftAccount
dotnet add package Microsoft.Identity.Web
```

### 2. Configure Approved Users

Edit `DXO/approved-users.json`:

```json
{
  "approvedUsers": [
    "user1@example.com",
    "user2@company.com"
  ]
}
```

Or enable wildcard access (all authenticated users):

```json
{
  "approvedUsers": [
    "*"
  ]
}
```

### 3. Register OAuth Applications

You need to register your application with each identity provider you want to use.

---

## Provider Registration

### Microsoft Entra ID (Azure AD)

**For organizational/work accounts:**

1. Go to [Azure Portal](https://portal.azure.com)
2. Navigate to **Azure Active Directory** > **App registrations**
3. Click **New registration**
4. Configure:
   - **Name:** AI Decision Council
   - **Supported account types:** Choose based on your needs
   - **Redirect URI:** `https://your-domain.com/signin-oidc`
5. After creation:
   - Copy the **Application (client) ID**
   - Copy the **Directory (tenant) ID**

6. Go to **Certificates & secrets** > Create a **New client secret**
7. Copy the secret value immediately
8. Configure in appsettings.json:
   ```json
   {
     "Authentication": {
       "EntraId": {
         "ClientId": "your-client-id",
         "ClientSecret": "your-client-secret",
         "TenantId": "your-tenant-id"
       }
     }
   }
   ```

### Microsoft Account

**For personal Microsoft accounts:**

1. Go to [Azure Portal](https://portal.azure.com) > **App registrations**
2. Click **New registration**
3. Configure:
   - **Name:** AI Decision Council
   - **Supported account types:** Personal Microsoft accounts only
   - **Redirect URI:** `https://your-domain.com/signin-microsoft`
4. After creation:
   - Copy the **Application (client) ID**
   - Go to **Certificates & secrets** > Create a **New client secret**
   - Copy the secret value

### Google

1. Go to [Google Cloud Console](https://console.cloud.google.com/)
2. Create a new project or select existing
3. Navigate to **APIs & Services** > **Credentials**
4. Click **Create Credentials** > **OAuth client ID**
5. Configure OAuth consent screen if prompted
6. Create credentials:
   - **Application type:** Web application
   - **Name:** AI Decision Council
   - **Authorized redirect URIs:** `https://your-domain.com/signin-google`
7. Copy the **Client ID** and **Client secret**

---

## Configuration

### Update appsettings.json

Edit `DXO/appsettings.json` and add your credentials:

```json
{
  "Authentication": {
    "EntraId": {
      "ClientId": "your-entra-client-id",
      "ClientSecret": "your-entra-client-secret",
      "TenantId": "your-tenant-id",
      "Instance": "https://login.microsoftonline.com/",
      "CallbackPath": "/signin-oidc"
    },
    "MicrosoftAccount": {
      "ClientId": "your-microsoft-account-client-id",
      "ClientSecret": "your-microsoft-account-client-secret",
      "CallbackPath": "/signin-microsoft"
    },
    "Google": {
      "ClientId": "your-google-client-id.apps.googleusercontent.com",
      "ClientSecret": "your-google-client-secret",
      "CallbackPath": "/signin-google"
    },
    "Cookie": {
      "ExpireTimeMinutes": 30
    }
  }
}
```

### Use User Secrets (Recommended for Development)

For local development, use .NET User Secrets instead of storing credentials in appsettings.json:

```bash
cd DXO

# Initialize user secrets
dotnet user-secrets init

# Set Entra ID credentials
dotnet user-secrets set "Authentication:EntraId:ClientId" "your-client-id"
dotnet user-secrets set "Authentication:EntraId:ClientSecret" "your-client-secret"
dotnet user-secrets set "Authentication:EntraId:TenantId" "your-tenant-id"

# Set Microsoft Account credentials
dotnet user-secrets set "Authentication:MicrosoftAccount:ClientId" "your-client-id"
dotnet user-secrets set "Authentication:MicrosoftAccount:ClientSecret" "your-client-secret"

# Set Google credentials
dotnet user-secrets set "Authentication:Google:ClientId" "your-client-id"
dotnet user-secrets set "Authentication:Google:ClientSecret" "your-client-secret"
```

### Environment Variables (Production)

For production deployments, use environment variables:

```bash
# Linux/macOS
export Authentication__EntraId__ClientId="your-client-id"
export Authentication__EntraId__ClientSecret="your-client-secret"
export Authentication__EntraId__TenantId="your-tenant-id"

# Windows
set Authentication__EntraId__ClientId=your-client-id
set Authentication__EntraId__ClientSecret=your-client-secret
set Authentication__EntraId__TenantId=your-tenant-id
```

---

## Approved Users Setup

### File Location

The approved users list is stored in: `DXO/approved-users.json`

### File Format

```json
{
  "approvedUsers": [
    "user1@example.com",
    "user2@company.com",
    "admin@yourorg.com"
  ]
}
```

### Hot Reload

The application automatically reloads the approved users list when the file changes. No restart required!

### Wildcard Access

To allow all authenticated users (useful for development):

```json
{
  "approvedUsers": [
    "*"
  ]
}
```

### Case Insensitivity

Email addresses are normalized (lowercased) automatically. These are equivalent:
- `User@Example.com`
- `user@example.com`
- `USER@EXAMPLE.COM`

### Important Notes

- Changes to `approved-users.json` take effect immediately
- The file is watched for changes (hot reload)
- If the file is missing or invalid, the application logs warnings
- Empty array = no users approved (unless wildcard is present)

---

## Testing

### 1. Start the Application

```bash
cd DXO
dotnet run
```

### 2. Access Landing Page

Navigate to: `https://localhost:5001/Landing` (or your configured URL)

### 3. Test Each Provider

Try signing in with each provider:
1. **Entra ID** - Use work/school account
2. **Microsoft Account** - Use personal Microsoft account
3. **Google** - Use Google account

### 4. Verify Authorization

After successful authentication, check:
- User email appears in the top-right user menu
- Application grants access if email is in approved list
- Application redirects to Landing with error if not approved

### 5. Monitor Debug Logs

Check the application logs for authentication debug information:

```
[AUTH-DEBUG] User authorization attempt
  Provider: Google
  Claim Type: email
  Email: user@example.com

[AUTH-DEBUG] Authorization result
  Provider: Google
  Claim Type: email
  Email (Raw): user@example.com
  Email (Normalized): user@example.com
  Wildcard Enabled: False
  In Approved List: True
  Result: PASS
```

---

## Troubleshooting

### Issue: "No email claim found"

**Symptom:** Authorization fails with "No email claim found" in logs

**Solution:**
- Check the provider is configured to include email in claims
- For Entra ID: Ensure "email" scope is requested
- For Google/Microsoft: Ensure consent screen includes email permission

### Issue: "Not in approved list"

**Symptom:** User authenticates successfully but gets "not authorized" error

**Solution:**
1. Check `approved-users.json` contains the user's email
2. Verify email formatting matches exactly (case-insensitive)
3. Check logs to see what email claim was used
4. Consider using wildcard `"*"` temporarily for testing

### Issue: "Redirect URI mismatch"

**Symptom:** Provider shows "redirect_uri_mismatch" error

**Solution:**
1. Verify redirect URI in provider portal matches exactly
2. Check for `http` vs `https`
3. Check for trailing slashes
4. Ensure port number is correct (if using localhost)

### Issue: Provider not configured

**Symptom:** Error on signin: "InvalidOperation: The authentication scheme 'X' is not configured"

**Solution:**
- Ensure ClientId and ClientSecret are set in configuration
- Check appsettings.json or user secrets are loaded
- Verify environment variables are set correctly

### Debug Logging

Enable detailed logging by setting log level in `appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information",
      "Microsoft.AspNetCore.Authentication": "Debug",
      "Microsoft.AspNetCore.Authorization": "Debug",
      "DXO.Services.Authorization": "Debug"
    }
  }
}
```

---

## Security Best Practices

### 1. Never Commit Secrets

- ❌ Don't commit `appsettings.json` with real credentials
- ✅ Use user secrets for development
- ✅ Use environment variables for production
- ✅ Use Azure Key Vault or similar for production secrets

### 2. Use HTTPS

- Always use HTTPS in production
- Configure proper SSL certificates
- Redirect HTTP to HTTPS

### 3. Secure Cookie Settings

The application is configured with secure cookie defaults:
- `HttpOnly`: Prevents JavaScript access to cookies
- `Secure`: Only sent over HTTPS (production)
- `SameSite`: Protection against CSRF attacks

### 4. Approved List Management

- Keep the approved users list minimal
- Remove users who no longer need access
- Audit the list regularly
- Consider integrating with your organization's user directory

### 5. Session Management

- Default session timeout: 30 minutes (configurable)
- Sessions use sliding expiration
- Users must re-authenticate after logout

### 6. Production Checklist

- [ ] All secrets in secure storage (not appsettings.json)
- [ ] HTTPS enabled and enforced
- [ ] Approved users list configured appropriately
- [ ] Wildcard (`*`) disabled unless intentional
- [ ] Redirect URIs configured correctly in all providers
- [ ] Security headers configured (already in Program.cs)
- [ ] Logging configured appropriately (not too verbose in production)

---

## Additional Resources

- [Microsoft Identity Platform Documentation](https://docs.microsoft.com/en-us/azure/active-directory/develop/)
- [Google OAuth 2.0 Documentation](https://developers.google.com/identity/protocols/oauth2)
- [ASP.NET Core Authentication](https://docs.microsoft.com/en-us/aspnet/core/security/authentication/)
- [ASP.NET Core Authorization](https://docs.microsoft.com/en-us/aspnet/core/security/authorization/)

---

## Support

If you encounter issues:

1. Check the [Troubleshooting](#troubleshooting) section
2. Review application logs for `[AUTH-DEBUG]` messages
3. Verify configuration matches this guide
4. Create an issue in the project repository with:
   - Provider being used
   - Error messages from logs
   - Steps to reproduce
   - Configuration (with secrets redacted)
