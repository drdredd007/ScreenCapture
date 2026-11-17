namespace Moments;
internal static class LibraryLayout
{
    public static double TileWidth(double available)=>Math.Max(120,Math.Max(120,available)/Math.Max(1,(int)(Math.Max(120,available)/270))-10);
    internal sealed record EventTag(string Label,int Count);
    public static string EventLabel(string label)=>label.Trim() switch{
        "My kills"=>"Kill","My deaths"=>"Death","My assists"=>"Assist",
        "My dragon steals"=>"Dragon steal","My Baron steals"=>"Baron steal","My Herald steals"=>"Herald steal",
        var value=>value.Length>64?value[..64]:value
    };
    public static EventTag[] EventTags(ClipEvents? clip)=>
        (clip?.Events??[]).Where(e=>e!=null && !string.IsNullOrWhiteSpace(e.Label) && double.IsFinite(e.Seconds) && e.Seconds>=0)
        .GroupBy(e=>EventLabel(e.Label),StringComparer.OrdinalIgnoreCase)
        .Select(g=>new EventTag(g.Key,g.Count())).ToArray();
}
