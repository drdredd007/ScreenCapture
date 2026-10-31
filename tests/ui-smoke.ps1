# Disposable acceptance run. The fixture and its recorder have a separate library.
$ErrorActionPreference = 'Stop'
$project = Split-Path $PSScriptRoot
$run = Join-Path $project ('build\ui_' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $run,(Join-Path $run 'tools') | Out-Null
$env:TMP = $env:TEMP = $run
$report = Join-Path $project 'build\ui-verification.txt'
[IO.File]::WriteAllText($report,'')
function Note($line){[IO.File]::AppendAllText($report,"$line"+[Environment]::NewLine)}
foreach($name in @('Moments.exe','Moments.dll','Moments.deps.json','Moments.runtimeconfig.json')){
    Copy-Item -LiteralPath (Join-Path $project $name) -Destination $run
}
foreach($name in @('ffmpeg.exe','ffprobe.exe')){
    New-Item -ItemType HardLink -Path (Join-Path "$run\tools" $name) -Target (Join-Path "$project\tools" $name) | Out-Null
}
# Isolate this test's Windows single-instance mutex and activation event from a live studio.
# Only the two object names differ from the release source.
$qaSource=Join-Path $run 'src'
New-Item -ItemType Directory -Path $qaSource | Out-Null
Get-ChildItem -LiteralPath "$project\src" -File | Copy-Item -Destination $qaSource
$qualifier=[IO.Path]::GetFileName($run)
foreach($unit in @('App.xaml.cs','MainWindow.xaml.cs')){
    $unitPath=Join-Path $qaSource $unit
    $code=[IO.File]::ReadAllText($unitPath).Replace('Local\MomentsStudio_FMoments',"Local\MomentsStudio_$qualifier").Replace('Local\MomentsStudio_Show',"Local\MomentsShow_$qualifier")
    [IO.File]::WriteAllText($unitPath,$code)
}
$env:DOTNET_CLI_HOME="$project\build\cli"
$env:NUGET_PACKAGES="$project\build\packages"
$env:DOTNET_CLI_TELEMETRY_OPTOUT='1'
& dotnet build "$qaSource\Moments.csproj" -c Release --disable-build-servers --configfile "$project\NuGet.Config" -p:NuGetAudit=false -o "$run\compiled"
if($LASTEXITCODE){throw 'isolated build'}
foreach($name in @('Moments.exe','Moments.dll','Moments.deps.json','Moments.runtimeconfig.json')){
    Copy-Item -LiteralPath "$run\compiled\$name" -Destination $run -Force
}
$fixture = Join-Path $run 'MomentFixture.exe'
$source = @'
using System;
using System.Drawing;
using System.Windows.Forms;
class Fixture : Form {
    [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ShowWindow(IntPtr window,int command);
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct RawDevice {public ushort page,usage;public uint flags;public IntPtr window;}
    [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool RegisterRawInputDevices(ref RawDevice device,uint count,uint size);
    protected override void WndProc(ref Message m){
        if(m.Msg==0x800B){
            var raw=new RawDevice{page=1,usage=6,flags=0x230,window=Handle};
            if(!RegisterRawInputDevices(ref raw,1,(uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(RawDevice))))throw new Exception("Raw fixture registration");
            FormBorderStyle=FormBorderStyle.None;Bounds=Screen.FromHandle(Handle).Bounds;Activate();
        }
        base.WndProc(ref m);
    }
    protected override void OnLoad(EventArgs e){base.OnLoad(e);BeginInvoke(new Action(delegate{ShowWindow(Handle,4);}));}
    Fixture(){Text="Moments QA Game";ClientSize=new Size(960,540);DoubleBuffered=true;}
    protected override void OnPaint(PaintEventArgs e){
        var g=e.Graphics;g.SmoothingMode=System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
        using(var sky=new System.Drawing.Drawing2D.LinearGradientBrush(ClientRectangle,Color.FromArgb(24,31,63),Color.FromArgb(202,126,102),90f))g.FillRectangle(sky,ClientRectangle);
        var random=new Random(18);for(int i=0;i<90;i++){int x=random.Next(960),y=random.Next(180);g.FillEllipse(Brushes.LightSteelBlue,x,y,1.3f,1.3f);}
        using(var sun=new SolidBrush(Color.FromArgb(248,194,143)))g.FillEllipse(sun,600,76,90,90);
        for(int layer=0;layer<3;layer++){var p=new Point[51];p[0]=new Point(-20,540);p[50]=new Point(980,540);
            for(int i=1;i<50;i++){int x=(i-1)*20;double y=199+layer*33+Math.Sin(x*.011+layer)*27+Math.Sin(x*.032+layer)*9; p[i]=new Point(x,(int)y-random.Next(13));}
            using(var brush=new SolidBrush(new[]{Color.FromArgb(91,86,111),Color.FromArgb(63,66,88),Color.FromArgb(36,45,61)}[layer]))g.FillPolygon(brush,p);}
        Point[] road={new Point(500,254),new Point(505,254),new Point(950,540),new Point(36,540)};
        using(var pavement=new SolidBrush(Color.FromArgb(28,33,48)))g.FillPolygon(pavement,road);
        using(var rail=new Pen(Color.FromArgb(75,182,195),3)){g.DrawLine(rail,500,254,36,540);g.DrawLine(rail,505,254,950,540);}
        for(int lane=1;lane<7;lane++){double t=lane*lane/49.0;int y=(int)(254+286*t);int dx=(int)(t*455);using(var stripe=new Pen(Color.FromArgb(77,90,104),1))g.DrawLine(stripe,501-dx,y,501+dx,y);}
        using(var big=new Font("Segoe UI",19,FontStyle.Bold))g.DrawString("MIRAGE RUN",big,Brushes.White,24,20);
        using(var font=new Font("Consolas",10)){g.DrawString("TEST CIRCUIT  /  SECTOR 07",font,Brushes.LightSteelBlue,27,57);g.DrawString("VECTOR   041.7      ALT   283",font,Brushes.LightSteelBlue,26,492);}
    }
    [STAThread] static void Main(){Application.Run(new Fixture());}
}
'@
[IO.File]::WriteAllText("$run\Fixture.cs",$source)
& "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe" /nologo /target:winexe "/out:$fixture" /r:System.Windows.Forms.dll /r:System.Drawing.dll "$run\Fixture.cs"
if($LASTEXITCODE){throw 'fixture compilation'}
@{Quality=1; Minutes=5; Hotkey='Ctrl + Alt + F8'; SystemAudio=$true; Mic=$true; Auto=$true; Games=@()} | ConvertTo-Json | Set-Content -LiteralPath "$run\settings.json" -Encoding utf8
Add-Type -AssemblyName UIAutomationClient,UIAutomationTypes,System.Windows.Forms,System.Drawing
Add-Type @'
using System;
using System.Runtime.InteropServices;
public class UiNative {
 [DllImport("user32.dll")] public static extern bool PostMessage(IntPtr h,uint msg,IntPtr w,IntPtr l);
 [DllImport("user32.dll")] public static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
 [DllImport("user32.dll")] public static extern bool RegisterHotKey(IntPtr h,int id,uint mods,uint vk);
 [DllImport("user32.dll")] public static extern bool UnregisterHotKey(IntPtr h,int id);
 [DllImport("user32.dll")] static extern void keybd_event(byte vk,byte scan,uint flags,UIntPtr extra);
 public static void Chord(IntPtr target,params byte[] keys){
    if(target==IntPtr.Zero || GetForegroundWindow()!=target)throw new Exception("Keyboard target is not the test window.");
    foreach(byte vk in new byte[]{0x10,0x11,0x12,0x5B,0x5C})if(GetAsyncKeyState(vk)<0)throw new Exception("A modifier is already held.");
    foreach(byte vk in keys)keybd_event(vk,0,0,UIntPtr.Zero);
    for(int i=keys.Length-1;i>=0;i--)keybd_event(keys[i],0,2,UIntPtr.Zero);
 }
 [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h,IntPtr dc,uint flags);
 [DllImport("user32.dll")] public static extern bool GetCursorPos(out Point p);
 [DllImport("user32.dll")] public static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")] public static extern IntPtr WindowFromPoint(Point p);
 [DllImport("user32.dll")] public static extern IntPtr GetAncestor(IntPtr h,uint flags);
 [DllImport("user32.dll")] public static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")] public static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [StructLayout(LayoutKind.Sequential)]public struct Point{public int X,Y;}
 [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h,out Box r);
 [StructLayout(LayoutKind.Sequential)]public struct Box{public int L,T,R,B;}
}
'@
function Wait-Until($test,$desc,$seconds=22){
    $until=(Get-Date).AddSeconds($seconds)
    do{$value=& $test;if($value){return $value};Start-Sleep -Milliseconds 200}while((Get-Date) -lt $until)
    throw "Timeout: $desc"
}
function Find-Id($root,$id){
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::AutomationIdProperty,$id)
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
}
function Find-Name($root,$name){
    $condition=[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name)
    return $root.FindFirst([Windows.Automation.TreeScope]::Descendants,$condition)
}
function Find-Dialog($name){
    $c=[Windows.Automation.AndCondition]::new([Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::NameProperty,$name),[Windows.Automation.PropertyCondition]::new([Windows.Automation.AutomationElement]::ProcessIdProperty,$p.Id))
    return [Windows.Automation.AutomationElement]::RootElement.FindFirst([Windows.Automation.TreeScope]::Descendants,$c)
}
function Click($el){$el.GetCurrentPattern([Windows.Automation.InvokePattern]::Pattern).Invoke()}
function Screenshot($hw,$file){
    $r=[UiNative+Box]::new();[UiNative]::GetWindowRect($hw,[ref]$r)|Out-Null
    $bmp=[Drawing.Bitmap]::new($r.R-$r.L,$r.B-$r.T);$g=[Drawing.Graphics]::FromImage($bmp);$dc=$g.GetHdc()
    try{[UiNative]::PrintWindow($hw,$dc,2)|Out-Null}finally{$g.ReleaseHdc($dc)}
    $bmp.Save($file,[Drawing.Imaging.ImageFormat]::Png);$g.Dispose();$bmp.Dispose()
}
$p=$null;$game=$null;$editor=$null;$ok=$false
try{
    $p=Start-Process -FilePath "$run\Moments.exe" -WindowStyle Hidden -PassThru
    $hw=Wait-Until {$p.Refresh();if($p.MainWindowTitle -like 'Moments*'){$p.MainWindowHandle}} 'startup'
    $root=[Windows.Automation.AutomationElement]::FromHandle($hw)
    Wait-Until {Find-Id $root 'RecordButton'} 'capture page'|Out-Null
    Screenshot $hw (Join-Path $project 'build\capture-ui.png')
    Click (Find-Id $root 'SettingsNav')
    $binding=Wait-Until {Find-Id $root 'HotkeyButton'} 'shortcut button'
    $split=(Find-Id $root 'AppTracksBox').GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern)
    if($split.Current.ToggleState -ne [Windows.Automation.ToggleState]::On){throw 'App tracks should be enabled on upgrade'}
    $split.Toggle()
    Wait-Until {(Get-Content "$run\settings.json" -Raw|ConvertFrom-Json).SeparateApps -eq $false} 'single mix setting'|Out-Null
    if($binding.Current.Name -ne 'Ctrl + Alt + F8'){throw 'Legacy binding not loaded'}
    [UiNative]::SetForegroundWindow($hw)|Out-Null
    function Enter-Binding([byte[]]$keys){
        Click $binding
        Wait-Until {$binding.Current.Name -eq 'Press a shortcut…'} 'listening'|Out-Null
        [UiNative]::Chord($hw,$keys)
    }
    Enter-Binding @(0x1B)
    Wait-Until {$binding.Current.Name -eq 'Ctrl + Alt + F8'} 'Escape cancels'|Out-Null
    if(![UiNative]::RegisterHotKey(0,92,6,0x75)){throw 'Conflict fixture could not reserve Ctrl+Shift+F6'}
    try{
        Enter-Binding @(0xA2,0xA0,0x75)
        Wait-Until {(Find-Id $root 'HotkeyHelp').Current.Name -like '*in use*'} 'conflict reported'|Out-Null
        if($binding.Current.Name -ne 'Ctrl + Alt + F8'){throw 'Conflict displaced previous binding'}
        [UiNative]::Chord($hw,[byte[]]@(0xA2,0xA4,0x77))
        Wait-Until {(Find-Id $root 'Notice').Current.Name -like '*No replay buffer is active*'} 'previous hotkey still works'|Out-Null
    }finally{[UiNative]::UnregisterHotKey(0,92)|Out-Null}
    Enter-Binding @(0xA2,0xA0,0x76)
    Wait-Until {$binding.Current.Name -eq 'Ctrl + Shift + F7'} 'new custom shortcut'|Out-Null
    if((Get-Content "$run\settings.json" -Raw|ConvertFrom-Json).Hotkey -ne 'Ctrl + Shift + F7'){throw 'Custom binding not persisted'}
    if(![UiNative]::RegisterHotKey(0,92,3,0x77)){throw 'Previous shortcut was not released'}
    [UiNative]::UnregisterHotKey(0,92)|Out-Null
    $p.CloseMainWindow()|Out-Null
    if(!$p.WaitForExit(5000)){throw 'Test studio did not exit'}
    $p=Start-Process -FilePath "$run\Moments.exe" -WindowStyle Hidden -PassThru
    $hw=Wait-Until {$p.Refresh();if($p.MainWindowTitle -like 'Moments*'){$p.MainWindowHandle}} 'restart'
    $root=[Windows.Automation.AutomationElement]::FromHandle($hw)
    Wait-Until {(Find-Id $root 'ReplayHint').Current.Name -like '*Ctrl + Shift + F7*'} 'binding restored after restart'|Out-Null
    Note 'Shortcut listener passed legacy settings, Escape, OS conflict, atomic replacement and reload.'
    $game=Start-Process -FilePath $fixture -WindowStyle Hidden -PassThru
    Wait-Until {$game.Refresh();$game.MainWindowHandle -ne 0} 'fixture window'|Out-Null
    Click (Find-Id $root 'SettingsNav')
    $add=Wait-Until {Find-Name $root '+   Add game'} 'add game button'
    $binding=Find-Id $root 'HotkeyButton'
    [UiNative]::SetForegroundWindow($hw)|Out-Null
    $gameKeys=[byte[]]@(0xA4,0x6A);$gameChord='Alt + Multiply'
    if([UiNative]::RegisterHotKey(0,92,1,0x6A)){[UiNative]::UnregisterHotKey(0,92)|Out-Null}
    else{$gameKeys=[byte[]]@(0xA4,0xA0,0x6A);$gameChord='Alt + Shift + Multiply'}
    Enter-Binding $gameKeys
    Wait-Until {(Find-Id $root 'HotkeyHelp').Current.Name -like 'Shortcut saved*'} 'Alt and numpad multiply'|Out-Null
    Screenshot $hw (Join-Path $project 'build\settings-ui.png')
    Click $add
    $picker=Wait-Until {Find-Dialog 'Moments · Choose Game'} 'game picker'
    $choice=Wait-Until {Find-Name $picker 'Moments QA Game    ·    MomentFixture.exe'} 'fixture row'
    $choice.GetCurrentPattern([Windows.Automation.SelectionItemPattern]::Pattern).Select()
    Click (Find-Name $picker 'Select')
    Wait-Until {$root.Current.IsEnabled} 'close selection'|Out-Null
    Click (Find-Id $root 'CaptureNav')
    Wait-Until {(Find-Id $root 'ReplayTitle').Current.Name -like '*active*'} 'automatic game capture' 35|Out-Null
    Note 'Selected executable detected automatically; live buffer active on a static game window.'
    if(!(Find-Id $root 'ReplayHint').Current.Name.Contains($gameChord)){throw 'Global hotkey not registered'}
    Start-Sleep -Seconds 4
    [UiNative]::PostMessage($game.MainWindowHandle,0x800B,0,0)|Out-Null
    Start-Sleep -Milliseconds 200
    [UiNative]::SetForegroundWindow($game.MainWindowHandle)|Out-Null
    Wait-Until {[UiNative]::GetForegroundWindow() -eq $game.MainWindowHandle} 'fixture foreground'|Out-Null
    [UiNative]::Chord($game.MainWindowHandle,$gameKeys)
    $file=Wait-Until {Get-ChildItem "$run\Videos" -Filter '*.mp4'|Select-Object -First 1} 'hotkey writes clip'
    if(!(Select-String -LiteralPath "$run\Moments.log" -SimpleMatch 'Replay shortcut: Raw Input')){throw 'The fullscreen fallback was not exercised.'}
    if(@(Get-ChildItem "$run\Videos" -Filter '*.mp4').Count -ne 1){throw 'Duplicate replay'}
    Note "$gameChord saved one replay with the fullscreen raw-keyboard fixture focused (RIDEV_NOHOTKEYS)."
    $info=(& "$run\tools\ffprobe.exe" -v error -show_format -show_streams -of json $file.FullName)|ConvertFrom-Json
    $rate=$info.streams[0].avg_frame_rate.Split('/')
    if($info.streams.Count -ne 3 -or $info.streams[0].r_frame_rate -ne '60/1' -or [Math]::Abs([double]$rate[0]/[double]$rate[1]-60) -gt .2){throw 'Expected H.264 60 fps with two audio tracks'}
    Note "The global shortcut saved a 60 fps replay with 2 audio tracks. Duration: $($info.format.duration)."
    $game.CloseMainWindow()|Out-Null
    Wait-Until {(Find-Id $root 'StateText').Current.Name -like '*Idle*'} 'stop on game exit'|Out-Null
    # Use the captured game frames plus known audio envelopes to exercise the waveform UI.
    $sample=Join-Path "$run\Videos" 'Mirage Run · Тестовый клип.mp4'
    & "$run\tools\ffmpeg.exe" -v error -y -i $file.FullName -f lavfi -i 'aevalsrc=0.40*sin(2*PI*155*t)*sin(2*PI*1.7*t)*cos(2*PI*0.22*t):s=48000' -f lavfi -i 'aevalsrc=0.24*sin(2*PI*390*t)*sin(2*PI*0.8*t)*cos(2*PI*0.32*t):s=48000' -map 0:v:0 -map 1:a:0 -map 2:a:0 -c:v copy -c:a aac -metadata:s:a:0 handler_name=System -metadata:s:a:1 handler_name=Microphone -t $info.format.duration $sample
    if($LASTEXITCODE){throw 'waveform fixture'}
    $info=(& "$run\tools\ffprobe.exe" -v error -show_format -show_streams -of json $sample)|ConvertFrom-Json
    Click (Find-Id $root 'LibraryNav')
    $clip=Wait-Until {Find-Name $root 'Mirage Run · Тестовый клип'} 'library entry'
    $walker=[Windows.Automation.TreeWalker]::ControlViewWalker;$button=$clip
    while($button -and $button.Current.ControlType -ne [Windows.Automation.ControlType]::Button){$button=$walker.GetParent($button)}
    if(!$button){throw 'clip button'}
    Click $button
    $editor=Wait-Until {Find-Dialog 'Moments · Clip Editor'} 'editor'
    Wait-Until {(Find-Id $editor 'Feedback').Current.Name -like 'Ready*'} 'video preview' 30|Out-Null
    Screenshot ([intptr]$editor.Current.NativeWindowHandle) (Join-Path $project 'build\editor-ui.png')
    $slider=(Find-Id $editor 'Timeline').GetCurrentPattern([Windows.Automation.RangeValuePattern]::Pattern)
    $rail=(Find-Id $editor 'Timeline').Current.BoundingRectangle
    $label=(Find-Name $editor '▸   Video').Current.BoundingRectangle
    $eh=[intptr]$editor.Current.NativeWindowHandle
    $start=[UiNative+Point]::new();$start.X=[int]($rail.Left+2);$start.Y=[int]($label.Top+$label.Height/2)
    $end=[UiNative+Point]::new();$end.X=[int]($rail.Left+$rail.Width*.22);$end.Y=$start.Y
    foreach($point in @($start,$end)){if([UiNative]::GetAncestor([UiNative]::WindowFromPoint($point),2) -ne $eh){throw 'Drag target is covered by another window.'}}
    if([UiNative]::GetAsyncKeyState(1) -lt 0){throw 'Mouse is already pressed.'}
    $previous=[UiNative+Point]::new();[UiNative]::GetCursorPos([ref]$previous)|Out-Null
    try{
        [UiNative]::SetCursorPos($start.X,$start.Y)|Out-Null;Start-Sleep -Milliseconds 70
        [UiNative]::mouse_event(2,0,0,0,[uintptr]::Zero);Start-Sleep -Milliseconds 80
        [UiNative]::SetCursorPos($end.X,$end.Y)|Out-Null;Start-Sleep -Milliseconds 80
    }finally{[UiNative]::mouse_event(4,0,0,0,[uintptr]::Zero);[UiNative]::SetCursorPos($previous.X,$previous.Y)|Out-Null}
    Wait-Until {(Find-Id $editor 'Markers').Current.Name -notlike 'A   00:00.0 *'} 'drag A marker' 4|Out-Null
    Note 'Dragging the left edge changed the A marker through the timeline control.'
    foreach($ab in @(@(.4,1.0),@(1.5,2.0))){
        $slider.SetValue($ab[0]);Click (Find-Name $editor 'A   Mark in')
        $slider.SetValue($ab[1]);Click (Find-Name $editor 'B   Mark out');Click (Find-Name $editor 'Cut A–B')
    }
    $box=Find-Name $editor 'Microphone';$box.GetCurrentPattern([Windows.Automation.TogglePattern]::Pattern).Toggle()
    Start-Sleep -Milliseconds 300
    Screenshot ([intptr]$editor.Current.NativeWindowHandle) (Join-Path $project 'build\editor-ui.png')
    Click (Find-Id $editor 'PlayButton');Start-Sleep -Milliseconds 400;Click (Find-Id $editor 'PlayButton')
    Click (Find-Id $editor 'ExportButton')
    $edited=Wait-Until {Get-ChildItem "$run\Videos\Монтаж" -File|Select-Object -First 1} 'editor export' 45
    $out=(& "$run\tools\ffprobe.exe" -v error -show_format -show_streams -of json $edited.FullName)|ConvertFrom-Json
    $inputSec=[double]::Parse($info.format.duration,[cultureinfo]::InvariantCulture)
    $resultSec=[double]::Parse($out.format.duration,[cultureinfo]::InvariantCulture)
    if($out.streams.Count -ne 2 -or $out.streams[1].tags.handler_name -ne 'System' -or [Math]::Abs($inputSec-$resultSec-1.1) -gt .15){throw "Unexpected export $resultSec after two cuts from $inputSec"}
    Note "Library opened the clip in editor. RGB preview opened, two cuts applied and the microphone track removed. Export: $($out.format.duration)s."
    $ok=$true;Note 'PASS'
} catch{Note "FAIL $_ $($_.ScriptStackTrace)";throw} finally{
    if($editor){try{[UiNative]::PostMessage([intptr]$editor.Current.NativeWindowHandle,0x10,0,0)|Out-Null}catch{};Start-Sleep -Milliseconds 400}
    if($game -and !$game.HasExited){$game.CloseMainWindow()|Out-Null;if(!$game.WaitForExit(3000)){$game.Kill()}}
    if($p -and !$p.HasExited){$p.CloseMainWindow()|Out-Null;if(!$p.WaitForExit(5000)){$p.Kill()}}
    if($ok){
        $actual=[IO.Path]::GetFullPath($run);$parent=[IO.Path]::GetFullPath((Join-Path $project 'build'))
        if([IO.Path]::GetDirectoryName($actual) -eq $parent -and [IO.Path]::GetFileName($actual) -match '^ui_[0-9a-f]{32}$'){Remove-Item -LiteralPath $actual -Recurse -Force}
    }
}

