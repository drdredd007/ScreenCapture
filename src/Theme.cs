using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using Forms=System.Windows.Forms;
using Color=System.Drawing.Color;
namespace Moments;
internal static class Theme
{
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(nint h,int attr,ref int value,int size);
    public static void Install()
    {
        EventManager.RegisterClassHandler(typeof(Window),FrameworkElement.LoadedEvent,new RoutedEventHandler((sender,e)=>{
            if(sender is not Window w || e.OriginalSource!=w)return;
            var h=new WindowInteropHelper(w).Handle;
            int dark=1,caption=0x221C17,text=0xF2F1ED,edge=0x443B33;
            DwmSetWindowAttribute(h,20,ref dark,4);DwmSetWindowAttribute(h,35,ref caption,4);
            DwmSetWindowAttribute(h,36,ref text,4);DwmSetWindowAttribute(h,34,ref edge,4);
        }));
    }
    public static Forms.ContextMenuStrip TrayMenu()=>new(){
        BackColor=Color.FromArgb(29,35,42),ForeColor=Color.FromArgb(237,241,242),ShowImageMargin=false,
        Renderer=new Forms.ToolStripProfessionalRenderer(new TrayColors()){RoundedEdges=false}
    };
    sealed class TrayColors : Forms.ProfessionalColorTable
    {
        public override Color ToolStripDropDownBackground=>Color.FromArgb(29,35,42);
        public override Color MenuItemSelected=>Color.FromArgb(49,66,56);
        public override Color MenuItemSelectedGradientBegin=>MenuItemSelected;
        public override Color MenuItemSelectedGradientEnd=>MenuItemSelected;
        public override Color MenuItemBorder=>Color.FromArgb(66,87,73);
        public override Color MenuBorder=>Color.FromArgb(60,70,79);
    }
}

