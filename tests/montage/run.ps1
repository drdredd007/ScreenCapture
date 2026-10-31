param([switch]$ColorsOnly)
$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot)
$qa=Join-Path $root ('build\montage_'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$qa\tools" -Force | Out-Null
foreach($name in 'ffmpeg.exe','ffprobe.exe'){New-Item -ItemType HardLink -Path "$qa\tools\$name" -Target "$root\tools\$name" | Out-Null}
$env:TMP=$env:TEMP="$root\build\temp";$env:DOTNET_CLI_HOME="$root\build\cli";$env:NUGET_PACKAGES="$root\build\packages";$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet build "$PSScriptRoot\MontageTests.csproj" -c Release --disable-build-servers --configfile "$root\NuGet.Config" -p:NuGetAudit=false -o $qa
if($LASTEXITCODE){exit $LASTEXITCODE}
$mode=if($ColorsOnly){'colors'}else{'montage'}
& "$qa\Moments.MontageTests.exe" "$root\build\$mode-verification.txt" $mode
if($LASTEXITCODE){throw "Montage check failed. See $qa"}
$absolute=(Resolve-Path -LiteralPath $qa).Path
if([IO.Path]::GetDirectoryName($absolute) -eq "$root\build" -and [IO.Path]::GetFileName($absolute) -match '^montage_[a-f0-9]{32}$'){Remove-Item -LiteralPath $absolute -Recurse -Force}
