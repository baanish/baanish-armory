#Requires -Version 7.2
[CmdletBinding()]
param()

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspacePath = Split-Path -Parent $PSScriptRoot
$gitWorkspace = $workspacePath.Replace('\', '/')
$sourceFiles = @(git -c "safe.directory=$gitWorkspace" -C $workspacePath ls-files --cached --others --exclude-standard | Sort-Object -Unique)
if ($LASTEXITCODE -ne 0 -or $sourceFiles.Count -eq 0) { throw 'Could not enumerate public source files.' }
$reviewedFiles = @(Get-Content -LiteralPath (Join-Path $workspacePath 'config/release-source-files.txt'))
if ((Compare-Object $reviewedFiles $sourceFiles -CaseSensitive) -or $reviewedFiles.Count -ne $sourceFiles.Count) {
    throw 'The public source file list changed. Review config/release-source-files.txt.'
}
foreach ($name in $sourceFiles) {
    if ($name -match '(^|/)(\.local|\.git|_donotship|Library|Temp|UserSettings|Logs|BlueprinterCache|Generated|nuclearoption|TextMesh Pro|155mmrailgun|Example)(/|$)' -or
        $name -match '(?i)\.(dll|pdb|nobp|zip|blend\d*)$|\.prototype-receipt\.json$' -or
        $name -match '(^|/)\.\.?(/|$)|\\|:|^/|//|/$' -or
        $name -notmatch '^(\.gitattributes|\.gitignore|\.github/workflows/ci\.yml|README\.md|LICENSE|CONTRIBUTING\.md|THIRD-PARTY-NOTICES\.md|docs/[^/]+\.md|config/(prototype-baseline-0\.1\.5\.json|game-asset-references\.json|release-source-files\.txt)|scripts/(Build-Prototype|Prepare-Release|Publish-Release|Test-Source)\.ps1|art/references/eyeball-xl-lineart-clean-reference\.png|unity/.+)$') {
        throw "Unexpected public source file: $name"
    }
    $path = Join-Path $workspacePath $name
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing public source file: $name" }
    for ($current = $path; $current; $current = Split-Path -Parent $current) {
        if ((Get-Item -LiteralPath $current -Force).Attributes -band [IO.FileAttributes]::ReparsePoint) {
            throw "Redirected source input: $current"
        }
    }
    if ($name.EndsWith('.ps1', [StringComparison]::OrdinalIgnoreCase)) {
        $parseErrors = $null
        [void][Management.Automation.Language.Parser]::ParseFile($path, [ref]$null, [ref]$parseErrors)
        if ($parseErrors.Count) { throw "PowerShell syntax error in ${name}: $($parseErrors.Message -join '; ')" }
    }
}
foreach ($required in @('LICENSE', 'README.md', 'THIRD-PARTY-NOTICES.md', 'docs/INSTALL.md', 'docs/BUILD.md', 'unity/LICENSE', 'config/prototype-baseline-0.1.5.json', '.github/workflows/ci.yml', 'scripts/Test-Source.ps1')) {
    if ($required -cnotin $sourceFiles) { throw "Required public source is excluded: $required" }
}

$baseline = Get-Content -LiteralPath (Join-Path $workspacePath 'config/prototype-baseline-0.1.5.json') -Raw | ConvertFrom-Json
if ($baseline.schemaVersion -ne 1 -or $baseline.modVersion -cne '0.1.5' -or
    $baseline.unityVersion -cne '2022.3.62f2' -or $baseline.hashPolicy -cne 'png-bytes;other-files-utf8-lf-no-bom' -or
    @($baseline.files).Count -ne 19) {
    throw 'Expected the reviewed 0.1.5 baseline schema, versions, hash policy and 19 authored files.'
}
$modFolder = 'unity/Assets/Blueprinter/Mods/baanish-armory'
$expected = [Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
foreach ($file in $baseline.files) {
    if ($file.path -isnot [string] -or $file.path -notmatch ('^' + [regex]::Escape($modFolder) + '(\.meta|/[^/]+)$') -or
        $file.path -match '(^|/)\.\.?(/|$)|\\|:|\.prototype-receipt\.json$' -or
        $file.path -cnotin $sourceFiles -or -not $expected.Add($file.path) -or $file.sha256 -cnotmatch '^[a-f0-9]{64}$') {
        throw 'Invalid, repeated or out-of-scope baseline path or SHA256.'
    }
    $path = Join-Path $workspacePath $file.path
    # Match PrototypeBuild.HashBaselineFile: PNG bytes, otherwise UTF-8 without BOM and CRLF converted to LF.
    $bytes = if ($path.EndsWith('.png', [StringComparison]::OrdinalIgnoreCase)) {
        [IO.File]::ReadAllBytes($path)
    } else {
        [Text.Encoding]::UTF8.GetBytes([IO.File]::ReadAllText($path).Replace("`r`n", "`n"))
    }
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([byte[]]$bytes)).ToLowerInvariant()
    if ($hash -cne $file.sha256) { throw "Authored source differs from the checked-in baseline: $($file.path)" }
}
$modPath = Join-Path $workspacePath $modFolder
$actual = @($modFolder + '.meta') + @(Get-ChildItem -LiteralPath $modPath -File -Force | Where-Object Name -NE '.prototype-receipt.json' | ForEach-Object { "$modFolder/$($_.Name)" })
if (@(Get-ChildItem -LiteralPath $modPath -Directory -Force).Count -or -not $expected.SetEquals([string[]]$actual)) {
    throw 'Unexpected authored files or missing asset metas; review the source baseline.'
}
$modInfo = Get-Content -LiteralPath (Join-Path $modPath 'modinfo.json') -Raw | ConvertFrom-Json
$editorVersion = @(Select-String -LiteralPath (Join-Path $workspacePath 'unity/ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (\S+)$')
if ($modInfo.version -cne $baseline.modVersion -or $editorVersion.Count -ne 1 -or
    $editorVersion[0].Matches[0].Groups[1].Value -cne $baseline.unityVersion) {
    throw 'Mod or Unity project version differs from the reviewed baseline.'
}
"Verified $($sourceFiles.Count) public source files and $($expected.Count) baseline hashes."
