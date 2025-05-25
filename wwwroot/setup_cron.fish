#!/usr/bin/env fish

# Default parameters
set DEFAULT_LANGUAGE "tr"
set DEFAULT_INTERVAL 30  # minutes
set SCRIPT_PATH (dirname (status -f))/auth_script.fish

# Make the script executable
chmod +x "$SCRIPT_PATH"

# Function to show usage
function show_usage
    echo "Usage: $argv[0] [options]"
    echo "Options:"
    echo "  -l, --language    Language code (default: $DEFAULT_LANGUAGE)"
    echo "  -i, --interval    Interval in minutes (default: $DEFAULT_INTERVAL)"
    echo "  -n, --name        Job name (default: api_job)"
    echo "  -h, --help        Show this help message"
end

# Parse command line arguments
set LANGUAGE $DEFAULT_LANGUAGE
set INTERVAL $DEFAULT_INTERVAL
set JOB_NAME "api_job"

# Parse arguments
for i in (seq (count $argv))
    switch $argv[$i]
        case -l --language
            set LANGUAGE $argv[(math $i + 1)]
        case -i --interval
            set INTERVAL $argv[(math $i + 1)]
        case -n --name
            set JOB_NAME $argv[(math $i + 1)]
        case -h --help
            show_usage
            exit 0
        case '*'
            echo "Unknown option: $argv[$i]"
            show_usage
            exit 1
    end
end

# Validate interval
if test $INTERVAL -lt 1
    echo "Error: Interval must be at least 1 minute"
    exit 1
end

# Create a temporary file for the new crontab
set TEMP_CRON (mktemp)

# Export current crontab
crontab -l > "$TEMP_CRON" 2>/dev/null; or echo "# New crontab" > "$TEMP_CRON"

# Remove any existing job with the same name
sed -i "/# $JOB_NAME/,/^$/d" "$TEMP_CRON"

# Add the new job
echo "# $JOB_NAME - Language: $LANGUAGE, Interval: $INTERVAL minutes" >> "$TEMP_CRON"
echo "*/$INTERVAL * * * * $SCRIPT_PATH $LANGUAGE >> /var/log/$JOB_NAME.log 2>&1" >> "$TEMP_CRON"
echo "" >> "$TEMP_CRON"

# Install the new crontab
crontab "$TEMP_CRON"

# Cleanup
rm "$TEMP_CRON"

echo "Cron job '$JOB_NAME' has been created successfully!"
echo "The script will run every $INTERVAL minute(s)"
echo "Using language: $LANGUAGE"
echo "Logs will be written to /var/log/$JOB_NAME.log"

# Example usage:
# ./setup_cron.fish -l en -i 15 -n english_news 