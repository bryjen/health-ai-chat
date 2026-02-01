########################################
# Resource Group
########################################

locals {
  # Determine resource group name (must be computed before resources)
  resource_group_name = var.azure_resource_group_name != "" ? var.azure_resource_group_name : "${var.project_name}-rg"
}

resource "azurerm_resource_group" "main" {
  count    = var.create_resource_group ? 1 : 0
  name     = local.resource_group_name
  location = local.location

  tags = {
    Environment = var.environment
    Project     = var.project_name
  }
}

data "azurerm_resource_group" "main" {
  count = var.create_resource_group ? 0 : 1
  name  = local.resource_group_name
}

locals {
  resource_group_id         = var.create_resource_group ? azurerm_resource_group.main[0].id : data.azurerm_resource_group.main[0].id
  resource_group_name_final = var.create_resource_group ? azurerm_resource_group.main[0].name : data.azurerm_resource_group.main[0].name
}

########################################
# Container App Environment
########################################

resource "azurerm_container_app_environment" "main" {
  name                       = "${var.project_name}-env"
  location                   = local.location
  resource_group_name        = local.resource_group_name_final
  log_analytics_workspace_id = azurerm_log_analytics_workspace.main.id
}

########################################
# Log Analytics Workspace (required for Container App Environment)
########################################

resource "azurerm_log_analytics_workspace" "main" {
  name                = "${var.project_name}-logs"
  location            = local.location
  resource_group_name = local.resource_group_name_final
  sku                 = "PerGB2018"
  retention_in_days   = 30
}

########################################
# Container App: WebApi
########################################

resource "azurerm_container_app" "webapi" {
  name                         = "${var.project_name}-webapi"
  container_app_environment_id = azurerm_container_app_environment.main.id
  resource_group_name          = local.resource_group_name_final
  revision_mode                = "Single"

  template {
    container {
      name   = "webapi"
      image  = var.webapi_image != "" ? var.webapi_image : "${var.acr_name}.azurecr.io/${var.project_name}-webapi:latest"
      cpu    = local.shared_config.webapi_config.cpu
      memory = local.shared_config.webapi_config.memory

      # Convert env vars map to Azure env blocks
      dynamic "env" {
        for_each = local.webapi_env_vars_all
        content {
          name  = env.key
          value = env.value
        }
      }
    }

    min_replicas = local.shared_config.webapi_config.min_replicas
    max_replicas = local.shared_config.webapi_config.max_replicas
  }

  ingress {
    external_enabled           = true
    target_port                = local.shared_config.webapi_config.port
    transport                  = "http"
    allow_insecure_connections = false

    traffic_weight {
      percentage      = 100
      latest_revision = true
    }
  }

  lifecycle {
    prevent_destroy = true
  }
}

