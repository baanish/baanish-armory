#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$UnityPath,
    [string]$GameDir = 'D:/SteamLibrary/steamapps/common/Nuclear Option',
    [string]$NotesFile,
    [switch]$Publish
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspacePath = Split-Path -Parent $PSScriptRoot
$gitWorkspace = $workspacePath.Replace('\', '/')

function Invoke-ReleaseGit([string[]]$Arguments) {
    $result = & git -c "safe.directory=$gitWorkspace" -C $workspacePath @Arguments
    if ($LASTEXITCODE -ne 0) { throw "Git failed: $($Arguments[0])" }
    $result
}

function Invoke-ReleaseGh([string[]]$Arguments) {
    $result = & gh @Arguments
    if ($LASTEXITCODE -ne 0) { throw "GitHub CLI failed: $($Arguments[0])" }
    $result
}

Push-Location $workspacePath
try {
    if (Invoke-ReleaseGit -Arguments @('status', '--porcelain')) { throw 'Commit or remove public working-tree changes before preparing a release.' }
    & (Join-Path $PSScriptRoot 'Test-Source.ps1')
    $commit = Invoke-ReleaseGit -Arguments @('rev-parse', 'HEAD')
    $modInfo = Get-Content -LiteralPath (Join-Path $workspacePath 'unity/Assets/Blueprinter/Mods/baanish-armory/modinfo.json') -Raw | ConvertFrom-Json
    $tag = 'v' + $modInfo.version

    if ($Publish) {
        if (-not $NotesFile -or -not (Test-Path -LiteralPath $NotesFile -PathType Leaf)) { throw 'Pass -NotesFile with the release notes to upload.' }
        $notes = (Resolve-Path -LiteralPath $NotesFile).Path
        if ([string]::IsNullOrWhiteSpace((Get-Content -LiteralPath $notes -Raw))) { throw 'Release notes are empty.' }
        $repository = Invoke-ReleaseGh -Arguments @('repo', 'view', '--json', 'nameWithOwner,defaultBranchRef') | ConvertFrom-Json
        $repoName = $repository.nameWithOwner
        $branch = [Uri]::EscapeDataString($repository.defaultBranchRef.name)
        $remoteCommit = Invoke-ReleaseGh -Arguments @('api', "repos/$repoName/commits/$branch", '--jq', '.sha')
        if ($remoteCommit -ne $commit) { throw 'The local commit must match the GitHub default branch before publishing.' }
        $refs = @(Invoke-ReleaseGh -Arguments @('api', "repos/$repoName/git/matching-refs/tags/$tag") | ConvertFrom-Json)
        if ($refs | Where-Object { $_.ref -eq "refs/tags/$tag" }) { throw "$tag already exists. Bump and validate the mod version instead of replacing a release." }
    }

    $buildDirectory = & (Join-Path $PSScriptRoot 'Build-Prototype.ps1') -UnityPath $UnityPath -PassThru
    if (-not $buildDirectory -or $buildDirectory -is [array]) { throw 'Build did not return exactly one output directory.' }
    $karambitArgs = @{ GameDir = $GameDir; PassThru = $true }
    if ($UnityPath) { $karambitArgs.UnityPath = $UnityPath }
    $karambitBuild = & (Join-Path $PSScriptRoot 'Build-Karambit.ps1') @karambitArgs
    if (-not $karambitBuild -or $karambitBuild -is [array]) { throw 'Karambit build did not return exactly one output directory.' }
    $releaseDirectory = & (Join-Path $PSScriptRoot 'Prepare-Release.ps1') -BuildDirectory $buildDirectory -KarambitBuildDirectory $karambitBuild -PassThru
    if (-not $releaseDirectory -or $releaseDirectory -is [array]) { throw 'Packaging did not return exactly one output directory.' }
    if ((Invoke-ReleaseGit -Arguments @('status', '--porcelain')) -or (Invoke-ReleaseGit -Arguments @('rev-parse', 'HEAD')) -ne $commit) {
        throw 'Public source changed while building. Commit the changes and rebuild before publishing.'
    }
    if (-not $Publish) {
        Write-Host "Release prepared locally: $releaseDirectory"
        Write-Host 'Nothing uploaded. Use -Publish -NotesFile <path> to build and upload a new version.'
        return
    }

    $remoteCommit = Invoke-ReleaseGh -Arguments @('api', "repos/$repoName/commits/$branch", '--jq', '.sha')
    if ($remoteCommit -ne $commit) { throw 'The GitHub default branch changed while building. Nothing uploaded.' }
    $manualName = "baanish-armory_$($modInfo.version)-manual-install.zip"
    $assets = @((Get-Item -LiteralPath (Join-Path $releaseDirectory $manualName))) +
        @(Get-ChildItem -LiteralPath $releaseDirectory -File | Where-Object Name -NE $manualName | Sort-Object Name)
    if ($assets.Count -ne 10) { throw 'Expected exactly ten verified release files.' }
    # Creating the ref fails atomically if another release claimed this version during the build.
    $createdTag = Invoke-ReleaseGh -Arguments @('api', '--method', 'POST', "repos/$repoName/git/refs",
        '-f', "ref=refs/tags/$tag", '-f', "sha=$commit") | ConvertFrom-Json
    if ($createdTag.object.sha -ne $commit) { throw 'GitHub did not create the expected release tag.' }
    $createArgs = @('release', 'create', $tag, '--repo', $repoName, '--target', $commit,
        '--verify-tag', '--title', "baanish-armory $($modInfo.version)", '--notes-file', $notes, '--prerelease', '--draft') + $assets.FullName
    Invoke-ReleaseGh -Arguments $createArgs | Out-Host
    # Keep incomplete uploads in a draft; expose the release only after hash verification.
    # The by-tag endpoint does not return drafts, so find the draft in the release list.
    $drafts = @(Invoke-ReleaseGh -Arguments @('api', "repos/$repoName/releases?per_page=100") | ConvertFrom-Json |
        Where-Object { $_.tag_name -eq $tag -and $_.draft })
    if ($drafts.Count -ne 1) { throw "Expected one uploaded draft for $tag. Inspect the releases before retrying." }
    $uploaded = $drafts[0]
    if ($uploaded.target_commitish -ne $commit -or $uploaded.assets.Count -ne $assets.Count) {
        throw 'Uploaded draft does not match the local release. Inspect it before retrying.'
    }
    foreach ($file in $assets) {
        $matches = @($uploaded.assets | Where-Object { $_.name -eq $file.Name })
        $digest = 'sha256:' + (Get-FileHash -LiteralPath $file.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
        if ($matches.Count -ne 1 -or $matches[0].digest -ne $digest) { throw "Uploaded hash mismatch for $($file.Name). The release remains a draft." }
    }
    $tagCommit = Invoke-ReleaseGh -Arguments @('api', "repos/$repoName/git/ref/tags/$tag", '--jq', '.object.sha')
    if ($tagCommit -ne $commit) { throw 'The release tag changed. The release remains a draft.' }
    Invoke-ReleaseGh -Arguments @('release', 'edit', $tag, '--repo', $repoName, '--draft=false') | Out-Host
    $published = Invoke-ReleaseGh -Arguments @('api', "repos/$repoName/releases/tags/$tag") | ConvertFrom-Json
    if ($published.draft -or -not $published.prerelease) { throw 'GitHub did not confirm the published prerelease.' }
    Write-Host "Published prerelease: $($published.html_url)"
} finally {
    Pop-Location
}
