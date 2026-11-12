using System.Text.Json;
namespace Moments;

// Each named project is its own autosaved Sequence. The small session pointer
// remembers which project to reopen; the old current.json draft remains intact.
internal sealed class MontageProjects
{
    public string Folder {get;}
    readonly string session;
    public string CurrentPath {get;private set;}
    public MontageProjects(string folder){Folder=Path.GetFullPath(folder);CurrentPath=Path.Combine(Folder,"current.json");session=Path.Combine(Folder,"last-project.json");}
    public Sequence Load(){
        if(System.IO.File.Exists(session)){
            string path=JsonSerializer.Deserialize<string>(System.IO.File.ReadAllText(session))??throw new InvalidDataException("Invalid last-project reference.");
            return Open(path);
        }
        return Sequence.Load(CurrentPath);
    }
    void Select(string path){
        Directory.CreateDirectory(Folder);string temp=session+".tmp";
        System.IO.File.WriteAllText(temp,JsonSerializer.Serialize(path));System.IO.File.Move(temp,session,true);CurrentPath=path;
    }
    string ProjectPath(string path){
        path=Path.GetFullPath(path);
        if(string.Equals(path,session,StringComparison.OrdinalIgnoreCase) || string.Equals(path,session+".tmp",StringComparison.OrdinalIgnoreCase))
            throw new IOException("Choose a project file, not the last-project reference.");
        return path;
    }
    public Sequence Open(string path){
        path=ProjectPath(path);if(!System.IO.File.Exists(path))throw new FileNotFoundException("Project file unavailable. Use Open project to choose another project.",path);
        var next=Sequence.Load(path);Select(path);return next;
    }
    public void Save(Sequence edit)=>edit.Save(CurrentPath);
    public void SaveAs(Sequence edit,string path){path=ProjectPath(path);edit.Save(path);Select(path);}
    public Sequence New(){
        var next=new Sequence();string path=Path.Combine(Folder,$"Project {DateTime.Now:yyyy-MM-dd HH-mm-ss} {Guid.NewGuid().ToString("N")[..6]}.moments");
        SaveAs(next,path);return next;
    }
}
