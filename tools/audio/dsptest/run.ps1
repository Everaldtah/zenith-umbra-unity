# Build + run the master-chain tests (Program.cs). Exit code 0 = every case passed.
#   powershell -NoProfile -File tools/audio/dsptest/run.ps1                 # the test cases
#   powershell -NoProfile -File tools/audio/dsptest/run.ps1 render in.wav out.wav [night]
$ErrorActionPreference = 'Stop'
$dotnet = 'C:\Program Files\Unity\Hub\Editor\6000.6.4f1\Editor\Data\DotNetSdk\dotnet.exe'
if (-not (Test-Path $dotnet)) { $dotnet = 'dotnet' }
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'; $env:DOTNET_NOLOGO = '1'
& $dotnet build "$PSScriptRoot\DspTest.csproj" -c Release -v quiet -nologo /clp:ErrorsOnly -p:UseSharedCompilation=false
if ($LASTEXITCODE -ne 0) { exit $LASTEXITCODE }
& $dotnet "$PSScriptRoot\bin\Release\net8.0\DspTest.dll" @args
exit $LASTEXITCODE
