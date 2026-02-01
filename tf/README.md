# Multi-Cloud Terraform Configuration

This directory contains a modular Terraform configuration that supports deployment to both Google Cloud Platform (GCP) and Microsoft Azure.

## Architecture

The configuration is organized into three layers:

1. **Shared Module** (`modules/shared/`) - Common variables, environment variable definitions, and configuration abstractions
2. **Provider Modules** (`modules/gcp/`, `modules/azure/`) - Cloud-specific resource implementations
3. **Environment Configurations** (`environments/gcp/`, `environments/azure/`) - Deployment entry points

## Directory Structure

```
tf/
├── modules/
│   ├── shared/          # Common configuration
│   ├── gcp/             # GCP-specific resources (Cloud Run)
│   └── azure/           # Azure-specific resources (Container Apps)
├── environments/
│   ├── gcp/             # GCP deployment configuration
│   └── azure/           # Azure deployment configuration
├── scripts/
│   └── deploy.sh        # Deployment wrapper script
└── gcp/                 # Legacy GCP config (maintained for backward compatibility)
```

## Quick Start

### Deploy to GCP

```bash
cd tf/environments/gcp
terraform init
terraform plan
terraform apply
```

Or use the deployment script:

```bash
cd tf/scripts
./deploy.sh gcp plan
./deploy.sh gcp apply
```

### Deploy to Azure

```bash
cd tf/environments/azure
terraform init
terraform plan
terraform apply
```

Or use the deployment script:

```bash
cd tf/scripts
./deploy.sh azure plan
./deploy.sh azure apply
```

## Configuration

### GCP-Specific Variables

- `gcp_project_id` - GCP Project ID
- `gcp_region` - GCP region (default: `us-central1`)

### Azure-Specific Variables

- `azure_location` - Azure region (default: `eastus`)
- `azure_resource_group_name` - Resource group name (optional, will be created if not provided)
- `acr_name` - Azure Container Registry name
- `create_resource_group` - Whether to create a new resource group (default: `true`)

### Shared Application Variables

All application configuration variables are shared between providers:

- `environment` - Environment name (dev, staging, prod)
- `project_name` - Project name for resource naming
- `webapi_image` - Container image for WebApi
- `database_connection_string` - Database connection string
- `jwt_secret` - JWT secret key
- `cors_enabled` - Enable or disable CORS (default: `true`, set to `false` to completely disable CORS)
- `cors_allowed_origins` - CORS allowed origins (comma-separated or single origin)
- OAuth configuration variables
- Container resource limits (CPU, memory, replicas, timeouts)

See `environments/gcp/variables.tf` or `environments/azure/variables.tf` for the complete list.

## Backend Configuration

### GCP Backend (GCS)

Configure in `environments/gcp/providers.tf`:

```hcl
backend "gcs" {
  bucket = "YOUR_PROJECT_NAME-terraform-state"
  prefix = "cloudrun/state"
}
```

### Azure Backend (Azure Storage)

Configure in `environments/azure/providers.tf`:

```hcl
backend "azurerm" {
  resource_group_name  = "terraform-state-rg"
  storage_account_name = "terraformstate"
  container_name       = "tfstate"
  key                  = "containerapps/state.terraform.tfstate"
}
```

## Key Features

- **~70% code reuse**: All application configuration defined once in the shared module
- **Single source of truth**: Environment variables and container config in one place
- **Consistent outputs**: Same output structure regardless of provider
- **Easy to extend**: Adding new providers only requires a new provider module

## Migration from Legacy Configuration

The original GCP configuration in `tf/gcp/` is preserved for backward compatibility. To migrate:

1. Copy your variable values from `tf/gcp/terraform.tfvars` (if you have one) to `tf/environments/gcp/terraform.tfvars`
2. Update backend configuration in `tf/environments/gcp/providers.tf`
3. Run `terraform init` to migrate state
4. Deploy using the new structure

## Notes

- The deployment script (`scripts/deploy.sh`) requires bash (works on Linux, Mac, Git Bash, or WSL on Windows)
- Container images should be pushed to the appropriate registry (GCR for GCP, ACR for Azure) before deployment
- Database connection strings and secrets should be provided via environment variables or secure variable files
