using Microsoft.Win32;
using System.Windows;
namespace Moments;
public partial class AssemblyWindow
{
    readonly MontageProjects projects=new(Path.Combine(Home.Root,"Projects"));
    int projectRevision;
    internal string ProjectFile=>projects.CurrentPath;
    void ProjectLabel(){ProjectLocation.Text=Path.GetFileName(projects.CurrentPath)+"  ·  Autosaved";ProjectLocation.ToolTip=projects.CurrentPath;}
    void SwitchProject(Sequence next){
        projectRevision++;Pause();CancelDrag();edit=next;loaded=true;frame=0;lane=1;selected=null;history.Clear();future.Clear();
        TimelineCanvas.IsEnabled=true;Refresh();ReloadBin();ProjectLabel();Say("Project opened. Changes are saved automatically.");
    }
    internal void OpenProject(string path)=>SwitchProject(projects.Open(path));
    internal void NewProject()=>SwitchProject(projects.New());
    internal void SaveProjectAs(string path){
        if(!loaded || closing)throw new IOException("Open a project before saving it.");
        projects.SaveAs(edit,path);projectRevision++;ProjectLabel();Say("Project saved. Further changes are saved to this file.");
    }
    void NewProjectClick(object s,RoutedEventArgs e){if(closing)return;try{NewProject();}catch(Exception ex){Say("Could not create project. "+ex.Message);}}
    void OpenProjectClick(object s,RoutedEventArgs e){
        if(closing)return;Directory.CreateDirectory(projects.Folder);
        var dialog=new OpenFileDialog{Title="Open montage project",Filter="Moments project (*.moments;*.json)|*.moments;*.json",InitialDirectory=projects.Folder};
        if(dialog.ShowDialog(this)==true)try{OpenProject(dialog.FileName);}catch(Exception ex){Say("Could not open project. "+ex.Message);}
    }
    void SaveProjectClick(object s,RoutedEventArgs e){
        if(!loaded || closing)return;Directory.CreateDirectory(projects.Folder);
        string name=new(edit.Title.Select(c=>Path.GetInvalidFileNameChars().Contains(c)?'_':c).ToArray());
        var dialog=new SaveFileDialog{Title="Save montage project as",Filter="Moments project (*.moments)|*.moments",DefaultExt=".moments",AddExtension=true,FileName=name,InitialDirectory=projects.Folder,OverwritePrompt=true};
        if(dialog.ShowDialog(this)==true)try{SaveProjectAs(dialog.FileName);}catch(Exception ex){Say("Could not save project. "+ex.Message);}
    }
}
