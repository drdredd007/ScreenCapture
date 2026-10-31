using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
namespace Moments;
public partial class GamePicker : Window
{
    sealed record Entry(string Path,string Label,bool Folder){public override string ToString()=>Label;}
    public string? SelectedFile {get;private set;}
    string current="";
    readonly bool media;
    public GamePicker(bool media=false){
        this.media=media;InitializeComponent();
        if(!media){Running();return;}
        Title="Moments · Import Video";Category.Text="MOMENTS   /   MEDIA";Heading.Text="Import video";
        Description.Text="Browse to a folder or paste the full path to a video file.";Location.ToolTip="Path to a folder or video file";
        RunningButton.Visibility=Visibility.Collapsed;Browse(Home.Videos);
    }
    bool Accepted(string path)=>media?AssemblyWindow.IsVideo(path):System.IO.Path.GetExtension(path).Equals(".exe",StringComparison.OrdinalIgnoreCase);
    void RunningClick(object s,RoutedEventArgs e)=>Running();
    void Running()
    {
        RunningButton.Style=(Style)FindResource("Accent");FilesButton.Style=(Style)FindResource(typeof(System.Windows.Controls.Button));
        PathRow.Visibility=Visibility.Collapsed;var rows=new List<Entry>();
        foreach(var t in Native.Windows()){
            Native.GetWindowThreadProcessId(t.Handle,out int pid);
            if(Native.ImagePath(pid) is string path)rows.Add(new(path,$"{t.Title}    ·    {System.IO.Path.GetFileName(path)}",false));
        }
        Entries.ItemsSource=rows.DistinctBy(x=>x.Path,StringComparer.OrdinalIgnoreCase).OrderBy(x=>x.Label).ToList();
        Hint.Text="Double-click a game to enable automatic capture for it.";
    }
    void FilesClick(object s,RoutedEventArgs e)=>Browse(current);
    void Browse(string path)
    {
        try{
            var rows=new List<Entry>();
            if(path.Length==0)rows.AddRange(DriveInfo.GetDrives().Where(d=>d.IsReady).Select(d=>new Entry(d.Name,"▸   "+d.Name,true)));
            else{
                var dir=new DirectoryInfo(path);
                rows.AddRange(dir.EnumerateDirectories().Where(d=>(d.Attributes&(FileAttributes.Hidden|FileAttributes.System))==0).OrderBy(d=>d.Name).Select(d=>new Entry(d.FullName,"▸   "+d.Name,true)));
                rows.AddRange(dir.EnumerateFiles().Where(f=>Accepted(f.FullName)).OrderBy(f=>f.Name).Select(f=>new Entry(f.FullName,"     "+f.Name,false)));
                path=dir.FullName;
            }
            current=path;Location.Text=path;Entries.ItemsSource=rows;PathRow.Visibility=Visibility.Visible;
            FilesButton.Style=(Style)FindResource("Accent");RunningButton.Style=(Style)FindResource(typeof(System.Windows.Controls.Button));
            Hint.Text=path.Length==0?"Select a drive or enter a file path.":path;
        }catch(Exception ex)when(ex is IOException or UnauthorizedAccessException or ArgumentException){Hint.Text=ex.Message;}
    }
    void UpClick(object s,RoutedEventArgs e)=>Browse(current==""?"":Directory.GetParent(current)?.FullName??"");
    void PathClick(object s,RoutedEventArgs e)=>OpenPath(Location.Text.Trim().Trim('"'));
    void PathKey(object s,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;PathClick(s,e);}}
    void OpenPath(string path){
        if(File.Exists(path)&&Accepted(path)){SelectedFile=System.IO.Path.GetFullPath(path);DialogResult=true;}
        else if(path=="" || Directory.Exists(path))Browse(path);
        else Hint.Text=media?"Enter an existing folder or video file.":"Enter an existing folder or .exe file.";
    }
    void ChooseClick(object s,RoutedEventArgs e){if(Entries.SelectedItem is Entry row)OpenPath(row.Path);else if(PathRow.IsVisible)OpenPath(Location.Text.Trim().Trim('"'));}
    void ChooseDoubleClick(object s,MouseButtonEventArgs e)=>ChooseClick(s,e);
    void ListKey(object s,KeyEventArgs e){if(e.Key==Key.Enter){e.Handled=true;ChooseClick(s,e);}}
}

