# GCP Terraform Configuration

This directory contains a modular Terraform configuration for deployment to Google Cloud Platform (GCP).

## Architecture

The configuration is organized into two layers:

1. **Shared Module** (`modules/shared/`) - Common variables, environment variable definitions, and configuration abstractions
2. **GCP Module** (`modules/gcp/`) - GCP-specific resource implementations (Cloud Run)
3. **Environment Configuration** (`environments/gcp/`) - Deployment entry point

## Directory Structure

```
tf/
├── modules/
│   ├── shared/          # Common configuration
│   └── gcp/             # GCP-specific resources (Cloud Run)
├── environments/
│   └── gcp/             # GCP deployment configuration
├── scripts/
│   └── deploy.sh        # Deployment wrapper script
└── accs/
    └── gcp/             # GCP service account creation scripts
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

## Configuration

### GCP-Specific Variables

- `gcp_project_id` - GCP Project ID
- `gcp_region` - GCP region (default: `us-central1`)

### Shared Application Variables

All application configuration variables:

- `environment` - Environment name (dev, staging, prod)
- `project_name` - Project name for resource naming
- `webapi_image` - Container image for WebApi
- `database_connection_string` - Database connection string
- `jwt_secret` - JWT secret key
- `cors_enabled` - Enable or disable CORS (default: `true`, set to `false` to completely disable CORS)
- `cors_allowed_origins` - CORS allowed origins (comma-separated or single origin)
- OAuth configuration variables
- Azure OpenAI configuration variables (for Azure OpenAI service)
- Container resource limits (CPU, memory, replicas, timeouts)

See `environments/gcp/variables.tf` for the complete list.

## Backend Configuration

### GCP Backend (GCS)

Configure in `environments/gcp/providers.tf`:

```hcl
backend "gcs" {
  bucket = "YOUR_PROJECT_NAME-terraform-state"
  prefix = "cloudrun/state"
}
```

## Key Features

- **Modular design**: Application configuration defined once in the shared module
- **Single source of truth**: Environment variables and container config in one place
- **Easy to extend**: Adding new services only requires updating the GCP module

## Notes

- The deployment script (`scripts/deploy.sh`) requires bash (works on Linux, Mac, Git Bash, or WSL on Windows)
- Container images should be pushed to GCR before deployment
- Database connection strings and secrets should be provided via environment variables or secure variable files
