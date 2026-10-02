# Build and run the solver behaviour tests with the .NET SDK bundled in the Unity editor. Exit code 0 = every assertion held.
$dotnet = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Data\DotNetSdk\dotnet.exe'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
& $dotnet build "$PSScriptRoot\DynTests.csproj" -c Release -v quiet -nologo /clp:ErrorsOnly
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet "$PSScriptRoot\bin\Release\net8.0\DynTests.dll" @args
exit $LASTEXITCODE
