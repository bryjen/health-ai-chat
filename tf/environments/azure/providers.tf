terraform {
  required_version = ">= 1.0"

  required_providers {
    azurerm = {
      source  = "hashicorp/azurerm"
      version = "~> 3.0"
    }
  }

  backend "azurerm" {
    # Storage account name - replace with your actual storage account name
    # You can override this during init: terraform init -backend-config="storage_account_name=your-storage-account"
    # Or create a backend.hcl file and use: terraform init -backend-config=backend.hcl
    resource_group_name  = "terraform-state-rg"
    storage_account_name = "terraformstate"
    container_name       = "tfstate"
    key                  = "containerapps/state.terraform.tfstate"
  }
}

provider "azurerm" {
  features {}
}
