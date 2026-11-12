using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
namespace Moments;
public partial class MainWindow
{
    void RefreshLibraryViewButtons(){
        ListViewButton.Background=BrushOf(cfg.LibraryTiles?"#29313A":"#405545");
        TilesViewButton.Background=BrushOf(cfg.LibraryTiles?"#405545":"#29313A");
        ListViewButton.IsEnabled=cfg.LibraryTiles;TilesViewButton.IsEnabled=!cfg.LibraryTiles;
    }
    void LibraryViewClick(object sender,RoutedEventArgs e){
        cfg.LibraryTiles=((Button)sender).Tag as string=="tiles";cfg.Save();RefreshLibraryViewButtons();LoadLibrary();
    }
    void LibraryResized(object sender,SizeChangedEventArgs e){
        if(!initialized || !cfg.LibraryTiles)return;
        double width=LibraryLayout.TileWidth(ClipList.ActualWidth);
        foreach(var wrap in ClipList.Children.OfType<WrapPanel>())foreach(var button in wrap.Children.OfType<Button>()){
            button.Width=width;
            if(button.Content is StackPanel stack && stack.Children[0] is Border preview)preview.Height=(width-18)*9/16;
        }
    }
    Button LibraryCard(FileInfo file,bool tiles,List<(Image,FileInfo)> thumbnails){
        double width=LibraryLayout.TileWidth(ClipList.ActualWidth);
        var button=new Button{HorizontalContentAlignment=HorizontalAlignment.Stretch,VerticalContentAlignment=VerticalAlignment.Top,
            Margin=tiles?new Thickness(0,0,10,10):new Thickness(0,0,0,6),Padding=new Thickness(8),ToolTip=file.Name};
        var image=new Image{Stretch=Stretch.Uniform};
        var preview=new Border{Background=BrushOf("#0C1116"),Child=image,CornerRadius=new CornerRadius(3),ClipToBounds=true};
        thumbnails.Add((image,file));
        var words=new StackPanel{VerticalAlignment=VerticalAlignment.Center};
        words.Children.Add(new TextBlock{Text=Home.ClipTitle(file.Name),TextTrimming=TextTrimming.CharacterEllipsis,FontWeight=FontWeights.SemiBold,
            TextWrapping=tiles?TextWrapping.Wrap:TextWrapping.NoWrap,MaxHeight=tiles?42:24});
        words.Children.Add(new TextBlock{Text=$"{file.CreationTime:HH:mm}   ·   {file.Extension[1..].ToUpperInvariant()}   ·   {file.Length/1048576d:0.#} MB",Style=(Style)FindResource("Sub"),FontSize=12});
        var markers=ClipEvents.Read(file.FullName);
        if(markers?.Events?.Length>0)words.Children.Add(new TextBlock{Text=$"AUTO · LoL · {markers.Events.Length} events",Foreground=BrushOf("#B7F59B"),FontSize=11,Margin=new Thickness(0,4,0,0)});
        if(tiles){
            button.Width=width;preview.Height=(width-18)*9/16;
            var stack=new StackPanel();stack.Children.Add(preview);words.Margin=new Thickness(2,9,2,3);stack.Children.Add(words);
            button.Content=stack;
        }else{
            var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(105)});grid.ColumnDefinitions.Add(new());grid.ColumnDefinitions.Add(new(){Width=GridLength.Auto});
            preview.Width=96;preview.Height=54;preview.HorizontalAlignment=HorizontalAlignment.Left;grid.Children.Add(preview);
            Grid.SetColumn(words,1);grid.Children.Add(words);
            var edit=new TextBlock{Text="Edit  ›",Margin=new Thickness(10,0,5,0)};Grid.SetColumn(edit,2);grid.Children.Add(edit);button.Content=grid;
        }
        button.Click+=(_,_)=>{new Editor(file.FullName){Owner=this}.ShowDialog();LoadLibrary();};return button;
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
