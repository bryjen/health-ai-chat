########################################
# Cloud Run Service: WebApi
########################################

resource "google_cloud_run_service" "webapi" {
  name     = "${var.project_name}-webapi"
  location = local.cloud_run_location

  lifecycle {
    prevent_destroy = true
  }

  template {
    spec {
      containers {
        image = var.webapi_image != "" ? var.webapi_image : "gcr.io/${var.gcp_project_id}/${var.project_name}-webapi:latest"

        ports {
          container_port = local.shared_config.webapi_config.port
        }

        resources {
          limits = {
            cpu    = local.shared_config.webapi_config.cpu
            memory = local.shared_config.webapi_config.memory
          }
        }

        # Static environment variables
        dynamic "env" {
          for_each = local.webapi_env_vars_all
          content {
            name  = env.key
            value = env.value
          }
        }
      }

      container_concurrency = local.shared_config.webapi_config.concurrency
      timeout_seconds      = local.shared_config.webapi_config.timeout
    }

    metadata {
      annotations = {
        "autoscaling.knative.dev/maxScale" = tostring(local.shared_config.webapi_config.max_replicas)
        "autoscaling.knative.dev/minScale" = tostring(local.shared_config.webapi_config.min_replicas)
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
