########################################
# GCP Provider Configuration
########################################
# Note: Provider configuration is typically done at the environment level,
# but this file can be used for module-specific provider requirements.

# This module expects the Google provider to be configured at the root level
# with the following variables:
# - project = var.gcp_project_id
# - region  = var.gcp_region

locals {
  cloud_run_location = var.gcp_region
}
