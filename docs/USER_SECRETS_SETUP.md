# User Secrets Setup Guide

This guide explains how to configure authentication secrets for local development using .NET User Secrets and for production deployment using Azure App Service.

## Overview

The application requires authentication secrets for three providers:
- **Entra ID** (Azure AD)
- **Microsoft Account** (Personal)
- **Google**

These secrets are **NOT** stored in `appsettings.json` for security reasons. Instead, they are configured separately for each environment.

---

## Local Development Setup (User Secrets)

User Secrets is a secure way to store secrets during development. Secrets are stored outside your project directory and are never committed to source control.

### 1. Initialize User Secrets (Already Done)

The project has been initialized with UserSecretsId: `903f0ebb-fdbf-4f76-8159-48375c410beb`

### 2. Set Your Secrets

Run these commands from the repository root to set your authentication secrets:

```bash
# Entra ID (Azure AD) Secret
dotnet user-secrets set "Authentication:EntraId:ClientSecret" "YOUR_ENTRA_ID_SECRET_HERE" --project DXO/DXO.csproj

# Microsoft Account Secret
dotnet user-secrets set "Authentication:MicrosoftAccount:ClientSecret" "YOUR_MICROSOFT_ACCOUNT_SECRET_HERE" --project DXO/DXO.csproj

# Google Secret
dotnet user-secrets set "Authentication:Google:ClientSecret" "YOUR_GOOGLE_SECRET_HERE" --project DXO/DXO.csproj
```

### 3. Verify Secrets Are Set

List all configured secrets:
```bash
dotnet user-secrets list --project DXO/DXO.csproj
```

### 4. Managing Secrets

**View a specific secret:**
```bash
dotnet user-secrets list --project DXO/DXO.csproj | findstr EntraId
```

**Remove a secret:**
```bash
dotnet user-secrets remove "Authentication:EntraId:ClientSecret" --project DXO/DXO.csproj
```

**Clear all secrets:**
```bash
dotnet user-secrets clear --project DXO/DXO.csproj
```

### Where Are Secrets Stored?

User Secrets are stored in your user profile directory:
- **Windows**: `%APPDATA%\Microsoft\UserSecrets\903f0ebb-fdbf-4f76-8159-48375c410beb\secrets.json`
- **macOS/Linux**: `~/.microsoft/usersecrets/903f0ebb-fdbf-4f76-8159-48375c410beb/secrets.json`

---

## Production Setup (Azure App Service)

For Azure App Service, configure secrets in the Application Settings section.

### 1. Access Azure Portal

1. Navigate to your App Service in Azure Portal
2. Go to **Settings** → **Configuration**
3. Click on **Application settings** tab

### 2. Add Secret Configuration

Add the following Application Settings (use `__` double underscores as hierarchy separator):

| Name | Value |
|------|-------|
| `Authentication__EntraId__ClientSecret` | Your Entra ID client secret |
| `Authentication__MicrosoftAccount__ClientSecret` | Your Microsoft Account client secret |
| `Authentication__Google__ClientSecret` | Your Google client secret |

### 3. Save and Restart

1. Click **Save** at the top
2. Click **Continue** to confirm
3. The App Service will automatically restart

### 4. Verify Configuration

Check the application logs to ensure authentication is working correctly:
```bash
az webapp log tail --name YOUR_APP_NAME --resource-group YOUR_RESOURCE_GROUP
```

---

## Configuration Priority

ASP.NET Core reads configuration in this order (later sources override earlier ones):

1. `appsettings.json`
2. `appsettings.{Environment}.json`
3. **User Secrets** (Development only)
4. **Environment Variables** (includes Azure App Service settings)
5. Command-line arguments

This means:
- In **local development**: User Secrets override appsettings.json
- In **production**: Azure App Service settings override everything

---

## Security Best Practices

### ✅ DO:
- Use User Secrets for local development
- Use Azure App Service Application Settings for production
- Rotate secrets regularly
- Use different secrets for each environment
- Keep secrets.json (local file) in your `.gitignore`

### ❌ DON'T:
- Commit secrets to source control
- Share your User Secrets with others
- Use production secrets in development
- Store secrets in appsettings.json
- Email or message secrets

---

## Troubleshooting

### "Missing authentication credentials" error

**Symptom:** Application fails to start or authentication doesn't work

**Solution:** 
1. Verify secrets are set: `dotnet user-secrets list --project DXO/DXO.csproj`
2. Check for typos in secret names (they are case-sensitive)
3. Ensure you're in the correct environment (Development vs Production)

### Secrets not loading in development

**Symptom:** Application uses empty strings from appsettings.json

**Solution:**
1. Verify `UserSecretsId` is in DXO.csproj: `<UserSecretsId>903f0ebb-fdbf-4f76-8159-48375c410beb</UserSecretsId>`
2. Confirm environment is set to "Development"
3. Restart your IDE/terminal

### Azure App Service configuration not working

**Symptom:** Authentication fails in Azure but works locally

**Solution:**
1. Verify setting names use `__` (double underscore), not `:`
2. Check Application Settings in Azure Portal
3. Ensure settings were saved and app restarted
4. Review application logs for specific errors

---

## Quick Reference

### Local Development Commands
```bash
# Set secrets
dotnet user-secrets set "Authentication:EntraId:ClientSecret" "value" --project DXO/DXO.csproj
dotnet user-secrets set "Authentication:MicrosoftAccount:ClientSecret" "value" --project DXO/DXO.csproj
dotnet user-secrets set "Authentication:Google:ClientSecret" "value" --project DXO/DXO.csproj

# List secrets
dotnet user-secrets list --project DXO/DXO.csproj

# Clear all secrets
dotnet user-secrets clear --project DXO/DXO.csproj
```

### Azure App Service Settings
```
Authentication__EntraId__ClientSecret
Authentication__MicrosoftAccount__ClientSecret
Authentication__Google__ClientSecret
```

---

## Additional Resources

- [Safe storage of app secrets in development](https://docs.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [Configuration in ASP.NET Core](https://docs.microsoft.com/en-us/aspnet/core/fundamentals/configuration/)
- [Azure App Service Configuration](https://docs.microsoft.com/en-us/azure/app-service/configure-common)
