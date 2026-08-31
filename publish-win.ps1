# Windows publish script for Quantum 2.0
$ErrorActionPreference = "Stop"

$project = Join-Path $PSScriptRoot "quantum/src/Quantum.Host/Quantum.Host.csproj"
$output = Join-Path $PSScriptRoot "artifacts/Windows-x64"

Write-Host "Publishing Quantum.Host for Windows x64..."
dotnet publish $project `
    -c Release `
    -f net10.0-windows10.0.19041.0 `
    -r win-x64 `
    -p:WindowsPackageType=None `
    -o $output

if ($LASTEXITCODE -ne 0) {
    Write-Error "Windows publish failed"
    exit $LASTEXITCODE
}

Write-Host "Publish completed: $output"
