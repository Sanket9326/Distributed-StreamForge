param([switch]$Trust)
$ErrorActionPreference = 'Stop'
$repoRoot = [System.IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../..'))
$certDirectory = Join-Path $repoRoot '.certs'
New-Item -ItemType Directory -Path $certDirectory -Force | Out-Null
& dotnet dev-certs https --export-path (Join-Path $certDirectory 'localhost.pem') --format PEM --no-password
if ($LASTEXITCODE -ne 0) { throw 'Could not export the local HTTPS certificate.' }
if ($Trust) {
    & dotnet dev-certs https --trust
    if ($LASTEXITCODE -ne 0) { throw 'Could not trust the local HTTPS certificate.' }
}
Write-Output 'Local HTTPS certificate exported to .certs. Mount this directory read-only; do not commit it.'
