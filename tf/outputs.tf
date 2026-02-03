########################################
# Outputs
########################################

output "version" {
  description = "Version information"
  value       = local.version
}

output "webapi_url" {
  description = "URL of the WebApi Cloud Run service"
  value       = google_cloud_run_service.webapi.status[0].url
}

output "webfrontend_url" {
  description = "URL of the WebFrontend Cloud Run service"
  value       = google_cloud_run_service.webfrontend.status[0].url
}

output "webapi_cors_env_var" {
  description = "Debug: CORS environment variable value that should be set on WebApi"
  value       = var.cors_allowed_origins != "" ? "Cors__AllowedOrigins=${var.cors_allowed_origins}" : "CORS env var not set (empty or default)"
}
