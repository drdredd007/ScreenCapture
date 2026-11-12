namespace Moments;
internal static class LibraryLayout
{
    public static double TileWidth(double available)=>Math.Max(120,Math.Max(120,available)/Math.Max(1,(int)(Math.Max(120,available)/270))-10);
    internal sealed record EventTag(string Label,int Count);
    public static EventTag[] EventTags(ClipEvents? clip)=>
        (clip?.Events??[]).Where(e=>e!=null && !string.IsNullOrWhiteSpace(e.Label))
        .Select(e=>e.Label.Trim() switch{
            "My kills"=>"Kill","My deaths"=>"Death","My assists"=>"Assist",
            "My dragon steals"=>"Dragon steal","My Baron steals"=>"Baron steal","My Herald steals"=>"Herald steal",
            var label=>label.Length>64?label[..64]:label})
        .GroupBy(label=>label,StringComparer.OrdinalIgnoreCase).Select(g=>new EventTag(g.Key,g.Count())).ToArray();
}
