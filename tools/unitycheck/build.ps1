# Headless compile of the Unity runtime code (see UnityCheck.csproj). Exit code 0 = no errors.
$dotnet = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Data\DotNetSdk\dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
& $dotnet build "$PSScriptRoot\UnityCheck.csproj" -v quiet -nologo /clp:ErrorsOnly
exit $LASTEXITCODE
