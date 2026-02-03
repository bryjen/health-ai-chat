########################################
# GCP-Specific Variables
########################################

variable "gcp_project_id" {
  description = "GCP Project ID"
  type        = string
  default     = "YOUR_PROJECT_ID"
}

variable "gcp_region" {
  description = "GCP region for resources"
  type        = string
  default     = "us-central1"
}

########################################
# Shared Application Variables
########################################

variable "environment" {
  description = "Environment name (dev, staging, prod)"
  type        = string
  default     = "dev"
}

variable "project_name" {
  description = "Project name used for resource naming"
  type        = string
  default     = "YOUR_PROJECT_NAME"
}

variable "webapi_image" {
  description = "Docker image for WebApi (e.g., gcr.io/PROJECT_ID/asptemplate-webapi:latest)"
  type        = string
  default     = ""
}

variable "database_connection_string" {
  description = "Database connection string"
  type        = string
  sensitive   = true
  default     = ""
}

variable "jwt_secret" {
  description = "JWT secret key"
  type        = string
  sensitive   = true
  default     = ""
}

variable "cors_enabled" {
  description = "Enable or disable CORS. Set to false to completely disable CORS."
  type        = bool
  default     = true
}

variable "cors_allowed_origins" {
  description = "CORS allowed origins (single origin, or comma-separated)"
  type        = string
  default     = ""
}

variable "email_resend_api_key" {
  description = "Resend API key for email service"
  type        = string
  sensitive   = true
  default     = ""
}

variable "email_resend_domain" {
  description = "Resend domain for email service"
  type        = string
  default     = ""
}

variable "oauth_google_client_id" {
  description = "Google OAuth Client ID"
  type        = string
  sensitive   = false
  default     = ""
}

variable "oauth_microsoft_client_id" {
  description = "Microsoft OAuth Client ID"
  type        = string
  sensitive   = false
  default     = ""
}

variable "oauth_microsoft_tenant_id" {
  description = "Microsoft OAuth Tenant ID (default: 'common')"
  type        = string
  sensitive   = false
  default     = "common"
}

variable "oauth_github_client_id" {
  description = "GitHub OAuth Client ID"
  type        = string
  sensitive   = false
  default     = ""
}

variable "oauth_github_client_secret" {
  description = "GitHub OAuth Client Secret"
  type        = string
  sensitive   = true
  default     = ""
}

variable "azure_openai_endpoint" {
  description = "Azure OpenAI endpoint URL"
  type        = string
  sensitive   = false
  default     = ""
}

variable "azure_openai_api_key" {
  description = "Azure OpenAI API key"
  type        = string
  sensitive   = true
  default     = ""
}

variable "azure_openai_deployment_name" {
  description = "Azure OpenAI deployment name"
  type        = string
  sensitive   = false
  default     = ""
}

variable "azure_openai_embedding_deployment_name" {
  description = "Azure OpenAI embedding deployment name"
  type        = string
  sensitive   = false
  default     = ""
}

variable "twilio_account_sid" {
  description = "Twilio Account SID"
  type        = string
  sensitive   = true
  default     = ""
}

variable "twilio_auth_token" {
  description = "Twilio Auth Token"
  type        = string
  sensitive   = true
  default     = ""
}

variable "twilio_from_phone_number" {
  description = "Twilio phone number to use for outbound calls (e.g., +1234567890)"
  type        = string
  sensitive   = false
  default     = ""
}

variable "twilio_base_url" {
  description = "Base URL for TwiML webhook callbacks (e.g., https://your-domain.com)"
  type        = string
  sensitive   = false
  default     = ""
}

variable "elevenlabs_api_key" {
  description = "ElevenLabs API key for text-to-speech"
  type        = string
  sensitive   = true
  default     = ""
}

variable "elevenlabs_voice_id" {
  description = "ElevenLabs voice ID to use for text-to-speech"
  type        = string
  sensitive   = false
  default     = ""
}
