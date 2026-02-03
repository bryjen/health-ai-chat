#!/usr/bin/env pwsh
########################################
# Generate TF_VAR export statements from Terraform variable definitions
########################################
# This script scans Terraform variable files and generates export statements
# grouped by comment sections in the format: export TF_VAR_<variable_name>="%env.TF_VAR_<variable_name>%"

param(
    [string]$TfRoot = "$PSScriptRoot\..",
    [string[]]$VariableFiles = @()
)

# If no specific files provided, find variables.tf in the root tf directory
if ($VariableFiles.Count -eq 0) {
    $variablesFile = Join-Path $TfRoot "variables.tf"
    if (Test-Path $variablesFile) {
        $VariableFiles = @($variablesFile)
    } else {
        Write-Host "Error: variables.tf not found in $TfRoot" -ForegroundColor Red
        exit 1
    }
}

# Dictionary to store variables grouped by section
$groupedVariables = [System.Collections.Generic.Dictionary[string, [System.Collections.Generic.List[string]]]]::new()

# Set to track all seen variables (for deduplication)
$seenVariables = [System.Collections.Generic.HashSet[string]]::new()

# Regular expression to match variable declarations
$variablePattern = 'variable\s+"([^"]+)"'

# Regular expression to match section headers (comments with text, not just separators)
$sectionPattern = '^#\s+([^#].+)$'

Write-Host "Scanning Terraform variable files..." -ForegroundColor Cyan
Write-Host ""

foreach ($file in $VariableFiles) {
    if (Test-Path $file) {
        Write-Host "  Reading: $file" -ForegroundColor Gray
        
        $lines = Get-Content -Path $file
        $currentSection = "Other"
        
        # Initialize section if it doesn't exist
        if (-not $groupedVariables.ContainsKey($currentSection)) {
            $groupedVariables[$currentSection] = [System.Collections.Generic.List[string]]::new()
        }
        
        foreach ($line in $lines) {
            # Check for section header (comment with text)
            $sectionMatch = $line -match $sectionPattern
            if ($sectionMatch) {
                $sectionName = $matches[1].Trim()
                # Clean up common comment patterns
                $sectionName = $sectionName -replace '^#+\s*', '' -replace '\s*#+$', ''
                
                # Skip separator lines (just # characters)
                if ($sectionName -match '^#+$') {
                    continue
                }
                
                $currentSection = $sectionName
                
                # Initialize section if it doesn't exist
                if (-not $groupedVariables.ContainsKey($currentSection)) {
                    $groupedVariables[$currentSection] = [System.Collections.Generic.List[string]]::new()
                }
            }
            
            # Check for variable declaration
            $varMatch = $line -match $variablePattern
            if ($varMatch) {
                $varName = $matches[1]
                # Only add if we haven't seen this variable before
                if (-not $seenVariables.Contains($varName)) {
                    $seenVariables.Add($varName) | Out-Null
                    $groupedVariables[$currentSection].Add($varName)
                }
            }
        }
    }
}

Write-Host ""
Write-Host "Found variables in $($groupedVariables.Count) groups" -ForegroundColor Green
Write-Host ""
Write-Host ("=" * 60) -ForegroundColor Cyan
Write-Host ""

# Sort sections alphabetically, but put "Other" at the end
$sortedSections = $groupedVariables.Keys | Where-Object { $_ -ne "Other" } | Sort-Object
if ($groupedVariables.ContainsKey("Other")) {
    $sortedSections += "Other"
}

# Generate and print export statements grouped by section
foreach ($section in $sortedSections) {
    $variables = $groupedVariables[$section]
    
    if ($variables.Count -gt 0) {
        # Print section header
        Write-Host "# $section" -ForegroundColor Yellow
        
        # Sort variables within section
        $sortedVars = $variables | Sort-Object
        
        # Generate export statements for this section
        foreach ($varName in $sortedVars) {
            $tfVarName = "TF_VAR_$varName"
            $exportStatement = "export $tfVarName=`"%env.$tfVarName%`""
            Write-Host $exportStatement
        }
        
        # Empty line between sections
        Write-Host ""
    }
}

Write-Host ("=" * 60) -ForegroundColor Cyan
$totalVars = ($groupedVariables.Values | Measure-Object -Property Count -Sum).Sum
Write-Host "Generated $totalVars export statements in $($sortedSections.Count) groups" -ForegroundColor Green
