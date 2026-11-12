using System.Windows;
namespace Moments;

// One live draft, many independent source windows. No owner/modal relationship:
// minimizing or closing a source must never hide or close the montage.
internal sealed class EditingWorkspace
{
    readonly Dictionary<Window,TaskCompletionSource> windows=[];
    AssemblyWindow? timeline;
    bool closing;
    public bool HasWindows=>windows.Count!=0;
    public event Action? LibraryChanged;
    public event Action? WindowsChanged;
    public void NotifyLibraryChanged()=>LibraryChanged?.Invoke();
    T Track<T>(T window) where T:Window {
        var closed=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);windows.Add(window,closed);
        window.Closed+=(_,_)=>{
            windows.Remove(window);if(ReferenceEquals(timeline,window))timeline=null;
            closed.TrySetResult();WindowsChanged?.Invoke();NotifyLibraryChanged();
        };
        WindowsChanged?.Invoke();window.Show();return window;
    }
    public Editor OpenClip(string file){
        if(closing)throw new InvalidOperationException("The editing workspace is closing.");
        return Track(new Editor(file,this));
    }
    public AssemblyWindow OpenTimeline(){
        if(closing)throw new InvalidOperationException("The editing workspace is closing.");
        if(timeline?.IsClosing==true)throw new IOException("The video editor is still closing. Try again in a moment.");
        if(timeline==null){timeline=new AssemblyWindow();timeline.Exported+=NotifyLibraryChanged;Track(timeline);}
        if(timeline.WindowState==WindowState.Minimized)timeline.WindowState=WindowState.Normal;
        timeline.Show();timeline.Activate();return timeline;
    }
    public async Task Append(string file,Interval[]? ranges=null,int[]? streams=null){
        var target=OpenTimeline();await target.AppendSource(file,ranges,streams);
    }
    public async Task CloseAll(){
        closing=true;var pending=windows.ToArray();
        foreach(var item in pending)item.Key.Close();
        await Task.WhenAll(pending.Select(item=>item.Value.Task));
    }
}
