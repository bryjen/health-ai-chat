########################################
# Container Configuration Objects
########################################

locals {
  webapi_config = {
    cpu            = "0.5"
    memory         = "512Mi"
    port           = 8080
    min_replicas   = 0
    max_replicas   = 2
    timeout        = 60
    concurrency    = 1
  }
}
