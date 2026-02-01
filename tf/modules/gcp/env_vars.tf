########################################
# GCP Environment Variable Helpers
########################################
# Converts shared module env_vars maps to GCP Cloud Run env {} block format

locals {
  # Import shared module configuration
  shared_config = {
    webapi_config = {
      cpu          = var.webapi_cpu
      memory       = var.webapi_memory
      port         = 8080
      min_replicas = var.webapi_min_replicas
      max_replicas = var.webapi_max_replicas
      timeout      = var.webapi_timeout
      concurrency  = var.container_concurrency
    }
    webapi_env_vars = {
      "ASPNETCORE_ENVIRONMENT" = var.environment == "prod" ? "Production" : "Development"
      "OTEL_SERVICE_NAME"      = "WebApi"
    }
    webapi_env_vars_conditional = {
      "ConnectionStrings__DefaultConnection" = var.database_connection_string
      "Jwt__Secret"                          = var.jwt_secret
      "Cors__Enabled"                        = tostring(var.cors_enabled)
      "Cors__AllowedOrigins"                 = var.cors_allowed_origins
      "Email__Resend__ApiKey"                = var.email_resend_api_key
      "Email__Resend__Domain"                = var.email_resend_domain
      "OAuth__Google__ClientId"              = var.oauth_google_client_id
      "OAuth__Microsoft__ClientId"           = var.oauth_microsoft_client_id
      "OAuth__Microsoft__TenantId"           = var.oauth_microsoft_tenant_id
      "OAuth__GitHub__ClientId"              = var.oauth_github_client_id
      "OAuth__GitHub__ClientSecret"          = var.oauth_github_client_secret
      "AzureOpenAI__Endpoint"                = var.azure_openai_endpoint
      "AzureOpenAI__ApiKey"                  = var.azure_openai_api_key
      "AzureOpenAI__DeploymentName"          = var.azure_openai_deployment_name
      "AzureOpenAI__EmbeddingDeploymentName" = var.azure_openai_embedding_deployment_name
      "Twilio__AccountSid"                   = var.twilio_account_sid
      "Twilio__AuthToken"                    = var.twilio_auth_token
      "Twilio__FromPhoneNumber"              = var.twilio_from_phone_number
      "Twilio__BaseUrl"                       = var.twilio_base_url
      "ElevenLabs__ApiKey"                    = var.elevenlabs_api_key
      "ElevenLabs__VoiceId"                   = var.elevenlabs_voice_id
    }
  }

  # Merge and filter empty values for WebApi
  webapi_env_vars_all = merge(
    local.shared_config.webapi_env_vars,
    {
      for k, v in local.shared_config.webapi_env_vars_conditional : k => v
      if v != "" && v != null
    }
  )
}
