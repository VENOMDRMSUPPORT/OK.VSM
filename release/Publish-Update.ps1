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

function Invoke-Native {
    param(
        [Parameter(Mandatory = $true)]
        [string]$FileName,
        [Parameter(Mandatory = $true)]
        [string[]]$Arguments
    )

    & $FileName @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$FileName failed with exit code $LASTEXITCODE`: $($Arguments -join ' ')"
    }
}

function Get-RepoRoot {
    $root = (& git rev-parse --show-toplevel)
    if ($LASTEXITCODE -ne 0 -or [string]::IsNullOrWhiteSpace($root)) {
        throw "Could not determine git repository root."
    }

    return [string]$root.Trim()
}

function Convert-ToRepoRelativePath {
    param(
        [Parameter(Mandatory = $true)]
        [string]$Path,
        [Parameter(Mandatory = $true)]
        [string]$RepoRoot
    )

    $fullPath = (Resolve-Path -LiteralPath $Path).Path.Replace('/', '\')
    $rootPath = (Resolve-Path -LiteralPath $RepoRoot).Path.Replace('/', '\').TrimEnd('\')
    $rootWithSlash = $rootPath + '\'
    if (-not $fullPath.StartsWith($rootWithSlash, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Path is outside the git repository: $fullPath"
    }

    return $fullPath.Substring($rootWithSlash.Length).Replace('\', '/')
}

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
    param([string]$RepoRoot)

    $status = @(& git -C $RepoRoot status --porcelain)
    if ($LASTEXITCODE -ne 0) {
        throw "Could not inspect git worktree."
    }

    if ($status.Count -gt 0) {
        throw "Git worktree has uncommitted changes. Commit the exact update changes first, then run Publish-Update.ps1 -GitHubRelease."
    }
}

$repoRoot = Get-RepoRoot
$resolvedProjectPath = Resolve-Path $ProjectPath
$resolvedManifestPath = Resolve-Path $ManifestPath
$version = Get-ProjectVersion -CsprojPath $resolvedProjectPath
$versionTag = "v$version"
$manifestRelativePath = Convert-ToRepoRelativePath -Path $resolvedManifestPath -RepoRoot $repoRoot

if ($GitHubRelease) {
    Assert-CleanGitWorktree -RepoRoot $repoRoot
}

& (Join-Path $PSScriptRoot "Publish-Release.ps1") `
    -ProjectPath $ProjectPath `
    -InnoScriptPath $InnoScriptPath `
    -ManifestPath $ManifestPath `
    -OwnerRepo $OwnerRepo

$installerName = "HyperVMManager-Setup-$version.exe"
$installerPath = (Resolve-Path (Join-Path (Split-Path (Resolve-Path $InnoScriptPath) -Parent) $installerName)).Path
$manifestPathResolved = (Resolve-Path $resolvedManifestPath).Path
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

$statusAfterBuild = @(& git -C $repoRoot status --porcelain)
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect git worktree after build."
}

if ($statusAfterBuild.Count -gt 0) {
    $allowed = @(" M $manifestRelativePath", "M  $manifestRelativePath", "MM $manifestRelativePath")
    $unexpected = @($statusAfterBuild | Where-Object { $allowed -notcontains $_ })
    if ($unexpected.Count -gt 0) {
        throw "Build left unexpected git changes:`n$($unexpected -join "`n")"
    }

    Invoke-Native -FileName "git" -Arguments @("-C", $repoRoot, "add", "--", $manifestRelativePath)
    Invoke-Native -FileName "git" -Arguments @("-C", $repoRoot, "commit", "-m", "Update release manifest for $versionTag")
}

$gh = Get-Command gh -ErrorAction SilentlyContinue
if ($null -eq $gh) {
    throw "GitHub CLI (gh) is required to publish update releases."
}

Invoke-Native -FileName "gh" -Arguments @("auth", "status")

$currentBranch = (& git -C $repoRoot branch --show-current)
if ($LASTEXITCODE -ne 0) {
    throw "Could not determine current git branch."
}
$currentBranch = $currentBranch.Trim()
if ([string]::IsNullOrWhiteSpace($currentBranch)) {
    throw "Could not determine current git branch."
}

Write-Host "Pushing branch $currentBranch ..."
Invoke-Native -FileName "git" -Arguments @("-C", $repoRoot, "push", "origin", $currentBranch)

$localTags = @(& git -C $repoRoot tag --list $versionTag)
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect local git tags."
}
$tagExists = $localTags -contains $versionTag

if (-not $tagExists) {
    Invoke-Native -FileName "git" -Arguments @("-C", $repoRoot, "tag", "-a", $versionTag, "-m", "Release $versionTag")
}

$remoteTag = & git -C $repoRoot ls-remote --tags origin $versionTag
if ($LASTEXITCODE -ne 0) {
    throw "Could not inspect remote git tags."
}
if ([string]::IsNullOrWhiteSpace($remoteTag)) {
    Write-Host "Pushing tag $versionTag ..."
    Invoke-Native -FileName "git" -Arguments @("-C", $repoRoot, "push", "origin", $versionTag)
}
else {
    Write-Host "Remote tag $versionTag already exists."
}

$releaseExists = $true
try {
    $oldErrorActionPreference = $ErrorActionPreference
    $ErrorActionPreference = "Continue"
    & gh release view $versionTag --repo $OwnerRepo *> $null
    $releaseViewExitCode = $LASTEXITCODE
}
finally {
    $ErrorActionPreference = $oldErrorActionPreference
}
if ($releaseViewExitCode -ne 0) {
    $releaseExists = $false
}

$notes = "Release $versionTag"
if ($releaseExists) {
    Write-Host "Uploading update assets to existing GitHub release $versionTag ..."
    Invoke-Native -FileName "gh" -Arguments @("release", "upload", $versionTag, $installerPath, $manifestPathResolved, "--repo", $OwnerRepo, "--clobber")
    Invoke-Native -FileName "gh" -Arguments @("release", "edit", $versionTag, "--repo", $OwnerRepo, "--title", $versionTag, "--notes", $notes, "--latest")
}
else {
    Write-Host "Creating GitHub release $versionTag ..."
    $args = @("release", "create", $versionTag, $installerPath, $manifestPathResolved, "--repo", $OwnerRepo, "--title", $versionTag, "--notes", $notes, "--latest")
    if ($Draft) { $args += "--draft" }
    if ($Prerelease) { $args += "--prerelease" }
    Invoke-Native -FileName "gh" -Arguments $args
}

Invoke-Native -FileName "gh" -Arguments @("release", "edit", $versionTag, "--repo", $OwnerRepo, "--title", $versionTag, "--notes", $notes, "--latest")
$latestTag = (& gh api "repos/$OwnerRepo/releases/latest" --jq ".tag_name")
if ($LASTEXITCODE -ne 0) {
    throw "Could not verify the latest GitHub release."
}
if ($latestTag.Trim() -ne $versionTag) {
    throw "GitHub latest release is $($latestTag.Trim()), expected $versionTag."
}

Write-Host ""
Write-Host "Published update $versionTag."
Write-Host "In-app updater URL:"
Write-Host "https://github.com/$OwnerRepo/releases/latest/download/latest.json"
