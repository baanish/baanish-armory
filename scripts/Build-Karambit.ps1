#Requires -Version 7.2
[CmdletBinding()]
param(
    [string]$GameDir = 'D:/SteamLibrary/steamapps/common/Nuclear Option',
    [string]$UnityPath = 'C:/Program Files/Unity/Hub/Editor/2022.3.62f2/Editor/Unity.exe',
    [string]$PythonPath = 'python',
    [switch]$AuthorAssets,
    [switch]$RuntimeOnly,
    [switch]$PassThru
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$workspace = Split-Path -Parent $PSScriptRoot
$project = Join-Path $workspace 'unity'
$output = Join-Path $workspace '.local/karambit-build'
$runtime = Join-Path $output 'runtime'
if (-not (Test-Path -LiteralPath (Join-Path $GameDir 'NuclearOption_Data/Managed/Assembly-CSharp.dll'))) {
    throw 'GameDir must contain the installed Nuclear Option game assemblies.'
}
if (-not $RuntimeOnly -and (-not (Test-Path -LiteralPath $UnityPath) -or
    (((Get-Item -LiteralPath $UnityPath).VersionInfo.ProductVersion -split '_')[0] -ne '2022.3.62f2'))) {
    throw 'The prototype requires Unity 2022.3.62f2.'
}
$lock = Join-Path $project 'Temp/UnityLockfile'
if (-not $RuntimeOnly -and (Test-Path -LiteralPath $lock)) {
    try {
        $probe = [IO.File]::Open($lock, [IO.FileMode]::Open, [IO.FileAccess]::ReadWrite, [IO.FileShare]::None)
        $probe.Dispose()
    } catch [IO.IOException] { throw 'Close this Unity project before building.' }
}
if ($RuntimeOnly) {
    $existingBundle = Join-Path $output 'baanish-karambit-prototype_0.1.0.nobp'
    $expectedBundle = (Get-FileHash -LiteralPath $existingBundle).Hash.ToLowerInvariant() + '  ' + (Split-Path -Leaf $existingBundle)
    if ($expectedBundle -cnotin @(Get-Content -LiteralPath (Join-Path $output 'SHA256SUMS.txt'))) {
        throw 'Runtime-only builds require a previously verified prototype bundle.'
    }
}
$projects = @(Get-ChildItem -LiteralPath (Join-Path $workspace 'src/Karambit') -Filter '*.csproj')
if ($projects.Count -ne 1) { throw 'Expected one Karambit runtime project.' }
& dotnet build $projects[0].FullName -c Release "-p:GameDir=$GameDir" -o $runtime --nologo | Out-Host
if ($LASTEXITCODE -ne 0) { throw 'Karambit runtime compilation failed.' }
if (-not $RuntimeOnly) {
    if ($AuthorAssets) {
        & $PythonPath (Join-Path $workspace 'art/source/karambit/build_karambit.py') --output (Join-Path $workspace '.local/karambit-art') --assets (Join-Path $project 'Assets/Blueprinter/_donotship') | Out-Host
        if ($LASTEXITCODE -ne 0) { throw 'Karambit model generation failed.' }
    }
    $buildMethod = if ($AuthorAssets) { 'AuthorAndBuild' } else { 'Build' }
    $log = Join-Path $output ('unity-' + [DateTime]::UtcNow.ToString('yyyyMMddTHHmmssfffZ') + '.log')
    $arguments = @('-batchmode', '-quit', '-projectPath', ('"' + $project + '"'),
        '-executeMethod', "BaanishArmory.Editor.KarambitAssetBuild.$buildMethod", '-logFile', ('"' + $log + '"'))
    $process = Start-Process -FilePath $UnityPath -ArgumentList $arguments -WindowStyle Hidden -PassThru
    Write-Host "Karambit Unity build PID: $($process.Id)"
    Write-Host "Build log: $log"
    $process.WaitForExit()
    if ($process.ExitCode -ne 0) { throw "Unity exited with code $($process.ExitCode). Inspect $log" }
    $completion = @(Select-String -LiteralPath $log -Pattern '^\[BaanishArmory\] Karambit build complete: (.+)$')
    if ($completion.Count -ne 1) { throw "Unity did not confirm the Karambit build. Inspect $log" }
    $bundle = Join-Path $completion[0].Matches[0].Groups[1].Value 'baanish-karambit-prototype_0.1.0.nobp'
    Copy-Item -LiteralPath $bundle -Destination $output -Force
    foreach ($name in @('baanish-karambit-prototype_0.1.0.source.zip', 'patch_manifest.json', 'asset-validation.txt')) {
        Copy-Item -LiteralPath (Join-Path $completion[0].Matches[0].Groups[1].Value $name) -Destination $output -Force
    }
}
$artifacts = @((Join-Path $output 'baanish-karambit-prototype_0.1.0.nobp')) +
    @(Get-ChildItem -LiteralPath $runtime -Filter '*.dll' | Select-Object -ExpandProperty FullName)
if ($artifacts.Count -ne 2) { throw 'Expected the prototype bundle and one runtime DLL.' }
if (Test-Path -LiteralPath (Join-Path $output 'baanish-karambit-prototype_0.1.0.source.zip')) {
    $artifacts += Join-Path $output 'baanish-karambit-prototype_0.1.0.source.zip'
}
$hashes = foreach ($file in $artifacts) {
    (Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' +
        [IO.Path]::GetRelativePath($output, $file).Replace('\', '/')
}
[IO.File]::WriteAllLines((Join-Path $output 'SHA256SUMS.txt'), $hashes, [Text.UTF8Encoding]::new($false))
Write-Host "Karambit artifacts: $output"
if ($PassThru) { $output }
