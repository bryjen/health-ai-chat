########################################
# Locals: Container Configuration
########################################

locals {
  cloud_run_location = var.gcp_region

  # Version information
  version = {
    Major        = var.version_major
    Minor        = var.version_minor
    Patch        = var.version_patch
    PreRelease   = var.version_prerelease
    BuildMetadata = var.version_build_metadata
  }

  # Container resource configuration
  webapi_config = {
    cpu          = "0.5"
    memory       = "512Mi"
    port         = 8080
    min_replicas = 0
    max_replicas = 2
    timeout      = 60
    concurrency  = 1
  }

  webfrontend_config = {
    cpu          = "0.5"
    memory       = "512Mi"
    port         = 8080
    min_replicas = 0
    max_replicas = 2
    timeout      = 60
    concurrency  = 1
  }

  # WebApi environment variables
  webapi_env_vars = {
    "ASPNETCORE_ENVIRONMENT" = var.environment == "prod" ? "Production" : "Development"
    "OTEL_SERVICE_NAME"      = "WebApi"
  }

  # WebApi conditional environment variables (only included if values are provided)
  webapi_env_vars_conditional = {
    "ConnectionStrings__DefaultConnection" = var.database_connection_string
    "Jwt__Secret"                         = var.jwt_secret
    "Cors__Enabled"                       = tostring(var.cors_enabled)
    "Cors__AllowedOrigins"                = var.cors_allowed_origins
    "Email__Resend__ApiKey"               = var.email_resend_api_key
    "Email__Resend__Domain"                = var.email_resend_domain
    "OAuth__Google__ClientId"              = var.oauth_google_client_id
    "OAuth__Microsoft__ClientId"          = var.oauth_microsoft_client_id
    "OAuth__Microsoft__TenantId"          = var.oauth_microsoft_tenant_id
    "OAuth__GitHub__ClientId"             = var.oauth_github_client_id
    "OAuth__GitHub__ClientSecret"         = var.oauth_github_client_secret
    "AzureOpenAI__Endpoint"                = var.azure_openai_endpoint
    "AzureOpenAI__ApiKey"                  = var.azure_openai_api_key
    "AzureOpenAI__DeploymentName"         = var.azure_openai_deployment_name
    "AzureOpenAI__EmbeddingDeploymentName" = var.azure_openai_embedding_deployment_name
    "Twilio__AccountSid"                   = var.twilio_account_sid
    "Twilio__AuthToken"                    = var.twilio_auth_token
    "Twilio__FromPhoneNumber"              = var.twilio_from_phone_number
    "Twilio__BaseUrl"                       = var.twilio_base_url
    "ElevenLabs__ApiKey"                    = var.elevenlabs_api_key
    "ElevenLabs__VoiceId"                   = var.elevenlabs_voice_id
  }

  # Merge and filter empty values for WebApi
  webapi_env_vars_all = merge(
    local.webapi_env_vars,
    {
      for k, v in local.webapi_env_vars_conditional : k => v
      if v != "" && v != null
    }
  )
}
