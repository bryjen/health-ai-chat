param(
    [string]$ProjectName,
    [string]$ProjectId
)

Write-Host "Passed project Name: $ProjectName"
Write-Host "Passed project ID: $ProjectId"

# enable required services, if disabled
gcloud services enable compute.googleapis.com --project=$ProjectId
gcloud services enable cloudbilling.googleapis.com --project=$ProjectId
gcloud services enable run.googleapis.com --project=$ProjectId
gcloud services enable artifactregistry.googleapis.com --project=$ProjectId
gcloud services enable containerregistry.googleapis.com --project=$ProjectId

$ServiceAccountName = "$ProjectName-sa"
$ServiceAccountEmail = "$ServiceAccountName@$ProjectId.iam.gserviceaccount.com"

# create service account & grant roles
gcloud iam service-accounts create $ServiceAccountName --display-name="$ProjectName Service Account" --project=$ProjectId
gcloud projects add-iam-policy-binding $ProjectId --member="serviceAccount:$ServiceAccountEmail" --role="roles/compute.admin"
gcloud projects add-iam-policy-binding $ProjectId --member="serviceAccount:$ServiceAccountEmail" --role="roles/billing.viewer"
gcloud projects add-iam-policy-binding $ProjectId --member="serviceAccount:$ServiceAccountEmail" --role="roles/run.admin"
gcloud projects add-iam-policy-binding $ProjectId --member="serviceAccount:$ServiceAccountEmail" --role="roles/artifactregistry.admin"
gcloud projects add-iam-policy-binding $ProjectId --member="serviceAccount:$ServiceAccountEmail" --role="roles/storage.admin"
gcloud projects add-iam-policy-binding $ProjectId --member="serviceAccount:$ServiceAccountEmail" --role="roles/iam.serviceAccountUser"
gcloud projects add-iam-policy-binding $ProjectId --member="serviceAccount:$ServiceAccountEmail" --role="roles/storage.objectAdmin"

# create key and print stuff
$CredentialsFileName = "${ServiceAccountEmail}.json"
gcloud iam service-accounts keys create $CredentialsFileName --iam-account=$ServiceAccountEmail
Write-Host "Service account created: $ServiceAccountEmail"
Write-Host "Credentials saved to: $CredentialsFileName"

# commands for using the credentials & testing em
# gcloud auth activate-service-account --key-file=./service-account.json
# gcloud compute instances list