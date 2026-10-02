# Install the desktop app "Zenith Umbra Unity" from the player build (Builds/ZenithUmbraUnity, made by the Pipeline's
# `build` command - see tools/build_app.sh): mirror it into %LOCALAPPDATA%\Programs\ZenithUmbraUnity and add Desktop +
# Start-menu shortcuts. Separate from the web/Electron "Zenith Umbra" app, so the two can be compared side by side.
# usage: powershell -NoProfile -ExecutionPolicy Bypass -File tools\install_app.ps1 [-Launch]
param([switch]$Launch)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$src = Join-Path $root 'Builds\ZenithUmbraUnity'
$exeName = 'Zenith Umbra Unity.exe'
if (-not (Test-Path (Join-Path $src $exeName))) { throw "no build at $src - run the build first" }
$dst = Join-Path $env:LOCALAPPDATA 'Programs\ZenithUmbraUnity'

# a running copy holds its files open
Get-Process -Name 'Zenith Umbra Unity' -ErrorAction SilentlyContinue | Stop-Process -Force -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Force $dst | Out-Null
# mirror (robocopy exit codes 0-7 are success)
robocopy $src $dst /MIR /NFL /NDL /NJH /NJS /NP | Out-Null
if ($LASTEXITCODE -ge 8) { throw "robocopy failed ($LASTEXITCODE)" }

$exe = Join-Path $dst $exeName
$shell = New-Object -ComObject WScript.Shell
$links = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'Zenith Umbra Unity.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'Zenith Umbra Unity.lnk')
)
foreach ($l in $links) {
    $s = $shell.CreateShortcut($l)
    $s.TargetPath = $exe
    $s.WorkingDirectory = $dst
    $s.IconLocation = "$exe,0"
    $s.Description = 'ZENITH//UMBRA - Unity edition'
    $s.Save()
}
$size = [math]::Round(((Get-ChildItem $dst -Recurse | Measure-Object Length -Sum).Sum / 1MB), 0)
Write-Output "installed $exe ($size MB); shortcuts: $($links -join '; ')"
if ($Launch) { Start-Process -FilePath $exe -WorkingDirectory $dst }
