$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot
$qa = Join-Path $root 'build/features'
New-Item -ItemType Directory -Force "$qa/tools", "$root/build/temp" | Out-Null
foreach ($name in 'ffmpeg.exe','ffprobe.exe') {
    if (!(Test-Path "$qa/tools/$name")) { New-Item -ItemType HardLink -Path "$qa/tools/$name" -Target "$root/tools/$name" | Out-Null }
}
dotnet build "$root/tests/montage/MontageTests.csproj" -c Release --configfile "$root/NuGet.Config" -p:NuGetAudit=false -o $qa
if ($LASTEXITCODE) { throw 'Feature test build failed' }
& "$qa/Moments.MontageTests.exe" "$root/build/features-verification.txt" features
if ($LASTEXITCODE) { throw 'Feature UI tests failed' }

& "$qa/Moments.MontageTests.exe" "$root/build/av-sync-verification.txt" sync
if ($LASTEXITCODE) { throw 'A/V synchronization tests failed' }
