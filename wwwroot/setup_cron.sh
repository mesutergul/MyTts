#!/bin/bash

# Default parameters
DEFAULT_LANGUAGE="tr"
DEFAULT_INTERVAL=30  # minutes
SCRIPT_PATH="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)/auth_script.sh"

# Make the script executable
chmod +x "$SCRIPT_PATH"

# Function to show usage
show_usage() {
    echo "Usage: $0 [options]"
    echo "Options:"
    echo "  -l, --language    Language code (default: $DEFAULT_LANGUAGE)"
    echo "  -i, --interval    Interval in minutes (default: $DEFAULT_INTERVAL)"
    echo "  -n, --name        Job name (default: api_job)"
    echo "  -h, --help        Show this help message"
}

# Parse command line arguments
LANGUAGE=$DEFAULT_LANGUAGE
INTERVAL=$DEFAULT_INTERVAL
JOB_NAME="api_job"

while [[ $# -gt 0 ]]; do
    case $1 in
        -l|--language)
            LANGUAGE="$2"
            shift 2
            ;;
        -i|--interval)
            INTERVAL="$2"
            shift 2
            ;;
        -n|--name)
            JOB_NAME="$2"
            shift 2
            ;;
        -h|--help)
            show_usage
            exit 0
            ;;
        *)
            echo "Unknown option: $1"
            show_usage
            exit 1
            ;;
    esac
done

# Validate interval
if [ "$INTERVAL" -lt 1 ]; then
    echo "Error: Interval must be at least 1 minute"
    exit 1
fi

# Create a temporary file for the new crontab
TEMP_CRON=$(mktemp)

# Export current crontab
crontab -l > "$TEMP_CRON" 2>/dev/null || echo "# New crontab" > "$TEMP_CRON"

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
# ./setup_cron.sh -l en -i 15 -n english_news 