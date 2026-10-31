using System.ComponentModel;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using FormsKeys=System.Windows.Forms.Keys;

namespace Moments;

internal readonly record struct ReplayHotkey(ModifierKeys Modifiers,int Key)
{
    internal static ModifierKeys ModifierFor(int vk) => vk switch {
        0x10 or 0xA0 or 0xA1 => ModifierKeys.Shift,
        0x11 or 0xA2 or 0xA3 => ModifierKeys.Control,
        0x12 or 0xA4 or 0xA5 => ModifierKeys.Alt,
        0x5B or 0x5C => ModifierKeys.Windows,
        _ => ModifierKeys.None
    };

    internal static string Prefix(ModifierKeys modifiers)
    {
        var names=new List<string>(4);
        if(modifiers.HasFlag(ModifierKeys.Control))names.Add("Ctrl");
        if(modifiers.HasFlag(ModifierKeys.Alt))names.Add("Alt");
        if(modifiers.HasFlag(ModifierKeys.Shift))names.Add("Shift");
        if(modifiers.HasFlag(ModifierKeys.Windows))names.Add("Win");
        return names.Count==0 ? "" : string.Join(" + ",names)+" + ";
    }

    public override string ToString()
    {
        string name=Key is >=0x30 and <=0x39 ? ((char)Key).ToString() :
            Enum.GetName((FormsKeys)Key) ?? $"VK-{Key:X2}";
        return Prefix(Modifiers)+name;
    }

    public static bool TryParse(string? text,out ReplayHotkey result)
    {
        result=default;
        if(string.IsNullOrWhiteSpace(text))return false;
        string[] parts=text.Split('+',StringSplitOptions.TrimEntries);
        ModifierKeys modifiers=ModifierKeys.None;
        foreach(string part in parts[..^1]){
            var mod=part.ToUpperInvariant() switch {
                "CTRL"=>ModifierKeys.Control, "ALT"=>ModifierKeys.Alt,
                "SHIFT"=>ModifierKeys.Shift, "WIN"=>ModifierKeys.Windows, _=>ModifierKeys.None
            };
            if(mod==ModifierKeys.None || modifiers.HasFlag(mod))return false;
            modifiers|=mod;
        }
        string name=parts[^1];int key;
        if(name.Length==1 && char.IsAsciiDigit(name[0]))key=name[0];
        else if(name.StartsWith("VK-",StringComparison.OrdinalIgnoreCase)){
            if(!int.TryParse(name.AsSpan(3),NumberStyles.HexNumber,CultureInfo.InvariantCulture,out key))return false;
        }else if(Enum.TryParse<FormsKeys>(name,true,out var parsed))key=(int)parsed;
        else return false;
        if(key is <8 or >254 || ModifierFor(key)!=ModifierKeys.None)return false;
        result=new(modifiers,key);return true;
    }

    public bool Register(nint hwnd,int id)=> Key!=0x7B && Native.RegisterHotKey(hwnd,id,(uint)Modifiers|0x4000,(uint)Key);
}

// The hook exists only while the focused settings button is listening. Intercept
// the main key before Windows dispatches any existing system-wide binding.
internal sealed class ShortcutListener : IDisposable
{
    delegate nint HookProc(int code,nint message,nint data);
    [DllImport("user32.dll",EntryPoint="SetWindowsHookExW",SetLastError=true)]
    static extern nint Install(int type,HookProc callback,nint module,uint thread);
    [DllImport("user32.dll")] static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32.dll")] static extern nint CallNextHookEx(nint hook,int code,nint message,nint data);
    [DllImport("user32.dll")] static extern nint GetForegroundWindow();
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern nint GetModuleHandle(string? name);

    readonly nint owner;
    readonly HookProc callback;
    readonly Action<string> preview;
    readonly Action<ShortcutListener,ReplayHotkey?> completed;
    readonly HashSet<int> heldModifiers=[];
    nint hook;
    ReplayHotkey? pending;
    bool finished;

    public ShortcutListener(nint owner,Action<string> preview,Action<ShortcutListener,ReplayHotkey?> completed)
    {
        this.owner=owner;this.preview=preview;this.completed=completed;callback=OnKey;
        foreach(int vk in new[]{0xA0,0xA1,0xA2,0xA3,0xA4,0xA5,0x5B,0x5C})
            if(GetAsyncKeyState(vk)<0)heldModifiers.Add(vk);
        hook=Install(13,callback,GetModuleHandle(null),0);
        if(hook==0)throw new Win32Exception(Marshal.GetLastWin32Error());
    }

    ModifierKeys Modifiers => heldModifiers.Aggregate(ModifierKeys.None,(mods,vk)=>mods|ReplayHotkey.ModifierFor(vk));

    nint OnKey(int code,nint message,nint data)
    {
        if(code>=0 && !finished && GetForegroundWindow()==owner){
            int key=Marshal.ReadInt32(data);
            bool up=message==0x101 || message==0x105;
            if(ReplayHotkey.ModifierFor(key)!=ModifierKeys.None){
                if(up)heldModifiers.Remove(key);else heldModifiers.Add(key);
                if(pending==null)preview(ReplayHotkey.Prefix(Modifiers)+"…");
            }else if(key is >=8 and <=254){
                if(!up && pending==null){pending=new(Modifiers,key);preview(pending.Value.ToString());}
                if(pending is { } chord){
                    if(up && key==chord.Key){
                        finished=true;
                        completed(this,chord is { Key:0x1B, Modifiers:ModifierKeys.None } ? null : chord);
                    }
                    return 1;
                }
            }
        }
        return CallNextHookEx(hook,code,message,data);
    }

    public void Dispose()
    {
        if(hook!=0){UnhookWindowsHookEx(hook);hook=0;}
    }
}

public partial class MainWindow
{
    ShortcutListener? hotkeyListener;
    int hotkeyId=17;
    ReplayHotkey activeShortcut;
    bool splitKeyReleased;
    internal void GiveSplitKeyToEditor(bool active)
    {
        if(active && hotkeyOk && activeShortcut is {Key:0x53,Modifiers:ModifierKeys.None}){
            if(Native.UnregisterHotKey(hwnd,hotkeyId)){splitKeyReleased=true;hotkeyOk=false;}
        }else if(!active && splitKeyReleased){
            splitKeyReleased=false;RegisterKey();
        }
    }
    const string IdleHotkeyHint="Click the shortcut to change it.";

    void RegisterKey()
    {
        hotkeyOk=ReplayHotkey.TryParse(cfg.Hotkey,out activeShortcut) && activeShortcut.Register(hwnd,hotkeyId);
        if(!hotkeyOk)HotkeyHelp.Text="The saved shortcut is unavailable. Click to choose another.";
        UpdateState();
    }

    void ListenHotkey(object sender,RoutedEventArgs e)
    {
        if(hotkeyListener!=null){StopListening();return;}
        HotkeyButton.Focus();
        try{
            hotkeyListener=new ShortcutListener(hwnd,text=>HotkeyButton.Content=text,(listener,chord)=>
                Dispatcher.BeginInvoke(()=>{if(hotkeyListener==listener){StopListening();if(chord.HasValue)ApplyHotkey(chord.Value);}}));
            HotkeyButton.Content="Press a shortcut…";
            HotkeyHelp.Text="Press and release the keys you want. Press Esc to cancel.";
        }catch(Win32Exception ex){HotkeyHelp.Text="Could not listen for a shortcut. "+ex.Message;}
    }

    void StopListening()
    {
        if(hotkeyListener==null)return;
        hotkeyListener.Dispose();hotkeyListener=null;
        HotkeyButton.Content=cfg.Hotkey;HotkeyHelp.Text=IdleHotkeyHint;
    }

    void ApplyHotkey(ReplayHotkey chord)
    {
        if(hotkeyOk && ReplayHotkey.TryParse(cfg.Hotkey,out var old) && old==chord)return;
        // Keep the previous shortcut registered until its replacement is both
        // accepted by Windows and saved to disk.
        int nextId=hotkeyId==17?18:17;
        if(!chord.Register(hwnd,nextId)){
            HotkeyHelp.Text=$"{chord} is in use or reserved. Try another shortcut.";
            return;
        }
        string previous=cfg.Hotkey;cfg.Hotkey=chord.ToString();
        try{cfg.Save();}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){
            cfg.Hotkey=previous;Native.UnregisterHotKey(hwnd,nextId);
            HotkeyHelp.Text="Could not save the shortcut. "+ex.Message;return;
        }
        if(hotkeyOk)Native.UnregisterHotKey(hwnd,hotkeyId);
        hotkeyId=nextId;activeShortcut=chord;hotkeyOk=true;HotkeyButton.Content=cfg.Hotkey;
        HotkeyHelp.Text="Shortcut saved. Click again to change it.";UpdateState();
    }
}
