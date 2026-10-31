$ErrorActionPreference='Stop'
$root=Split-Path (Split-Path $PSScriptRoot);$qa=Join-Path $root ('build\app_audio_'+[Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path "$qa\tools" -Force|Out-Null
foreach($name in 'ffmpeg.exe','ffprobe.exe'){New-Item -ItemType HardLink -Path "$qa\tools\$name" -Target "$root\tools\$name"|Out-Null}
$env:TMP=$env:TEMP="$root\build\temp";$env:DOTNET_CLI_HOME="$root\build\cli";$env:NUGET_PACKAGES="$root\build\packages";$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
dotnet build "$root\tests\montage\MontageTests.csproj" -c Release --disable-build-servers --configfile "$root\NuGet.Config" -p:NuGetAudit=false -o $qa
if($LASTEXITCODE){exit $LASTEXITCODE}
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$qa\ToneGame.exe" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$PSScriptRoot\ToneFixture.cs"
if($LASTEXITCODE){throw 'Tone fixture build failed'}
Copy-Item -LiteralPath "$qa\ToneGame.exe" -Destination "$qa\ToneChat.exe"
Copy-Item -LiteralPath "$qa\ToneGame.exe" -Destination "$qa\ToneLauncher.exe"
$launcher=Start-Process -FilePath "$qa\ToneLauncher.exe" -WindowStyle Hidden -PassThru
$p=Start-Process -FilePath "$qa\ToneGame.exe" -WindowStyle Hidden -PassThru
try{
    & "$qa\Moments.MontageTests.exe" "$root\build\app-audio-verification.txt" apps
    if($LASTEXITCODE){throw "Audio test failed in $qa"}
}finally{
    [IO.File]::WriteAllText("$qa\quit.test",'');if(!$p.HasExited -and !$p.WaitForExit(4000)){$p.Kill()}
    if(!$launcher.HasExited -and !$launcher.WaitForExit(4000)){$launcher.Kill()}
}
$absolute=(Resolve-Path -LiteralPath $qa).Path
if([IO.Path]::GetDirectoryName($absolute) -eq "$root\build" -and [IO.Path]::GetFileName($absolute) -match '^app_audio_[0-9a-f]{32}$'){Remove-Item -LiteralPath $absolute -Recurse -Force}
