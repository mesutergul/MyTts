@echo off
setlocal enabledelayedexpansion

:: Configuration
set "API_URL=http://localhost:5209"
set "CREDENTIALS_FILE=%USERPROFILE%\.mytts_credentials"
set "LANGUAGE=%1"
if "%LANGUAGE%"=="" set "LANGUAGE=tr"

echo ===========================
echo  MyTTS Authentication Script
echo ===========================
echo Using language: %LANGUAGE%
echo API URL: %API_URL%
echo Credentials file: %CREDENTIALS_FILE%
echo.

:: Get credentials
if exist "%CREDENTIALS_FILE%" (
    for /f "tokens=1*" %%a in (%CREDENTIALS_FILE%) do (
        set "EMAIL=%%a"
        set "PASSWORD=%%b"
    )
    if not defined EMAIL (
        echo ERROR: Email not found in credentials file
        goto :clear_credentials
    )
    if not defined PASSWORD (
        echo ERROR: Password not found in credentials file
        goto :clear_credentials
    )
    echo Using saved credentials for: !EMAIL!
) else (
    goto :prompt_credentials
)

:get_token
echo.
echo Getting authentication token...
echo {"email":"%EMAIL%","password":"%PASSWORD%"} > "%TEMP%\login.json"

curl -s -X POST "%API_URL%/api/auth/login" ^
     -H "Content-Type: application/json" ^
     -d "@%TEMP%\login.json" ^
     -o "%TEMP%\token_response.json"

:: Parse token with jq
for /f "usebackq delims=" %%a in (`jq -r ".token" "%TEMP%\token_response.json"`) do set "TOKEN=%%a"

if "%TOKEN%"=="" (
    echo ERROR: Failed to get token. Check credentials or API.
    echo Showing response:
    type "%TEMP%\token_response.json"
    goto :error
)

echo Token received successfully.

:: API Call
echo.
echo Making authenticated API call...
curl -s -X GET "%API_URL%/api/mp3/feed/%LANGUAGE%" ^
     -H "Authorization: Bearer %TOKEN%" ^
     -H "Content-Type: application/json"

goto :end

:prompt_credentials
echo Please enter your credentials:
set /p "EMAIL=Enter email: "
set /p "PASSWORD=Enter password: "
(
    echo %EMAIL% %PASSWORD%
) > "%CREDENTIALS_FILE%"
echo Credentials saved to %CREDENTIALS_FILE%
goto :get_token

:clear_credentials
echo Clearing invalid credentials...
del "%CREDENTIALS_FILE%" >nul 2>&1
goto :prompt_credentials

:error
echo.
echo Script failed with errors.
goto :end

:end
del "%TEMP%\login.json" >nul 2>&1
del "%TEMP%\token_response.json" >nul 2>&1
endlocal
pause >nul
