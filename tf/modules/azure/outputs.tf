########################################
# Azure Module Outputs
########################################

output "webapi_url" {
  description = "URL of the WebApi Container App service"
  value       = "https://${azurerm_container_app.webapi.latest_revision_fqdn}"
}

output "webapi_cors_env_var" {
  description = "Debug: CORS environment variable value that should be set on WebApi"
  value       = var.cors_allowed_origins != "" ? "Cors__AllowedOrigins=${var.cors_allowed_origins}" : "CORS env var not set (empty or default)"
}
