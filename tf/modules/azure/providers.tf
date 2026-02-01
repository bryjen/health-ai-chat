########################################
# Azure Provider Configuration
########################################
# Note: Provider configuration is typically done at the environment level,
# but this file can be used for module-specific provider requirements.

# This module expects the Azure provider to be configured at the root level
# with appropriate authentication and subscription context.

locals {
  location = var.azure_location
}
