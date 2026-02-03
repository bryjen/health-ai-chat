#!/bin/sh
# Don't use set -e here - we want to continue even if config replacement fails
# set -e

###############################################################################
# Frontend appsettings.json Runtime Replacement
###############################################################################
# This script replaces values in appsettings.json at container startup using
# environment variables passed from Terraform/Cloud Run.
#
# HOW IT WORKS:
# 1. The appsettings.json file is baked into the Docker image during build
#    with default/placeholder values.
#
# 2. At container startup (before nginx starts), this script:
#    a. Reads environment variables (e.g., VERSION_MAJOR, OAUTH_GOOGLE_CLIENT_ID)
#    b. Uses sed to find and replace matching JSON values in appsettings.json
#    c. Handles different JSON value types:
#       - Numbers: Replaces numeric values (e.g., "Major": 0 -> "Major": 1)
#       - Strings: Replaces quoted string values
#       - Nullable: Replaces null or string values (for optional fields)
#
# 3. The replacement uses a declarative mapping table (CONFIG_MAPPINGS) that
#    defines:
#    - Environment variable name (e.g., VERSION_MAJOR)
#    - JSON key to replace (e.g., Major)
#    - Parent object if nested (e.g., Version)
#    - Value type (string, number, nullable)
#
# 4. For nested JSON objects, sed uses range patterns:
#    /"Version":\s*{/,/}/  matches everything between "Version": { and }
#    This ensures we only replace values within the correct parent object.
#
# 5. Special characters in values are escaped for sed safety.
#
# EXAMPLE:
#   Environment: VERSION_MAJOR=2
#   JSON before: "Version": { "Major": 0, ... }
#   JSON after:  "Version": { "Major": 2, ... }
#
# WHY RUNTIME REPLACEMENT:
# - Allows same Docker image to be deployed with different configs
# - No need to rebuild images for environment-specific values
# - Values come from Terraform variables, ensuring consistency
# - Frontend is static files served by nginx, so runtime replacement is needed
###############################################################################

CONFIG_FILE="/usr/share/nginx/html/appsettings.json"

# Check if config file exists
if [ ! -f "$CONFIG_FILE" ]; then
    echo "WARNING: $CONFIG_FILE not found, skipping config replacement"
else
    echo "Found config file: $CONFIG_FILE"
fi

# Escape special characters for sed
escape_sed() {
    echo "$1" | sed 's/[[\.*^$()+?{|]/\\&/g'
}

# Replace JSON string value
replace_json_string() {
    local json_key=$1
    local env_value=$2
    local parent_object=$3
    
    if [ -z "$env_value" ] || [ ! -f "$CONFIG_FILE" ]; then
        return 0
    fi
    
    local escaped_value=$(escape_sed "$env_value")
    
    if [ -n "$parent_object" ]; then
        # Replace within a specific parent object
        sed -i "/\"$parent_object\":\\s*{/,/}/ s|\"$json_key\"\\s*:\\s*\"[^\"]*\"|\"$json_key\": \"$escaped_value\"|g" "$CONFIG_FILE" || true
    else
        # Replace at root level
        sed -i "s|\"$json_key\"\\s*:\\s*\"[^\"]*\"|\"$json_key\": \"$escaped_value\"|g" "$CONFIG_FILE" || true
    fi
    
    echo "Replaced $json_key with value from environment"
}

# Replace JSON number value
replace_json_number() {
    local json_key=$1
    local env_value=$2
    local parent_object=$3
    
    if [ -z "$env_value" ] || [ ! -f "$CONFIG_FILE" ]; then
        return 0
    fi
    
    if [ -n "$parent_object" ]; then
        # Replace within a specific parent object
        sed -i "/\"$parent_object\":\\s*{/,/}/ s|\"$json_key\"\\s*:\\s*[0-9]*|\"$json_key\": $env_value|g" "$CONFIG_FILE" || true
    else
        # Replace at root level
        sed -i "s|\"$json_key\"\\s*:\\s*[0-9]*|\"$json_key\": $env_value|g" "$CONFIG_FILE" || true
    fi
    
    echo "Replaced $json_key with value from environment"
}

# Replace JSON null value (for optional fields)
replace_json_nullable() {
    local json_key=$1
    local env_value=$2
    local parent_object=$3
    
    if [ -z "$env_value" ] || [ ! -f "$CONFIG_FILE" ]; then
        return 0
    fi
    
    local escaped_value=$(escape_sed "$env_value")
    
    if [ -n "$parent_object" ]; then
        # Replace null or string value within parent object
        sed -i "/\"$parent_object\":\\s*{/,/}/ s|\"$json_key\"\\s*:\\s*[^,}]*|\"$json_key\": \"$escaped_value\"|g" "$CONFIG_FILE" || true
    else
        # Replace null or string value at root level
        sed -i "s|\"$json_key\"\\s*:\\s*[^,}]*|\"$json_key\": \"$escaped_value\"|g" "$CONFIG_FILE" || true
    fi
    
    echo "Replaced $json_key with value from environment"
}

# Declarative configuration mapping: ENV_VAR_NAME|JSON_KEY|PARENT_OBJECT|TYPE
# TYPE: string, number, nullable
CONFIG_MAPPINGS="
API_BASE_URL|BaseUrl||string
OAUTH_GOOGLE_CLIENT_ID|ClientId|Google|string
OAUTH_MICROSOFT_CLIENT_ID|ClientId|Microsoft|string
OAUTH_MICROSOFT_TENANT_ID|TenantId|Microsoft|string
OAUTH_GITHUB_CLIENT_ID|ClientId|GitHub|string
VERSION_MAJOR|Major|Version|number
VERSION_MINOR|Minor|Version|number
VERSION_PATCH|Patch|Version|number
VERSION_PRERELEASE|PreRelease|Version|nullable
VERSION_BUILD_METADATA|BuildMetadata|Version|nullable
"

# Process each configuration mapping using here-string to avoid subshell
while IFS='|' read -r env_var json_key parent_object value_type; do
    # Skip empty lines
    [ -z "$env_var" ] && continue
    
    # Get environment variable value
    eval "env_value=\$$env_var"
    
    # Skip if environment variable is not set
    [ -z "$env_value" ] && continue
    
    # Apply replacement based on type
    case "$value_type" in
        string)
            replace_json_string "$json_key" "$env_value" "$parent_object"
            ;;
        number)
            replace_json_number "$json_key" "$env_value" "$parent_object"
            ;;
        nullable)
            replace_json_nullable "$json_key" "$env_value" "$parent_object"
            ;;
    esac
done <<EOF
$CONFIG_MAPPINGS
EOF

# Verify nginx config exists and is valid before starting
echo "Checking nginx configuration..."
if [ ! -f "/etc/nginx/conf.d/default.conf" ]; then
    echo "ERROR: /etc/nginx/conf.d/default.conf not found!"
    exit 1
fi

# Show nginx config for debugging
echo "Nginx config file exists. First 10 lines:"
head -n 10 /etc/nginx/conf.d/default.conf || true

# Verify nginx config is valid
echo "Validating nginx configuration..."
if ! nginx -t; then
    echo "ERROR: nginx configuration is invalid!"
    echo "Showing nginx config:"
    cat /etc/nginx/conf.d/default.conf || true
    echo "Showing nginx error log:"
    cat /var/log/nginx/error.log 2>/dev/null || true
    exit 1
fi

# List files in html directory for debugging
echo "Contents of /usr/share/nginx/html:"
ls -la /usr/share/nginx/html/ || true

# Check if index.html exists (critical file)
if [ ! -f "/usr/share/nginx/html/index.html" ]; then
    echo "ERROR: index.html not found in /usr/share/nginx/html/"
    echo "This is a critical error - the app cannot start without index.html"
    exit 1
fi

# Start nginx
echo "Starting nginx..."
exec nginx -g 'daemon off;'
