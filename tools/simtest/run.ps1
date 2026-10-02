# Run the headless simulation tool: run.ps1 smoke | parity <scenario.json> | ...
$dotnet = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Data\DotNetSdk\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet "$PSScriptRoot\bin\Release\net8.0\SimTest.dll" @args
exit $LASTEXITCODE
