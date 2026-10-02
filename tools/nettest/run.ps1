# Run the netcode tests: run.ps1 unit | node [http://localhost:8788/api/net] | golden <ts-vectors.json>
$dotnet = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Data\DotNetSdk\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
& $dotnet "$PSScriptRoot\bin\Release\net8.0\NetTest.dll" @args
exit $LASTEXITCODE
