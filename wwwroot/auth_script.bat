@echo off
setlocal enabledelayedexpansion

:: Configuration
set "API_URL=http://localhost:5209"
set "CREDENTIALS_FILE=%USERPROFILE%\.mytts_credentials"
set "LANGUAGE=%1"
if "%LANGUAGE%"=="" set "LANGUAGE=tr"

echo Using language: %LANGUAGE%

:: Function to get credentials
:get_credentials
if exist "%CREDENTIALS_FILE%" (
    :: Read credentials from file
    set /p EMAIL=<"%CREDENTIALS_FILE%"
    set /p PASSWORD=<"%CREDENTIALS_FILE%"
) else (
    :: Prompt for credentials and save them
    set /p "EMAIL=Enter email: "
    set /p "PASSWORD=Enter password: "
    echo %EMAIL%> "%CREDENTIALS_FILE%"
    echo %PASSWORD%>> "%CREDENTIALS_FILE%"
    :: Set file permissions (Windows equivalent of chmod 600)
    icacls "%CREDENTIALS_FILE%" /inheritance:r /grant:r "%USERNAME%:(R,W)"
)
goto :eof

:: Function to get token
:get_token
echo Getting authentication token...
for /f "tokens=*" %%a in ('curl -s -X POST "%API_URL%/api/auth/login" -H "Content-Type: application/json" -d "{\"email\": \"%EMAIL%\", \"password\": \"%PASSWORD%\"}" ^| powershell -Command "$input | ConvertFrom-Json | Select-Object -ExpandProperty token"') do set "TOKEN=%%a"
goto :eof

:: Main script
call :get_credentials
call :get_token

:: Make the API call
echo Making authenticated API call...
curl -s -X GET "%API_URL%/api/mp3/feed/%LANGUAGE%" -H "Authorization: Bearer %TOKEN%" -H "Content-Type: application/json"

endlocal 