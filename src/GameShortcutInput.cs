using System.Runtime.InteropServices;
using System.Windows.Input;
namespace Moments;

// Background keyboard input is needed for foreground games that suppress WM_HOTKEY.
// This registration is scoped to the lifetime of an active replay buffer.
internal sealed class GameShortcutInput
{
    [StructLayout(LayoutKind.Sequential)] struct Device {public ushort Page,Usage;public uint Flags;public nint Window;}
    [StructLayout(LayoutKind.Sequential)] struct Header {public uint Type,Size;public nint Device,Param;}
    [StructLayout(LayoutKind.Sequential)] struct Packet {public Header Header;public ushort Scan,Flags,Reserved,Key;public uint Message,Extra;}
    [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterRawInputDevices(ref Device device,uint count,uint size);
    [DllImport("user32.dll")] static extern uint GetRawInputData(nint handle,uint command,out Packet data,ref uint size,uint headerSize);
    [DllImport("user32.dll")] static extern short GetAsyncKeyState(int key);
    [DllImport("user32.dll")] internal static extern nint GetForegroundWindow();
    readonly HashSet<int> modifiers=[];
    bool enabled,mainDown;
    public void Start(nint hwnd)
    {
        var device=new Device{Page=1,Usage=6,Flags=0x100,Window=hwnd};
        enabled=RegisterRawInputDevices(ref device,1,(uint)Marshal.SizeOf<Device>());
        if(!enabled){Home.Log("Raw Input: "+Marshal.GetLastWin32Error());return;}
        modifiers.Clear();mainDown=false;
        foreach(int key in new[]{0xA0,0xA1,0xA2,0xA3,0xA4,0xA5,0x5B,0x5C})
            if(GetAsyncKeyState(key)<0)modifiers.Add(key);
    }
    public void Stop()
    {
        if(!enabled)return;
        var remove=new Device{Page=1,Usage=6,Flags=1};
        RegisterRawInputDevices(ref remove,1,(uint)Marshal.SizeOf<Device>());enabled=false;modifiers.Clear();mainDown=false;
    }
    public bool Matches(nint input,ReplayHotkey chord)
    {
        if(!enabled)return false;
        uint size=(uint)Marshal.SizeOf<Packet>();
        if(GetRawInputData(input,0x10000003,out var packet,ref size,(uint)Marshal.SizeOf<Header>())!=size || packet.Header.Type!=1)return false;
        bool e0=(packet.Flags&2)!=0, released=(packet.Flags&1)!=0;
        int key=packet.Key switch{
            0x10=>packet.Scan==0x36?0xA1:0xA0,
            0x11=>e0?0xA3:0xA2,0x12=>e0?0xA5:0xA4,
            _ when packet.Scan==0x37 && !e0=>0x6A, _=>packet.Key
        };
        if(ReplayHotkey.ModifierFor(key)!=ModifierKeys.None){
            if(released)modifiers.Remove(key);else modifiers.Add(key);
            return false;
        }
        if(key!=chord.Key)return false;
        if(released){mainDown=false;return false;}
        if(mainDown)return false;mainDown=true;
        return modifiers.Aggregate(ModifierKeys.None,(v,k)=>v|ReplayHotkey.ModifierFor(k))==chord.Modifiers;
    }
}
