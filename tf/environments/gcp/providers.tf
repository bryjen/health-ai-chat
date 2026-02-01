terraform {
  required_version = ">= 1.0"

  required_providers {
    google = {
      source  = "hashicorp/google"
      version = "~> 5.0"
    }
  }

  backend "gcs" {
    # Bucket name - replace with your actual bucket name
    # You can override this during init: terraform init -backend-config="bucket=your-bucket-name"
    # Or create a backend.hcl file (see backend.hcl.example) and use: terraform init -backend-config=backend.hcl
    bucket = "YOUR_PROJECT_NAME-terraform-state"
    prefix = "cloudrun/state"
  }
}

provider "google" {
  project = var.gcp_project_id
  region  = var.gcp_region
}
