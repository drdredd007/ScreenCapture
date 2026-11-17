using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Moments;
public partial class MainWindow
{
    void RefreshLibraryViewButtons(){
        ListViewButton.Background=(Brush)FindResource(cfg.LibraryTiles?"Panel":"SelectedSurface");
        TilesViewButton.Background=(Brush)FindResource(cfg.LibraryTiles?"SelectedSurface":"Panel");
        ListViewButton.Foreground=(Brush)FindResource(cfg.LibraryTiles?"Dim":"Mint");
        TilesViewButton.Foreground=(Brush)FindResource(cfg.LibraryTiles?"Mint":"Dim");
    }
    void LibraryViewClick(object sender,RoutedEventArgs e){
        bool tiles=((Button)sender).Tag as string=="tiles";if(tiles==cfg.LibraryTiles)return;
        cfg.LibraryTiles=tiles;cfg.Save();RefreshLibraryViewButtons();LoadLibrary();
    }
    void LibraryResized(object sender,SizeChangedEventArgs e){
        if(!initialized || !cfg.LibraryTiles)return;
        double width=LibraryLayout.TileWidth(ClipList.ActualWidth);
        foreach(var wrap in ClipList.Children.OfType<WrapPanel>())foreach(var button in wrap.Children.OfType<Button>()){
            button.Width=width;button.Height=double.NaN;button.MinHeight=(width-18)*9/16+108;
            if(button.Content is StackPanel stack && stack.Children[0] is Border preview)preview.Height=(width-18)*9/16;
        }
    }
    Button LibraryCard(FileInfo file,bool tiles,List<(Image,FileInfo)> thumbnails){
        double width=LibraryLayout.TileWidth(ClipList.ActualWidth);
        var button=new Button{Background=(Brush)FindResource("Panel"),BorderBrush=(Brush)FindResource("Line"),HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,
            Margin=tiles?new Thickness(0,0,10,10):new Thickness(0,0,0,6),Padding=new Thickness(8),ToolTip=file.Name};
        var image=new Image{Stretch=Stretch.Uniform};
        var preview=new Border{Background=BrushOf("#0C1116"),Child=image,CornerRadius=new CornerRadius(3),ClipToBounds=true};
        thumbnails.Add((image,file));
        var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        words.Children.Add(new TextBlock{Text=Home.ClipTitle(file.Name),TextTrimming=TextTrimming.CharacterEllipsis,FontWeight=FontWeights.SemiBold,FontSize=15,
            TextWrapping=tiles?TextWrapping.Wrap:TextWrapping.NoWrap,MaxHeight=tiles?42:24});
        words.Children.Add(new TextBlock{Text=$"{file.CreationTime:HH:mm}   ·   {file.Extension[1..].ToUpperInvariant()}   ·   {file.Length/1048576d:0.#} MB",Style=(Style)FindResource("Sub"),FontSize=12});
        var markers=ClipEvents.Read(file.FullName);
        var tags=LibraryLayout.EventTags(markers);
        if(tags.Length>0){
            var badges=new WrapPanel{Tag="EventTags",Margin=new Thickness(0,5,0,0),ToolTip=markers!.Game+" · saved events"};
            foreach(var tag in tags){
                var moments=markers.Events.Where(e=>e!=null && !string.IsNullOrWhiteSpace(e.Label) &&
                    double.IsFinite(e.Seconds) && e.Seconds>=0 && LibraryLayout.EventLabel(e.Label).Equals(tag.Label,StringComparison.OrdinalIgnoreCase))
                    .OrderBy(e=>e.Seconds).ToArray();
                badges.Children.Add(EventButton(file.FullName,tag,moments));
            }
            words.Children.Add(badges);
        }
        if(tiles){
            button.Width=width;button.MinHeight=(width-18)*9/16+108;preview.Height=(width-18)*9/16;
            var stack=new StackPanel();stack.Children.Add(preview);words.Margin=new Thickness(2,9,2,3);stack.Children.Add(words);
            button.Content=stack;
        }else{
            var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(105)});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            preview.Width=96;preview.Height=54;preview.HorizontalAlignment=HorizontalAlignment.Left;grid.Children.Add(preview);
            Grid.SetColumn(words,1);grid.Children.Add(words);
            var edit=new TextBlock{Text="Edit  ›",Margin=new Thickness(10,0,5,0)};Grid.SetColumn(edit,2);grid.Children.Add(edit);button.Content=grid;
        }
        button.Click+=(_,_)=>OpenLibraryClip(file.FullName);return button;
    }
    void OpenLibraryClip(string file,double start=0){
        if(quitting || installingUpdate || updateHandedOff)return;
        try{Editing.OpenClip(file,start);}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){Message("Could not open recording. "+ex.Message);LoadLibrary();}
    }
    Button EventButton(string file,LibraryLayout.EventTag tag,ClipEvent[] moments){
        string tone=tag.Label switch{
            "Pentakill" or "Quadra kill" or "Triple kill"=>"MajorEvent",
            "Double kill" or "Dragon steal" or "Baron steal"=>"SpecialEvent",
            _=>"Mint"
        };
        var ink=(Brush)FindResource(tone);
        var caption=new StackPanel{Orientation=Orientation.Horizontal};
        caption.Children.Add(new TextBlock{Text=tag.Label+(moments.Length>1?$" ×{moments.Length}":""),Foreground=ink,FontWeight=FontWeights.SemiBold});
        caption.Children.Add(new TextBlock{Text=moments.Length==1?Home.Time(moments[0].Seconds):"▾",Margin=new Thickness(7,0,0,0),Foreground=(Brush)FindResource("Dim")});
        var jump=new Button{Content=caption,Background=(Brush)FindResource("Back"),BorderBrush=(Brush)FindResource("Line"),FontSize=11,
            Margin=new Thickness(0,3,5,2),Padding=new Thickness(7,3,7,3),ToolTip=moments.Length==1?"Open at this moment":"Choose a moment"};
        System.Windows.Automation.AutomationProperties.SetName(jump,tag.Label+" · "+(moments.Length==1?Home.Time(moments[0].Seconds):moments.Length+" events"));
        jump.Click+=(_,e)=>{
            e.Handled=true;
            if(moments.Length==1){OpenLibraryClip(file,moments[0].Seconds);return;}
            var menu=new ContextMenu{PlacementTarget=jump,Placement=System.Windows.Controls.Primitives.PlacementMode.Bottom};
            foreach(var moment in moments){
                var entry=new MenuItem{Header=$"{Home.Time(moment.Seconds)}   {tag.Label}   ·   game {Home.Time(moment.GameTime)}"};
                entry.Click+=(_,_)=>OpenLibraryClip(file,moment.Seconds);menu.Items.Add(entry);
            }
            menu.IsOpen=true;
        };
        return jump;
    }
    async void LoadLibrary()
    {
        int generation=++libraryGeneration;ClipList.Children.Clear();var filter=Search.Text.Trim();
        var files=new DirectoryInfo(Home.Videos).EnumerateFiles("*",SearchOption.AllDirectories)
            .Where(f=>new[]{".mp4",".mkv",".webm"}.Contains(f.Extension.ToLowerInvariant()) && f.Name.Contains(filter,StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(f=>f.CreationTimeUtc).ToArray();
        LibraryCount.Text=files.Length==0?(filter.Length>0?"No matches":"No recordings yet. Your recordings and exports will appear here."):$"Recordings: {files.Length}   ·   Newest first   ·   Search by name or date";
        var thumbnails=new List<(Image,FileInfo)>();
        foreach(var group in files.GroupBy(f=>f.CreationTime.Date)){
            ClipList.Children.Add(new TextBlock{Text=group.Key.ToString("MMM d, yyyy",System.Globalization.CultureInfo.GetCultureInfo("en-US")),Style=(Style)FindResource("Kicker"),Margin=new Thickness(0,16,0,7)});
            Panel container=cfg.LibraryTiles?new WrapPanel():new StackPanel();ClipList.Children.Add(container);
            foreach(var file in group)container.Children.Add(LibraryCard(file,cfg.LibraryTiles,thumbnails));
        }
        foreach(var (image,file) in thumbnails){
            if(generation!=libraryGeneration || page!=1 || quitting)return;
            try{image.Source=await Media.Thumbnail(file);}catch(Exception ex){Home.Log("Thumbnail: "+ex.Message);}
        }
    }
}
