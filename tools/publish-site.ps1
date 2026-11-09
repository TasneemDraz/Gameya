<#
.SYNOPSIS
  Builds the deployable site for IIS: the API (self-contained, win-x64) with the Angular build in wwwroot.
.EXAMPLE
  pwsh tools/publish-site.ps1 -OutDir $env:TEMP/gameya-site
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$OutDir
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$webDir = Join-Path $repoRoot 'src/gameya-web'
$browserDir = Join-Path $webDir 'dist/gameya-web/browser'
$OutDir = [System.IO.Path]::GetFullPath($OutDir)

Push-Location $repoRoot
try {
    dotnet publish src/Gameya.Api -c Release -r win-x64 --self-contained -o $OutDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    Push-Location $webDir
    try {
        npx ng build --configuration production
        if ($LASTEXITCODE -ne 0) { throw "ng build failed with exit code $LASTEXITCODE" }
    }
    finally {
        Pop-Location
    }

    $wwwroot = Join-Path $OutDir 'wwwroot'
    if (Test-Path $wwwroot) { Remove-Item -Recurse -Force $wwwroot }
    New-Item -ItemType Directory -Force -Path $wwwroot | Out-Null
    Copy-Item -Path (Join-Path $browserDir '*') -Destination $wwwroot -Recurse -Force

    if (-not (Test-Path (Join-Path $wwwroot 'index.html'))) {
        throw "wwwroot/index.html is missing from $OutDir"
    }
    Write-Host "Site published to $OutDir"
}
finally {
    Pop-Location
}
