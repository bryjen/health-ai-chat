########################################
# Container Configuration Objects
########################################

locals {
  webapi_config = {
    cpu            = var.webapi_cpu
    memory         = var.webapi_memory
    port           = 8080
    min_replicas   = var.webapi_min_replicas
    max_replicas   = var.webapi_max_replicas
    timeout        = var.webapi_timeout
    concurrency    = var.container_concurrency
  }
}
