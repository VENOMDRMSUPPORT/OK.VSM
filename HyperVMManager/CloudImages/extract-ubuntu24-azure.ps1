# Extracts ubuntu-24.04-server-cloudimg-amd64-azure.vhd from the official .tar.gz
# into this folder as ubuntu-24.04-server-cloudimg-amd64-azure.vhd
# Usage:
#   .\extract-ubuntu24-azure.ps1
#   .\extract-ubuntu24-azure.ps1 -ArchivePath 'D:\Downloads\ubuntu-24.04-server-cloudimg-amd64-azure.vhd.tar.gz'

param(
    [string]$ArchivePath = ""
)

$ErrorActionPreference = "Stop"
$destDir = $PSScriptRoot
$finalName = "ubuntu-24.04-server-cloudimg-amd64-azure.vhd"

if (-not $ArchivePath) {
    $candidates = @(
        (Join-Path $destDir "ubuntu-24.04-server-cloudimg-amd64-azure.vhd.tar.gz")
        (Join-Path (Get-Location) "ubuntu-24.04-server-cloudimg-amd64-azure.vhd.tar.gz")
    )
    foreach ($c in $candidates) {
        if (Test-Path -LiteralPath $c) {
            $ArchivePath = $c
            break
        }
    }
}

if (-not $ArchivePath -or -not (Test-Path -LiteralPath $ArchivePath)) {
    $msg = "Could not find ubuntu-24.04-server-cloudimg-amd64-azure.vhd.tar.gz. Copy it next to this script ($destDir), or run: .\extract-ubuntu24-azure.ps1 -ArchivePath 'FULL_PATH'"
    Write-Error $msg
    exit 1
}

$tmp = Join-Path $env:TEMP "extract-ubuntu24-azure-$([guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $tmp -Force | Out-Null
try {
    Write-Host "Extracting (may take a few minutes; tar lists the file name as it writes)..." -ForegroundColor Cyan
    Write-Host "Archive: $ArchivePath"
    # -v shows each member as it is extracted (single large .vhd = one line of feedback)
    & tar -xvzf $ArchivePath -C $tmp
    if ($LASTEXITCODE -ne 0) { throw "tar failed with exit code $LASTEXITCODE" }
    $vhd = Get-ChildItem -Path $tmp -Filter "*.vhd" -Recurse -File | Select-Object -First 1
    if (-not $vhd) { throw "No .vhd file found inside the archive." }
    $out = Join-Path $destDir $finalName
    Write-Host "Moving to $out ..." -ForegroundColor Cyan
    Move-Item -LiteralPath $vhd.FullName -Destination $out -Force
    Write-Host "Done: $out" -ForegroundColor Green
}
finally {
    Remove-Item -LiteralPath $tmp -Recurse -Force -ErrorAction SilentlyContinue
}
