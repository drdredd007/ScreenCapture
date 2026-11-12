namespace Moments;
internal static class LibraryLayout
{
    public static double TileWidth(double available)=>Math.Max(120,Math.Max(120,available)/Math.Max(1,(int)(Math.Max(120,available)/270))-10);
}
