# CONFIG
$ApiUrl = "http://localhost:5209"
$Lang = "tr"
$CredentialsPath = "$env:USERPROFILE\.mytts_credentials"

# --- GET CREDENTIALS ---
if (Test-Path $CredentialsPath) {
    $lines = Get-Content $CredentialsPath
    $Email = $lines[0]
    $Password = $lines[1]
    Write-Host "Using saved credentials: $Email"
} else {
    $Email = Read-Host "Enter Email"
    $Password = Read-Host "Enter Password" -AsSecureString | ConvertFrom-SecureString
    $DecodedPassword = $Password | ConvertTo-SecureString
    Set-Content -Path $CredentialsPath -Value @($Email, ($DecodedPassword | ConvertFrom-SecureString))
    Write-Host "Credentials saved to $CredentialsPath"
    $Password = $DecodedPassword
}

# --- CONVERT PASSWORD BACK TO PLAIN TEXT ---
if ($Password -is [SecureString]) {
    $Ptr = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($Password)
    $PasswordPlain = [System.Runtime.InteropServices.Marshal]::PtrToStringBSTR($Ptr)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($Ptr)
} else {
    $PasswordPlain = $Password
}

# --- REQUEST TOKEN ---
Write-Host "Requesting authentication token..."

# Manually create the JSON string instead of using ConvertTo-Json
$LoginBody = "{`"email`": `"$Email`", `"password`": `"$PasswordPlain`"}"

try {
    $response = Invoke-WebRequest -Uri "$ApiUrl/api/auth/login" `
        -Method Post `
        -Body $LoginBody `
        -Headers @{"Content-Type" = "application/json"} `
        -UseBasicParsing

    $json = $response.Content | ConvertFrom-Json
    $token = $json.token

    if ([string]::IsNullOrWhiteSpace($token)) {
        Write-Error "Token not found in response. Please check credentials."
        exit 1
    }

    Write-Host "✅ Token retrieved."
} catch {
    Write-Error "❌ Failed to retrieve token: $_"
    exit 1
}

# --- MAKE AUTHENTICATED API CALL ---
Write-Host "Calling secure API endpoint..."
try {
    $secureResp = Invoke-WebRequest -Uri "$ApiUrl/api/mp3/feed/$Lang" `
        -Headers @{Authorization = "Bearer $token"; "Content-Type" = "application/json"} `
        -UseBasicParsing

    Write-Host "`n--- Response ---"
    Write-Output $secureResp.Content
} catch {
    Write-Error "❌ API call failed: $_"
}
