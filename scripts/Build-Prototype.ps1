#Requires -Version 7.2
[CmdletBinding()]
param([string]$UnityPath, [switch]$PassThru)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspacePath = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $workspacePath 'unity'
$versionLines = @(Select-String -LiteralPath (Join-Path $projectPath 'ProjectSettings/ProjectVersion.txt') -Pattern '^m_EditorVersion: (\S+)$')
if ($versionLines.Count -ne 1) { throw 'Expected one editor version in unity/ProjectSettings/ProjectVersion.txt.' }
$requiredVersion = $versionLines[0].Matches[0].Groups[1].Value
$candidates = if ($UnityPath) {
    [IO.Path]::GetFullPath($UnityPath, $PWD.Path)
} else {
    foreach ($registryRoot in @('HKLM:/SOFTWARE', 'HKLM:/SOFTWARE/WOW6432Node', 'HKCU:/SOFTWARE')) {
        $key = "$registryRoot/Microsoft/Windows/CurrentVersion/Uninstall/Unity $requiredVersion"
        $iconPath = Get-ItemPropertyValue -LiteralPath $key -Name DisplayIcon -ErrorAction SilentlyContinue
        if ($iconPath) { ($iconPath -replace ',\s*-?\d+\s*$', '').Trim().Trim('"') }
    }
    if ($env:ProgramFiles) { Join-Path $env:ProgramFiles "Unity/Hub/Editor/$requiredVersion/Editor/Unity.exe" }
    Get-Command Unity.exe -CommandType Application -All -ErrorAction SilentlyContinue | Select-Object -ExpandProperty Source
}
$editorPath = $candidates | Where-Object {
    (Test-Path -LiteralPath $_ -PathType Leaf) -and
    (((Get-Item -LiteralPath $_).VersionInfo.ProductVersion -split '_')[0] -eq $requiredVersion)
} | Select-Object -First 1
if (-not $editorPath) { throw "Could not find Unity $requiredVersion. Pass its executable with -UnityPath." }
$lockPath = Join-Path $projectPath 'Temp/UnityLockfile'
if (Test-Path -LiteralPath $lockPath) {
    # A failed batch leaves the file behind; only a held lock indicates an open editor.
    try {
        $lockProbe = [IO.File]::Open($lockPath, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $lockProbe.Dispose()
    } catch [IO.IOException] {
        throw 'Close this Unity project normally before starting its batch build.'
    }
}
if (-not (Test-Path -LiteralPath (Join-Path $projectPath 'Assets/Blueprinter/Mods/baanish-armory/modinfo.json'))) {
    throw 'The authored baanish-armory source is missing.'
}
$logFolder = Join-Path $workspacePath '.local/evidence'
New-Item -ItemType Directory -Path $logFolder -Force | Out-Null
$logPath = Join-Path $logFolder ('unity-prototype-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '.log')
$entryPoint = 'BaanishArmory.Editor.PrototypeBuild.Build'
$arguments = @('-batchmode', '-quit', '-projectPath', ('"' + $projectPath + '"'),
    '-executeMethod', $entryPoint, '-logFile', ('"' + $logPath + '"'))
$buildProcess = Start-Process -FilePath $editorPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
Write-Host "Unity build PID: $($buildProcess.Id)"
Write-Host "Build log: $logPath"
$buildProcess.WaitForExit()
if ($buildProcess.ExitCode -ne 0) { throw "Unity exited with code $($buildProcess.ExitCode). Inspect $logPath" }
$completion = @(Select-String -LiteralPath $logPath -Pattern '^\[BaanishArmory\] Build and source export complete: (.+)$')
if ($completion.Count -ne 1) { throw "Unity did not record a completed prototype build. Inspect $logPath" }
$buildDirectory = $completion[0].Matches[0].Groups[1].Value
Write-Host "Verified build completion: $buildDirectory"
if ($PassThru) { $buildDirectory }
