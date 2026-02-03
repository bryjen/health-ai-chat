########################################
# GCP Environment Configuration
########################################
# This file calls the GCP module with all required variables

module "gcp_deployment" {
  source = "../../modules/gcp"

  # GCP-specific variables
  gcp_project_id = var.gcp_project_id
  gcp_region     = var.gcp_region

  # Shared application variables
  environment  = var.environment
  project_name = var.project_name

  # Container images
  webapi_image = var.webapi_image

  # App configuration / secrets
  database_connection_string = var.database_connection_string
  jwt_secret                 = var.jwt_secret
  cors_enabled               = var.cors_enabled
  cors_allowed_origins       = var.cors_allowed_origins
  email_resend_api_key       = var.email_resend_api_key
  email_resend_domain        = var.email_resend_domain

  # OAuth configuration
  oauth_google_client_id     = var.oauth_google_client_id
  oauth_microsoft_client_id  = var.oauth_microsoft_client_id
  oauth_microsoft_tenant_id  = var.oauth_microsoft_tenant_id
  oauth_github_client_id     = var.oauth_github_client_id
  oauth_github_client_secret = var.oauth_github_client_secret

  # Azure OpenAI configuration
  azure_openai_endpoint                  = var.azure_openai_endpoint
  azure_openai_api_key                   = var.azure_openai_api_key
  azure_openai_deployment_name           = var.azure_openai_deployment_name
  azure_openai_embedding_deployment_name = var.azure_openai_embedding_deployment_name

  # Twilio configuration
  twilio_account_sid      = var.twilio_account_sid
  twilio_auth_token       = var.twilio_auth_token
  twilio_from_phone_number = var.twilio_from_phone_number
  twilio_base_url         = var.twilio_base_url

  # ElevenLabs configuration
  elevenlabs_api_key = var.elevenlabs_api_key
  elevenlabs_voice_id = var.elevenlabs_voice_id
}

########################################
# Outputs (pass through from module)
########################################

output "webapi_url" {
  description = "URL of the WebApi service"
  value       = module.gcp_deployment.webapi_url
}

output "webapi_cors_env_var" {
  description = "Debug: CORS environment variable value"
  value       = module.gcp_deployment.webapi_cors_env_var
}
