@echo off
setlocal enabledelayedexpansion

set "USERPROFILE=C:\Users\Yenimedya"
echo USERPROFILE: !USERPROFILE!

set "CREDENTIALS_FILE=!USERPROFILE!\.mytts_credentials"
echo Looking for: !CREDENTIALS_FILE!

if exist "!CREDENTIALS_FILE!" (
    echo Found credentials:
    type "!CREDENTIALS_FILE!"
) else (
    echo Not found!
)
