$ErrorActionPreference = "Stop"
$startTime = Get-Date

# Get the script directory and set paths
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$webApiPath = Join-Path $scriptDir "..\WebApi"
$webApiSeedingPath = $scriptDir

# Check if WebApi directory exists
if (-not (Test-Path $webApiPath)) {
    Write-Error "WebApi directory not found at: $webApiPath"
    exit 1
}

Write-Host "=== Database Reset Script ===" -ForegroundColor Cyan
Write-Host ""

# Change to WebApi directory
Push-Location $webApiPath

try {
    # Step 1: Drop all tables in the schema
    Write-Host "[1/6] Dropping existing tables..." -ForegroundColor Yellow
    Pop-Location
    Push-Location $webApiSeedingPath
    $result = dotnet run -- --drop-tables 2>&1 | Out-String
    if ($LASTEXITCODE -eq 0) {
        Write-Host "  Dropped existing tables" -ForegroundColor Green
    } else {
        Write-Host "  Note: Could not drop tables (may not exist)" -ForegroundColor Gray
    }
    Pop-Location
    Push-Location $webApiPath
    # Small delay to ensure connections are closed
    Start-Sleep -Seconds 2

    # Step 2: Remove all existing migrations
    Write-Host ""
    Write-Host "[2/6] Removing existing migrations..." -ForegroundColor Yellow
    $migrationsPath = Join-Path $webApiPath "Migrations"
    if (Test-Path $migrationsPath) {
        $migrationFiles = Get-ChildItem -Path $migrationsPath -Filter "*.cs" -Exclude "*ModelSnapshot.cs"
        if ($migrationFiles.Count -gt 0) {
            Write-Host "  Found $($migrationFiles.Count) migration file(s) to remove"
            # Remove migrations one by one (EF requires this)
            $removedCount = 0
            while ($true) {
                $result = dotnet ef migrations remove --context WebApi.Data.AppDbContext --project . --force 2>&1
                if ($LASTEXITCODE -ne 0) {
                    # Check if error is because there are no more migrations
                    $errorOutput = $result -join "`n"
                    if ($errorOutput -match "No migrations were found" -or $errorOutput -match "no migrations to remove") {
                        break
                    }
                    # If it's a different error, show it
                    Write-Warning "  Migration remove returned exit code $LASTEXITCODE"
                    Write-Host $errorOutput
                    break
                }
                $removedCount++
                Write-Host "  Removed migration $removedCount"
            }
            if ($removedCount -gt 0) {
                Write-Host "  Successfully removed $removedCount migration(s)" -ForegroundColor Green
            }
        } else {
            Write-Host "  No migrations found to remove" -ForegroundColor Gray
        }
    } else {
        Write-Host "  Migrations directory does not exist (will be created)" -ForegroundColor Gray
    }
    
    # Ensure Migrations directory exists
    if (-not (Test-Path $migrationsPath)) {
        New-Item -ItemType Directory -Path $migrationsPath -Force | Out-Null
        Write-Host "  Created Migrations directory" -ForegroundColor Gray
    }

    # Step 3: Create a new migration
    Write-Host ""
    Write-Host "[3/6] Creating new migration..." -ForegroundColor Yellow
    
    # Ensure project is built first
    Write-Host "  Building project..." -ForegroundColor Gray
    $buildResult = dotnet build --no-restore 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Build failed. Output:" -ForegroundColor Red
        $buildResult | ForEach-Object { Write-Host $_ }
        Write-Host "Attempting full build with restore..." -ForegroundColor Yellow
        $buildResult = dotnet build 2>&1
        if ($LASTEXITCODE -ne 0) {
            Write-Error "Failed to build project. Exit code: $LASTEXITCODE"
            $buildResult | ForEach-Object { Write-Host $_ }
            exit 1
        }
    }
    
    $date = Get-Date -Format "yyyyMMddHHmmss"
    $migrationName = "Initial_${date}"
    Write-Host "  Creating migration: $migrationName" -ForegroundColor Gray
    # Use dotnet ef from the project's tools package (avoids global tool version issues)
    $result = dotnet ef migrations add $migrationName --context WebApi.Data.AppDbContext --project . --verbose 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to create migration. Exit code: $LASTEXITCODE" -ForegroundColor Red
        Write-Host "Error output:" -ForegroundColor Red
        $result | ForEach-Object { Write-Host $_ }
        exit 1
    }
    Write-Host "  Created migration: $migrationName" -ForegroundColor Green

    # Step 4: Update database
    Write-Host ""
    Write-Host "[4/6] Updating database..." -ForegroundColor Yellow
    # Retry logic for transient connection issues
    $maxRetries = 3
    $retryCount = 0
    $updateSuccess = $false
    
    while ($retryCount -lt $maxRetries -and -not $updateSuccess) {
        if ($retryCount -gt 0) {
            Write-Host "  Retry attempt $retryCount of $maxRetries..." -ForegroundColor Yellow
            Start-Sleep -Seconds 3
        }
        
        $updateOutput = dotnet ef database update --context WebApi.Data.AppDbContext --project . 2>&1
        if ($LASTEXITCODE -eq 0) {
            Write-Host "  Database updated successfully" -ForegroundColor Green
            $updateSuccess = $true
        } else {
            $retryCount++
            $errorText = $updateOutput -join "`n"
            # Check if it's a connection disposal error (transient)
            if ($errorText -match "ObjectDisposedException" -or $errorText -match "disposed") {
                if ($retryCount -lt $maxRetries) {
                    Write-Host "  Transient error detected, retrying..." -ForegroundColor Yellow
                    continue
                }
            }
            # If it's not a transient error or we've exhausted retries, show the error
            Write-Host "Failed to update database. Exit code: $LASTEXITCODE" -ForegroundColor Red
            Write-Host "Error output:" -ForegroundColor Red
            $updateOutput | ForEach-Object { Write-Host $_ }
            # Don't exit - try to continue with seeding anyway (tables might already exist)
            Write-Host "Continuing with seeding (database may already be up to date)..." -ForegroundColor Yellow
        }
    }

    # Step 5: Seed the database
    Write-Host ""
    Write-Host "[5/6] Seeding database..." -ForegroundColor Yellow
    Pop-Location
    Push-Location $webApiSeedingPath
    
    # Small delay before seeding to ensure database is ready
    Start-Sleep -Seconds 1
    
    $result = dotnet run 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Failed to seed database. Exit code: $LASTEXITCODE" -ForegroundColor Red
        Write-Host "Error output:" -ForegroundColor Red
        $result | ForEach-Object { Write-Host $_ }
        exit 1
    }
    Write-Host "  Database seeded successfully" -ForegroundColor Green

    # Success
    Write-Host ""
    $endTime = Get-Date
    $duration = $endTime - $startTime
    Write-Host "[SUCCESS] Script completed in $($duration.TotalSeconds.ToString('F2')) seconds" -ForegroundColor Green
    Write-Host ""
}
catch {
    Write-Error "An error occurred: $_"
    exit 1
}
finally {
    Pop-Location
}
