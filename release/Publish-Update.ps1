param(
    [string]$ProjectPath = "..\HyperVMManager\HyperVMManager.csproj",
    [string]$InnoScriptPath = "..\installer\HyperVMManager.iss",
    [string]$ManifestPath = ".\latest.json",
    [string]$OwnerRepo = "VENOMDRMSUPPORT/OK.VSM",
    [switch]$GitHubRelease,
    [switch]$Draft,
    [switch]$Prerelease
)

$ErrorActionPreference = "Stop"
Set-Location $PSScriptRoot

function Get-ProjectVersion {
    param([string]$CsprojPath)

    [xml]$csproj = Get-Content -Path $CsprojPath
    $versionNode = $csproj.Project.PropertyGroup.Version | Select-Object -First 1
    if ([string]::IsNullOrWhiteSpace($versionNode)) {
        throw "Could not find <Version> in $CsprojPath"
    }

    return [string]$versionNode
}

function Assert-CleanGitWorktree {
    $status = @(git status --porcelain)
    if ($status.Count -gt 0) {
        throw "Git worktree has uncommitted changes. Commit the exact update changes first, then run Publish-Update.ps1 -GitHubRelease."
    }
}

$resolvedProjectPath = Resolve-Path $ProjectPath
$resolvedManifestPath = Resolve-Path $ManifestPath
$version = Get-ProjectVersion -CsprojPath $resolvedProjectPath
$versionTag = "v$version"

& (Join-Path $PSScriptRoot "Publish-Release.ps1") `
    -ProjectPath $ProjectPath `
    -InnoScriptPath $InnoScriptPath `
    -ManifestPath $ManifestPath `
    -OwnerRepo $OwnerRepo

$installerName = "HyperVMManager-Setup-$version.exe"
$installerPath = Resolve-Path (Join-Path (Split-Path (Resolve-Path $InnoScriptPath) -Parent) $installerName)
$manifestPathResolved = Resolve-Path $resolvedManifestPath
$sha256 = (Get-FileHash -LiteralPath $installerPath -Algorithm SHA256).Hash

Write-Host ""
Write-Host "Local update artifacts are ready:"
Write-Host "- Installer: $installerPath"
Write-Host "- Manifest:  $manifestPathResolved"
Write-Host "- SHA256:    $sha256"

if (-not $GitHubRelease) {
    Write-Host ""
    Write-Host "Run with -GitHubRelease after committing changes to publish the in-app update."
    exit 0
}

Assert-CleanGitWorktree

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI (gh) is required to publish update releases."
}

gh auth status | Out-Host

$currentBranch = (git branch --show-current).Trim()
if ([string]::IsNullOrWhiteSpace($currentBranch)) {
    throw "Could not determine current git branch."
}

Write-Host "Pushing branch $currentBranch ..."
git push origin $currentBranch

$tagExists = $false
try {
    git rev-parse -q --verify "refs/tags/$versionTag" | Out-Null
    $tagExists = $true
}
catch {
    $tagExists = $false
}

if (-not $tagExists) {
    git tag -a $versionTag -m "Release $versionTag"
}

Write-Host "Pushing tag $versionTag ..."
git push origin $versionTag

$releaseExists = $true
try {
    gh release view $versionTag --repo $OwnerRepo | Out-Null
}
catch {
    $releaseExists = $false
}

$notes = "Release $versionTag"
if ($releaseExists) {
    Write-Host "Uploading update assets to existing GitHub release $versionTag ..."
    gh release upload $versionTag $installerPath $manifestPathResolved --repo $OwnerRepo --clobber
    gh release edit $versionTag --repo $OwnerRepo --title $versionTag --notes $notes --latest
}
else {
    Write-Host "Creating GitHub release $versionTag ..."
    $args = @("release", "create", $versionTag, "$installerPath", "$manifestPathResolved", "--repo", $OwnerRepo, "--title", $versionTag, "--notes", $notes, "--latest")
    if ($Draft) { $args += "--draft" }
    if ($Prerelease) { $args += "--prerelease" }
    & gh @args
}

Write-Host ""
Write-Host "Published update $versionTag."
Write-Host "In-app updater URL:"
Write-Host "https://github.com/$OwnerRepo/releases/latest/download/latest.json"
