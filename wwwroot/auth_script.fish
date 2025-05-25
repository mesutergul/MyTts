#!/usr/bin/env fish

# Configuration
set API_URL "http://localhost:5209"
set CREDENTIALS_FILE "$HOME/.mytts_credentials"
set TEMP_DIR "/tmp"
set LANGUAGE $argv[1] # Get first argument
if test -z "$LANGUAGE"
    set LANGUAGE "tr" # Default to "tr" if no argument provided
end

echo "Using language: $LANGUAGE"

# Function to get credentials
function get_credentials
    if test -f "$CREDENTIALS_FILE"
        # Read credentials from file
        set -l credentials (cat "$CREDENTIALS_FILE")
        set -l lines (string split \n $credentials)
        set -g EMAIL $lines[1]
        set -g PASSWORD $lines[2]
    else
        # Prompt for credentials and save them
        read -P "Enter email: " EMAIL
        read -P "Enter password: " -s PASSWORD
        echo # New line after password
        echo $EMAIL > "$CREDENTIALS_FILE"
        echo $PASSWORD >> "$CREDENTIALS_FILE"
        chmod 600 "$CREDENTIALS_FILE" # Restrict file permissions
    end
end

# Function to get token
function get_token
    echo "Getting authentication token..."
    set -g TOKEN (curl -s -X POST "$API_URL/api/auth/login" \
        -H "Content-Type: application/json" \
        -d "{\"email\": \"$EMAIL\", \"password\": \"$PASSWORD\"}" | jq -r '.token')
end

# Get credentials and token
get_credentials
get_token

# Make the API call
echo "Making authenticated API call..."
curl -s -X GET "$API_URL/api/mp3/feed/$LANGUAGE" \
    -H "Authorization: Bearer $TOKEN" \
    -H "Content-Type: application/json"

# Cleanup
rm -f "$TEMP_DIR/token.txt" 