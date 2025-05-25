# Get the current directory
$scriptPath = Split-Path -Parent $MyInvocation.MyCommand.Path
$batchFilePath = Join-Path $scriptPath "auth_script.bat"

# Default parameters
$defaultLanguage = "tr"
$defaultIntervalMinutes = 30  # Default to 30 minutes
$taskName = "APIAuthJob"

# Parse command line arguments
param(
    [string]$Language = $defaultLanguage,
    [int]$IntervalMinutes = $defaultIntervalMinutes,
    [string]$TaskName = $taskName
)

# Validate interval
if ($IntervalMinutes -lt 1) {
    Write-Host "Error: Interval must be at least 1 minute"
    exit 1
}

# Create the scheduled task action with language parameter
$action = New-ScheduledTaskAction -Execute $batchFilePath -Argument $Language

# Create the trigger (runs at specified interval)
$trigger = New-ScheduledTaskTrigger -Once -At (Get-Date) -RepetitionInterval (New-TimeSpan -Minutes $IntervalMinutes)

# Create the task settings
$settings = New-ScheduledTaskSettingsSet -StartWhenAvailable -DontStopOnIdleEnd

# Create the task description
$taskDescription = "Runs API authentication and calls every $IntervalMinutes minute(s) for language: $Language"

# Check if task already exists and remove it
Unregister-ScheduledTask -TaskName $TaskName -Confirm:$false -ErrorAction SilentlyContinue

# Register the new task
Register-ScheduledTask -TaskName $TaskName -Description $taskDescription -Action $action -Trigger $trigger -Settings $settings -Force

Write-Host "Task '$TaskName' has been created successfully!"
Write-Host "The script will run every $IntervalMinutes minute(s) starting from now."
Write-Host "Using language: $Language"

# Example usage:
# Set-ExecutionPolicy Bypass -Scope Process -Force
# .\setup_task.ps1 -Language "en" -IntervalMinutes 15