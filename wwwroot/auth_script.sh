#!/bin/bash

# Configuration
API_URL="http://localhost:5000"
EMAIL="admin@example.com"
PASSWORD="Admin123!"
TEMP_DIR="/tmp"
LANGUAGE=${1:-"tr"}  # Default to "tr" if no argument provided

echo "Using language: $LANGUAGE"

# Function to get token
get_token() {
    echo "Getting authentication token..."
    response=$(curl -s -X POST "$API_URL/api/auth/login" \
        -H "Content-Type: application/json" \
        -d "{\"email\": \"$EMAIL\", \"password\": \"$PASSWORD\"}")
    
    # Extract token using jq (make sure jq is installed)
    TOKEN=$(echo $response | jq -r '.token')
    
    if [ -z "$TOKEN" ] || [ "$TOKEN" = "null" ]; then
        echo "Failed to get token"
        exit 1
    fi
    
    echo "Token obtained successfully"
}

# Get the token
get_token

# Make the API call
echo "Making authenticated API call..."
curl -s -X GET "$API_URL/api/mp3/feed/$LANGUAGE" \
    -H "Authorization: Bearer $TOKEN" > "$TEMP_DIR/user_info.json"

# Display the response
echo "User information:"
cat "$TEMP_DIR/user_info.json"

# Cleanup
rm -f "$TEMP_DIR/user_info.json" 