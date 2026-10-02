# Compile the netcode headless (no Unity editor needed). Exit code 0 = no errors.
$ErrorActionPreference = 'Stop'
$dotnet = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Data\DotNetSdk\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
& $dotnet build "$PSScriptRoot\NetTest.csproj" -c Release -v quiet -nologo /clp:ErrorsOnly
exit $LASTEXITCODE
