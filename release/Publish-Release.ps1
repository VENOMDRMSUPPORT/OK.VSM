param(
    [string]$ProjectPath = "..\HyperVMManager\HyperVMManager.csproj",
    [string]$InnoScriptPath = "..\installer\HyperVMManager.iss",
    [string]$ManifestPath = ".\latest.json",
    [string]$OwnerRepo = "VENOMDRMSUPPORT/OK.VSM"
)

$ErrorActionPreference = "Stop"

function Get-ProjectVersion {
    param([string]$CsprojPath)

    [xml]$csproj = Get-Content -Path $CsprojPath
    $versionNode = $csproj.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($versionNode)) {
        throw "Could not find <Version> in $CsprojPath"
    }

    return $versionNode
}

Set-Location $PSScriptRoot

$resolvedProjectPath = Resolve-Path $ProjectPath
$resolvedInnoScriptPath = Resolve-Path $InnoScriptPath
$resolvedManifestPath = Resolve-Path $ManifestPath

$version = Get-ProjectVersion -CsprojPath $resolvedProjectPath
$versionTag = "v$version"

Write-Host "Building Release for version $version ..."
dotnet build $resolvedProjectPath -c Release

$iscc = Get-Command iscc -ErrorAction SilentlyContinue
if ($null -eq $iscc) {
    $isccCandidates = @(
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles(x86)\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
    )

    $isccPath = $isccCandidates | Where-Object { Test-Path $_ } | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($isccPath)) {
        throw "Inno Setup compiler (iscc.exe) not found. Install Inno Setup 6 first."
    }
}
else {
    $isccPath = $iscc.Source
}

Write-Host "Compiling installer with Inno Setup ..."
& $isccPath $resolvedInnoScriptPath

$installerName = "HyperVMManager-Setup-$version.exe"
$installerPath = Join-Path (Split-Path $resolvedInnoScriptPath -Parent) $installerName
if (-not (Test-Path $installerPath)) {
    throw "Installer not found at $installerPath"
}

$sha256 = (Get-FileHash -Path $installerPath -Algorithm SHA256).Hash
$downloadUrl = "https://github.com/$OwnerRepo/releases/download/$versionTag/$installerName"

$manifestObject = [ordered]@{
    version = $version
    downloadUrl = $downloadUrl
    sha256 = $sha256
    releaseNotes = "Release $versionTag"
}

$manifestObject | ConvertTo-Json -Depth 3 | Set-Content -Path $resolvedManifestPath -Encoding UTF8

Write-Host "Release artifacts ready:"
Write-Host "- Installer: $installerPath"
Write-Host "- Manifest: $resolvedManifestPath"
Write-Host "- SHA256: $sha256"
Write-Host "Next: create git tag $versionTag, push commits/tags, upload installer + latest.json to GitHub Release."
