param(
    [Parameter(Mandatory=$true)]
    [string]$AccountName,
    [Parameter(Mandatory=$true)]
    [string]$SubscriptionId,
    [string]$ResourceGroupName = "",
    [string]$Location = "eastus"
)

Write-Host "Account Name: $AccountName"
Write-Host "Subscription ID: $SubscriptionId"
Write-Host "Resource Group: $ResourceGroupName"
Write-Host "Location: $Location"

# Set the active subscription
az account set --subscription $SubscriptionId

# Register required resource providers (equivalent to enabling APIs in GCP)
Write-Host "Checking and registering required resource providers..."
$providers = @("Microsoft.App", "Microsoft.OperationalInsights", "Microsoft.ContainerRegistry", "Microsoft.Storage", "Microsoft.Compute")

foreach ($provider in $providers) {
    $status = az provider show --namespace $provider --query "registrationState" -o tsv 2>$null
    if ($status -eq "Registered") {
        Write-Host "  $provider is already registered"
    } else {
        Write-Host "  Registering $provider..."
        az provider register --namespace $provider
    }
}

Write-Host "Provider registration initiated (may take a few minutes to complete)"
Write-Host "Continuing with service principal creation..."

# Create resource group if not provided
if ([string]::IsNullOrWhiteSpace($ResourceGroupName)) {
    $ResourceGroupName = "$AccountName-rg"
    Write-Host "Creating resource group: $ResourceGroupName"
    az group create --name $ResourceGroupName --location $Location
}

# Service Principal name
$ServicePrincipalName = "$AccountName-sp"

# Create service principal (equivalent to service account)
Write-Host "Creating service principal: $ServicePrincipalName"
$spOutput = az ad sp create-for-rbac --name $ServicePrincipalName --role contributor --scopes /subscriptions/$SubscriptionId --output json | ConvertFrom-Json

$appId = $spOutput.appId
$password = $spOutput.password

Write-Host "Service principal created with App ID: $appId"

# Assign additional roles at subscription level
Write-Host "Assigning RBAC roles..."

# Container Apps Contributor (equivalent to run.admin)
az role assignment create --assignee $appId --role "Container Apps Contributor" --scope /subscriptions/$SubscriptionId

# Storage Blob Data Contributor (equivalent to storage.admin and storage.objectAdmin)
az role assignment create --assignee $appId --role "Storage Blob Data Contributor" --scope /subscriptions/$SubscriptionId

# AcrPush (equivalent to artifactregistry.admin) - for pushing images
az role assignment create --assignee $appId --role "AcrPush" --scope /subscriptions/$SubscriptionId

# Billing Reader (equivalent to billing.viewer)
az role assignment create --assignee $appId --role "Billing Reader" --scope /subscriptions/$SubscriptionId

# User Access Administrator (equivalent to iam.serviceAccountUser) - for managing IAM
az role assignment create --assignee $appId --role "User Access Administrator" --scope /subscriptions/$SubscriptionId

# Create credentials JSON file (similar to GCP service account key)
# Format: <name>@<sub_id>.json
$CredentialsFileName = "$AccountName@$SubscriptionId.json"
$CredentialsObject = @{
    clientId       = $appId
    clientSecret   = $password
    subscriptionId = $SubscriptionId
    tenantId       = (az account show --query tenantId -o tsv)
    resourceGroup  = $ResourceGroupName
    location       = $Location
} | ConvertTo-Json -Depth 10

$CredentialsObject | Out-File -FilePath $CredentialsFileName -Encoding utf8

Write-Host ""
Write-Host "=========================================="
Write-Host "Service principal created successfully!"
Write-Host "=========================================="
Write-Host "App ID (Client ID): $appId"
Write-Host "Client Secret: $password"
Write-Host "Tenant ID: $($CredentialsObject | ConvertFrom-Json | Select-Object -ExpandProperty tenantId)"
Write-Host "Credentials saved to: $CredentialsFileName"
Write-Host ""
Write-Host "To use these credentials:"
Write-Host "  az login --service-principal -u $appId -p $password --tenant $($CredentialsObject | ConvertFrom-Json | Select-Object -ExpandProperty tenantId)"
Write-Host ""
Write-Host "Or set environment variables:"
Write-Host "  `$env:ARM_CLIENT_ID='$appId'"
Write-Host "  `$env:ARM_CLIENT_SECRET='$password'"
Write-Host "  `$env:ARM_SUBSCRIPTION_ID='$SubscriptionId'"
Write-Host "  `$env:ARM_TENANT_ID='$($CredentialsObject | ConvertFrom-Json | Select-Object -ExpandProperty tenantId)'"
Write-Host ""
