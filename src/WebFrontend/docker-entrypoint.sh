#!/bin/sh
set -e

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

# Escape special characters for sed
escape_sed() {
    echo "$1" | sed 's/[[\.*^$()+?{|]/\\&/g'
}

# Replace JSON string value
replace_json_string() {
    local json_key=$1
    local env_value=$2
    local parent_object=$3
    
    if [ -z "$env_value" ]; then
        return 0
    fi
    
    local escaped_value=$(escape_sed "$env_value")
    
    if [ -n "$parent_object" ]; then
        # Replace within a specific parent object
        sed -i "/\"$parent_object\":\\s*{/,/}/ s|\"$json_key\"\\s*:\\s*\"[^\"]*\"|\"$json_key\": \"$escaped_value\"|g" "$CONFIG_FILE"
    else
        # Replace at root level
        sed -i "s|\"$json_key\"\\s*:\\s*\"[^\"]*\"|\"$json_key\": \"$escaped_value\"|g" "$CONFIG_FILE"
    fi
    
    echo "Replaced $json_key with value from environment"
}

# Replace JSON number value
replace_json_number() {
    local json_key=$1
    local env_value=$2
    local parent_object=$3
    
    if [ -z "$env_value" ]; then
        return 0
    fi
    
    if [ -n "$parent_object" ]; then
        # Replace within a specific parent object
        sed -i "/\"$parent_object\":\\s*{/,/}/ s|\"$json_key\"\\s*:\\s*[0-9]*|\"$json_key\": $env_value|g" "$CONFIG_FILE"
    else
        # Replace at root level
        sed -i "s|\"$json_key\"\\s*:\\s*[0-9]*|\"$json_key\": $env_value|g" "$CONFIG_FILE"
    fi
    
    echo "Replaced $json_key with value from environment"
}

# Replace JSON null value (for optional fields)
replace_json_nullable() {
    local json_key=$1
    local env_value=$2
    local parent_object=$3
    
    if [ -z "$env_value" ]; then
        return 0
    fi
    
    local escaped_value=$(escape_sed "$env_value")
    
    if [ -n "$parent_object" ]; then
        # Replace null or string value within parent object
        sed -i "/\"$parent_object\":\\s*{/,/}/ s|\"$json_key\"\\s*:\\s*[^,}]*|\"$json_key\": \"$escaped_value\"|g" "$CONFIG_FILE"
    else
        # Replace null or string value at root level
        sed -i "s|\"$json_key\"\\s*:\\s*[^,}]*|\"$json_key\": \"$escaped_value\"|g" "$CONFIG_FILE"
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

# Start nginx
exec nginx -g 'daemon off;'
