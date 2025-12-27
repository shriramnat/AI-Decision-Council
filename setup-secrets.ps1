# PowerShell Script to Set User Secrets for AI Decision Council
# Run this script from the repository root directory

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "AI Decision Council - User Secrets Setup" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Check if we're in the correct directory
if (-not (Test-Path "DXO/DXO.csproj")) {
    Write-Host "ERROR: Please run this script from the repository root directory." -ForegroundColor Red
    Write-Host "Current directory: $(Get-Location)" -ForegroundColor Red
    exit 1
}

Write-Host "This script will help you configure authentication secrets for local development." -ForegroundColor Yellow
Write-Host "Secrets will be stored in your user profile, NOT in the project directory." -ForegroundColor Yellow
Write-Host ""

# Function to read secret input
function Read-Secret {
    param (
        [string]$Prompt
    )
    
    Write-Host $Prompt -NoNewline
    $secret = Read-Host -AsSecureString
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secret)
    $plainText = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
    
    return $plainText
}

# Ask user which secrets to configure
Write-Host "Which authentication providers do you want to configure?" -ForegroundColor Cyan
Write-Host "1. All providers (Entra ID + Microsoft Account + Google)" -ForegroundColor White
Write-Host "2. Entra ID only" -ForegroundColor White
Write-Host "3. Microsoft Account only" -ForegroundColor White
Write-Host "4. Google only" -ForegroundColor White
Write-Host "5. Custom selection" -ForegroundColor White
Write-Host ""

$choice = Read-Host "Enter your choice (1-5)"

$configureEntraId = $false
$configureMicrosoftAccount = $false
$configureGoogle = $false

switch ($choice) {
    "1" {
        $configureEntraId = $true
        $configureMicrosoftAccount = $true
        $configureGoogle = $true
    }
    "2" { $configureEntraId = $true }
    "3" { $configureMicrosoftAccount = $true }
    "4" { $configureGoogle = $true }
    "5" {
        $configureEntraId = (Read-Host "Configure Entra ID? (y/n)") -eq "y"
        $configureMicrosoftAccount = (Read-Host "Configure Microsoft Account? (y/n)") -eq "y"
        $configureGoogle = (Read-Host "Configure Google? (y/n)") -eq "y"
    }
    default {
        Write-Host "Invalid choice. Exiting." -ForegroundColor Red
        exit 1
    }
}

Write-Host ""
Write-Host "Configuring secrets..." -ForegroundColor Green
Write-Host ""

# Configure Entra ID
if ($configureEntraId) {
    Write-Host "=== Entra ID (Azure AD) Configuration ===" -ForegroundColor Cyan
    $entraSecret = Read-Secret "Enter Entra ID Client Secret: "
    
    if ($entraSecret) {
        dotnet user-secrets set "Authentication:EntraId:ClientSecret" $entraSecret --project DXO/DXO.csproj
        Write-Host "✓ Entra ID secret configured" -ForegroundColor Green
    }
    Write-Host ""
}

# Configure Microsoft Account
if ($configureMicrosoftAccount) {
    Write-Host "=== Microsoft Account Configuration ===" -ForegroundColor Cyan
    $msaSecret = Read-Secret "Enter Microsoft Account Client Secret: "
    
    if ($msaSecret) {
        dotnet user-secrets set "Authentication:MicrosoftAccount:ClientSecret" $msaSecret --project DXO/DXO.csproj
        Write-Host "✓ Microsoft Account secret configured" -ForegroundColor Green
    }
    Write-Host ""
}

# Configure Google
if ($configureGoogle) {
    Write-Host "=== Google Configuration ===" -ForegroundColor Cyan
    $googleSecret = Read-Secret "Enter Google Client Secret: "
    
    if ($googleSecret) {
        dotnet user-secrets set "Authentication:Google:ClientSecret" $googleSecret --project DXO/DXO.csproj
        Write-Host "✓ Google secret configured" -ForegroundColor Green
    }
    Write-Host ""
}

# Display configured secrets (masked)
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Configuration Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "To verify your configured secrets, run:" -ForegroundColor Yellow
Write-Host "  dotnet user-secrets list --project DXO/DXO.csproj" -ForegroundColor White
Write-Host ""
Write-Host "To start the application:" -ForegroundColor Yellow
Write-Host "  cd DXO" -ForegroundColor White
Write-Host "  dotnet run" -ForegroundColor White
Write-Host ""
Write-Host "For more information, see docs/USER_SECRETS_SETUP.md" -ForegroundColor Cyan
Write-Host ""
