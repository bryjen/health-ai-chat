#!/bin/bash

########################################
# GCP Terraform Deployment Script
########################################
# Usage: ./deploy.sh [plan|apply|destroy] [options]
#
# Options:
#   --backend-config="key=value"  - Backend configuration (can be used multiple times)
#   --migrate-state                - Migrate state during init
#   --plan-file=FILE               - Save plan to file (for plan) or apply saved plan (for apply)
#   --auto-approve                 - Skip confirmation prompts
#
# Examples:
#   ./deploy.sh plan
#   ./deploy.sh plan --plan-file=cloud_run
#   ./deploy.sh init --backend-config="bucket=my-bucket" --migrate-state
#   ./deploy.sh apply --plan-file=cloud_run --auto-approve
#   ./deploy.sh apply --auto-approve

set -e

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
NC='\033[0m' # No Color

# Script directory
SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TF_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"

# Parse arguments
ACTION="${1:-plan}"

# Parse options (skip first positional arg)
BACKEND_CONFIGS=()
MIGRATE_STATE=false
PLAN_FILE=""
AUTO_APPROVE=false

shift 1 2>/dev/null || true

while [[ $# -gt 0 ]]; do
    case $1 in
        --backend-config=*)
            BACKEND_CONFIGS+=("${1#*=}")
            shift
            ;;
        --backend-config)
            if [ -z "$2" ]; then
                echo -e "${RED}Error: --backend-config requires a value${NC}"
                exit 1
            fi
            BACKEND_CONFIGS+=("$2")
            shift 2
            ;;
        --migrate-state)
            MIGRATE_STATE=true
            shift
            ;;
        --plan-file=*)
            PLAN_FILE="${1#*=}"
            shift
            ;;
        --plan-file)
            if [ -z "$2" ]; then
                echo -e "${RED}Error: --plan-file requires a value${NC}"
                exit 1
            fi
            PLAN_FILE="$2"
            shift 2
            ;;
        --auto-approve)
            AUTO_APPROVE=true
            shift
            ;;
        *)
            echo -e "${RED}Error: Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

# Validate action
if [ "$ACTION" != "plan" ] && [ "$ACTION" != "apply" ] && [ "$ACTION" != "destroy" ] && [ "$ACTION" != "init" ]; then
    echo -e "${RED}Error: Invalid action '$ACTION'. Must be 'init', 'plan', 'apply', or 'destroy'${NC}"
    exit 1
fi

# Set terraform directory
ENV_DIR="$TF_ROOT"

if [ ! -d "$ENV_DIR" ]; then
    echo -e "${RED}Error: Terraform directory not found: $ENV_DIR${NC}"
    exit 1
fi

# Change to terraform directory
cd "$ENV_DIR"

echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Deploying to GCP${NC}"
echo -e "${GREEN}Action: $ACTION${NC}"
echo -e "${GREEN}Directory: $ENV_DIR${NC}"
echo -e "${GREEN}========================================${NC}"
echo ""

# Check if terraform is installed
if ! command -v terraform &> /dev/null; then
    echo -e "${RED}Error: terraform command not found. Please install Terraform.${NC}"
    exit 1
fi

# Check GCP authentication
echo -e "${YELLOW}Checking GCP authentication...${NC}"
if ! command -v gcloud &> /dev/null; then
    echo -e "${YELLOW}Warning: gcloud CLI not found. Make sure you're authenticated.${NC}"
else
    echo -e "${GREEN}gcloud CLI found${NC}"
fi

echo ""

# Build backend config arguments
BACKEND_ARGS=()
for config in "${BACKEND_CONFIGS[@]}"; do
    BACKEND_ARGS+=(-backend-config)
    BACKEND_ARGS+=("$config")
done

# Initialize Terraform
INIT_NEEDED=false
if [ "$ACTION" == "init" ]; then
    INIT_NEEDED=true
elif [ ! -d ".terraform" ]; then
    INIT_NEEDED=true
fi

if [ "$INIT_NEEDED" == "true" ]; then
    echo -e "${YELLOW}Initializing Terraform...${NC}"
    
    # Build init command with backend config
    INIT_ARGS=("terraform" "init")
    for config in "${BACKEND_ARGS[@]}"; do
        INIT_ARGS+=("$config")
    done
    
    if [ "$MIGRATE_STATE" == "true" ]; then
        INIT_ARGS+=("-migrate-state")
        # Auto-approve migration by piping "yes" to the command
        echo "yes" | "${INIT_ARGS[@]}"
    else
        "${INIT_ARGS[@]}"
    fi
    echo ""
fi

# Execute Terraform command
case "$ACTION" in
    init)
        # Already handled above
        ;;
    plan)
        echo -e "${GREEN}Running terraform plan...${NC}"
        PLAN_ARGS=("terraform" "plan")
        if [ -n "$PLAN_FILE" ]; then
            PLAN_ARGS+=("-out=$PLAN_FILE")
            echo -e "${YELLOW}Plan will be saved to: $PLAN_FILE${NC}"
        fi
        "${PLAN_ARGS[@]}"
        ;;
    apply)
        echo -e "${GREEN}Running terraform apply...${NC}"
        
        # Check if applying a saved plan file
        if [ -n "$PLAN_FILE" ]; then
            if [ ! -f "$PLAN_FILE" ]; then
                echo -e "${RED}Error: Plan file not found: $PLAN_FILE${NC}"
                exit 1
            fi
            echo -e "${YELLOW}Applying saved plan file: $PLAN_FILE${NC}"
            APPLY_ARGS=("terraform" "apply")
            if [ "$AUTO_APPROVE" == "true" ]; then
                APPLY_ARGS+=("-auto-approve")
            fi
            APPLY_ARGS+=("$PLAN_FILE")
            "${APPLY_ARGS[@]}"
        else
            echo -e "${YELLOW}This will make changes to your infrastructure.${NC}"
            if [ "$AUTO_APPROVE" != "true" ]; then
                read -p "Do you want to continue? (yes/no): " confirm
                if [ "$confirm" != "yes" ]; then
                    echo -e "${YELLOW}Aborted.${NC}"
                    exit 0
                fi
            fi
            APPLY_ARGS=("terraform" "apply")
            if [ "$AUTO_APPROVE" == "true" ]; then
                APPLY_ARGS+=("-auto-approve")
            fi
            "${APPLY_ARGS[@]}"
        fi
        ;;
    destroy)
        echo -e "${RED}WARNING: This will destroy all infrastructure in GCP!${NC}"
        if [ "$AUTO_APPROVE" != "true" ]; then
            read -p "Type 'yes' to confirm: " confirm
            if [ "$confirm" != "yes" ]; then
                echo -e "${YELLOW}Aborted.${NC}"
                exit 0
            fi
        fi
        DESTROY_CMD="terraform destroy"
        if [ "$AUTO_APPROVE" == "true" ]; then
            DESTROY_CMD="$DESTROY_CMD -auto-approve"
        fi
        $DESTROY_CMD
        ;;
esac

echo ""
echo -e "${GREEN}========================================${NC}"
echo -e "${GREEN}Deployment script completed${NC}"
echo -e "${GREEN}========================================${NC}"
