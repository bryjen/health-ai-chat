########################################
# GCP Terraform Configuration
# Cloud Run services for WebApi and WebFrontend
########################################

########################################
# Cloud Run Service: WebApi
########################################

resource "google_cloud_run_service" "webapi" {
  name     = "ai-health-app-backend"
  location = local.cloud_run_location

  lifecycle {
    prevent_destroy = true
  }

  template {
    spec {
      containers {
        image = var.webapi_image != "" ? var.webapi_image : "gcr.io/${var.gcp_project_id}/${var.project_name}-webapi:latest"

        ports {
          container_port = local.webapi_config.port
        }

        resources {
          limits = {
            cpu    = local.webapi_config.cpu
            memory = local.webapi_config.memory
          }
        }

        # Environment variables
        dynamic "env" {
          for_each = local.webapi_env_vars_all
          content {
            name  = env.key
            value = env.value
          }
        }
      }

      container_concurrency = local.webapi_config.concurrency
      timeout_seconds      = local.webapi_config.timeout
    }

    metadata {
      annotations = {
        "autoscaling.knative.dev/maxScale" = tostring(local.webapi_config.max_replicas)
        "autoscaling.knative.dev/minScale" = tostring(local.webapi_config.min_replicas)
      }
    }
  }

  traffic {
    percent         = 100
    latest_revision = true
  }
}

########################################
# Cloud Run Service: WebFrontend
########################################

resource "google_cloud_run_service" "webfrontend" {
  name     = "ai-health-app-frontend"
  location = local.cloud_run_location

  lifecycle {
    prevent_destroy = true
  }

  template {
    spec {
      containers {
        image = var.webfrontend_image != "" ? var.webfrontend_image : "gcr.io/${var.gcp_project_id}/${var.project_name}-webfrontend:latest"

        ports {
          container_port = local.webfrontend_config.port
        }

        resources {
          limits = {
            cpu    = local.webfrontend_config.cpu
            memory = local.webfrontend_config.memory
          }
        }
      }

      container_concurrency = local.webfrontend_config.concurrency
      timeout_seconds      = local.webfrontend_config.timeout
    }

    metadata {
      annotations = {
        "autoscaling.knative.dev/maxScale" = tostring(local.webfrontend_config.max_replicas)
        "autoscaling.knative.dev/minScale" = tostring(local.webfrontend_config.min_replicas)
      }
    }
  }

  traffic {
    percent         = 100
    latest_revision = true
  }
}

########################################
# IAM: Public access for WebApi service
########################################

resource "google_cloud_run_service_iam_member" "webapi_public" {
  service  = google_cloud_run_service.webapi.name
  location = google_cloud_run_service.webapi.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}

########################################
# IAM: Public access for WebFrontend service
########################################

resource "google_cloud_run_service_iam_member" "webfrontend_public" {
  service  = google_cloud_run_service.webfrontend.name
  location = google_cloud_run_service.webfrontend.location
  role     = "roles/run.invoker"
  member   = "allUsers"
}
