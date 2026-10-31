$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$env:DOTNET_CLI_HOME = "$PSScriptRoot\build\cli"
$env:NUGET_PACKAGES = "$PSScriptRoot\build\packages"
$env:TEMP = $env:TMP = "$PSScriptRoot\build\temp"
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_NOLOGO = '1'
New-Item -ItemType Directory -Force $env:TEMP | Out-Null
dotnet build src/Moments.csproj -c Release --disable-build-servers --configfile NuGet.Config -p:NuGetAudit=false
if ($LASTEXITCODE) {exit $LASTEXITCODE}
Copy-Item src/bin/Release/net10.0-windows/Moments.exe,src/bin/Release/net10.0-windows/Moments.dll,src/bin/Release/net10.0-windows/Moments.deps.json,src/bin/Release/net10.0-windows/Moments.runtimeconfig.json -Destination $PSScriptRoot

