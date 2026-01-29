# Entra ID Authentication Without Client Secret

This guide explains how to configure Microsoft Entra ID (Azure AD) authentication for DXO **without storing a client secret** in your application.

## Overview

DXO supports two authentication modes for Entra ID:

| Mode | Client Secret | Flow Type | Recommended For |
|------|---------------|-----------|-----------------|
| **Confidential Client** | ✅ Required | Authorization Code + PKCE | Server apps with secure secret storage |
| **Public Client** | ❌ Not needed | Implicit (ID Token) | Apps that don't want to manage secrets |

This guide focuses on the **Public Client** approach using implicit flow, which is ideal when:
- You don't want to manage client secrets
- You're deploying to environments where secret rotation is difficult
- You want to simplify deployment and configuration
- You only need user identity (ID token), not API access

## How Implicit Flow Works

1. **User clicks "Sign in with Entra ID"** → App redirects to Microsoft login
2. **User authenticates** with their Entra ID credentials
3. **Microsoft validates credentials** and generates an ID token
4. **Microsoft redirects back** with the ID token directly (via form_post)
5. **App validates the ID token** signature and claims
6. **User is authenticated** based on the ID token claims

The implicit flow is secure for ID tokens because:
- ID tokens are signed by Microsoft and validated by the app
- `form_post` response mode prevents token exposure in browser history
- Nonce validation prevents replay attacks
- No access tokens are used (just identity verification)

## Azure Portal Configuration

### Step 1: Create or Update App Registration

1. Go to [Azure Portal](https://portal.azure.com) → **Microsoft Entra ID** → **App registrations**

2. Either:
   - Click **New registration** to create a new app, OR
   - Select your existing app registration

3. Configure basic settings:
   - **Name**: `DXO` (or your preferred name)
   - **Supported account types**: Choose based on your needs:
     - `Accounts in this organizational directory only` - Single tenant
     - `Accounts in any organizational directory` - Multi-tenant
     - `Accounts in any organizational directory and personal Microsoft accounts` - Multi-tenant + personal
   - Click **Register**

### Step 2: Configure Platform Settings

1. In your app registration, go to **Authentication**

2. Click **Add a platform** and select **Web**

3. Configure the Web platform:
   - **Redirect URIs**: Add your callback URL(s):
     ```
     https://your-domain.com/signin-oidc
     https://localhost:5001/signin-oidc  (for local development)
     ```
   - **Front-channel logout URL**: `https://your-domain.com/signout-callback-oidc` (optional)
   - **ID tokens**: ✅ Check this box
   - **Access tokens**: ❌ Leave unchecked (not needed for PKCE-only flow)

4. Under **Advanced settings**, enable public client flows:
   - **Allow public client flows**: Set to **Yes**
   
   > ⚠️ **Important**: This setting is required for PKCE without a client secret

5. Click **Save**

### Step 3: Configure API Permissions (Minimal)

1. Go to **API permissions**

2. You should have these permissions by default:
   - `Microsoft Graph` → `User.Read` (Delegated)

3. If missing, click **Add a permission**:
   - Select **Microsoft Graph** → **Delegated permissions**
   - Add: `openid`, `profile`, `email`
   - Click **Add permissions**

4. If your tenant requires admin consent:
   - Click **Grant admin consent for [tenant name]**

### Step 4: Note Your Configuration Values

Go to **Overview** and note these values:

| Setting | Where to Find | Example |
|---------|---------------|---------|
| **Application (client) ID** | Overview page | `12345678-1234-1234-1234-123456789012` |
| **Directory (tenant) ID** | Overview page | `87654321-4321-4321-4321-210987654321` |

You do **NOT** need:
- ❌ Client secret
- ❌ Certificate

## Application Configuration

### appsettings.json

Configure your `appsettings.json` (or `appsettings.Production.json`):

```json
{
  "Authentication": {
    "Enabled": true,
    "EntraId": {
      "Enabled": true,
      "Instance": "https://login.microsoftonline.com/",
      "TenantId": "YOUR_TENANT_ID",
      "ClientId": "YOUR_CLIENT_ID",
      "ClientSecret": null,
      "CallbackPath": "/signin-oidc",
      "UsePkce": true,
      "Scopes": ["openid", "profile", "email"]
    }
  }
}
```

### Environment Variables (Alternative)

You can also use environment variables or Azure App Service configuration:

```bash
Authentication__EntraId__Enabled=true
Authentication__EntraId__TenantId=YOUR_TENANT_ID
Authentication__EntraId__ClientId=YOUR_CLIENT_ID
Authentication__EntraId__UsePkce=true
```

### Configuration Options

| Option | Description | Default |
|--------|-------------|---------|
| `Enabled` | Enable/disable Entra ID authentication | `true` |
| `Instance` | Microsoft identity platform URL | `https://login.microsoftonline.com/` |
| `TenantId` | Your Azure AD tenant ID (or `common` for multi-tenant) | Required |
| `ClientId` | Application (client) ID from Azure | Required |
| `ClientSecret` | Client secret (null for PKCE) | `null` |
| `CallbackPath` | OAuth callback path | `/signin-oidc` |
| `UsePkce` | Enable PKCE flow | `true` |
| `Scopes` | OpenID Connect scopes | `["openid", "profile", "email"]` |

## Multi-Tenant Configuration

For multi-tenant applications (users from any Azure AD tenant):

1. **Azure Portal**: Set **Supported account types** to include other directories

2. **appsettings.json**: Use `common` or `organizations` as TenantId:
   ```json
   {
     "Authentication": {
       "EntraId": {
         "TenantId": "common"
       }
     }
   }
   ```

| TenantId Value | Allows |
|----------------|--------|
| `{tenant-guid}` | Only users from that specific tenant |
| `common` | Users from any Azure AD tenant + personal Microsoft accounts |
| `organizations` | Users from any Azure AD tenant (no personal accounts) |
| `consumers` | Only personal Microsoft accounts |

## Troubleshooting

### Error: "AADSTS7000218: The request body must contain... client_secret"

**Cause**: Azure app registration requires a client secret but your app isn't providing one.

**Solution**: 
1. Go to Azure Portal → App registration → Authentication
2. Enable "Allow public client flows" = **Yes**
3. Save and wait a few minutes for changes to propagate

### Error: "AADSTS50011: The redirect URI... does not match"

**Cause**: The callback URL doesn't match what's registered in Azure.

**Solution**:
1. Check the exact URL in the error message
2. Add that exact URL to your app registration's redirect URIs
3. Ensure protocol (http vs https) and port match exactly

### Error: "AADSTS65001: The user or administrator has not consented"

**Cause**: API permissions haven't been granted.

**Solution**:
1. Go to API permissions in your app registration
2. Click "Grant admin consent for [tenant]"
3. Or have users consent individually on first login

### ID Token Claims Missing

**Cause**: Required claims not included in ID token.

**Solution**:
1. Verify `Scopes` in configuration include `openid`, `profile`, `email`
2. Check Azure Portal → Token configuration for custom claims
3. Ensure `GetClaimsFromUserInfoEndpoint = true` (default in DXO)

### PKCE Not Working

**Cause**: PKCE may not be enabled or supported.

**Verification**:
1. Check startup logs for: `Configuring Entra ID authentication: UsePKCE=True`
2. Ensure `UsePkce: true` in configuration
3. Ensure `ClientSecret` is `null` or empty

## Security Considerations

### Why PKCE is Secure Without a Secret

1. **Code verifier is random**: 43-128 character cryptographically random string
2. **Code challenge is derived**: SHA256 hash of the verifier
3. **Verifier never transmitted during auth**: Only the challenge is sent to Microsoft
4. **One-time use**: Each authentication generates new verifier/challenge pair
5. **Cannot be reversed**: Attacker cannot derive verifier from challenge

### Recommendations

- ✅ Always use HTTPS in production
- ✅ Keep redirect URIs specific (avoid wildcards)
- ✅ Use `form_post` response mode (default in DXO)
- ✅ Implement proper session management
- ✅ Use the approved user list for authorization
- ❌ Don't log or store the code verifier

## Comparison: PKCE vs Client Secret

| Aspect | PKCE (Public Client) | Client Secret (Confidential) |
|--------|---------------------|------------------------------|
| Secret management | None required | Must secure and rotate secret |
| Azure setup | Enable public client flows | Create and manage secrets |
| Security | Cryptographically secure | Relies on secret protection |
| Token refresh | Not available | Available with refresh tokens |
| API access | ID token only | Access tokens for APIs |
| Best for | User authentication | Server-to-server + user auth |

## Additional Resources

- [Microsoft Identity Platform - Authorization Code Flow with PKCE](https://learn.microsoft.com/en-us/entra/identity-platform/v2-oauth2-auth-code-flow)
- [Public Client Applications](https://learn.microsoft.com/en-us/entra/identity-platform/msal-client-applications)
- [OpenID Connect on Microsoft Identity Platform](https://learn.microsoft.com/en-us/entra/identity-platform/v2-protocols-oidc)