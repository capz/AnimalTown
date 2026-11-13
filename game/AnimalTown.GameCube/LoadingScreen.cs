using System;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Timing;

// A small, independent loading surface: just the title's moving dots and a
// hopping sprout. It is retired before gameplay needs its native memory.
internal sealed class LoadingScreen : IDisposable
{
    private static LoadingScreen? current;
    private readonly Draw2D ui=new Draw2D(GraphicsDevice.Default,new Draw2DOptions{Width=640,Height=480,VertexCapacity=1024,SpriteCapacity=16});
    private readonly TitleBackdrop background=new TitleBackdrop();
    private readonly bool performance=DebugOverlay.PerformanceVisible,text=DebugOverlay.TextVisible;
    internal static void Draw(string? error=null,UiFont? font=null)
    {
        if(current==null)current=new LoadingScreen();
        current.Frame(error,font);
    }
    internal static void Pulse(){if(current!=null&&!GraphicsDevice.Default.IsFrameActive)current.Frame(null,null);}
    internal static void Finish()
    {
        if(current==null)return;
        current.Dispose();current=null;
    }
    private void Frame(string? error,UiFont? font)
    {
        DebugOverlay.PerformanceVisible=false;DebugOverlay.TextVisible=false;
        var device=GraphicsDevice.Default;UiNoise.Prepare();device.BeginFrame(TitleBackdrop.Color);ui.Begin();background.Draw(ui);
        Sprout(Clock.Timestamp);
        if(error!=null&&font!=null){TitleArt.Panel(ui,90,182,460,116);font.Draw(ui,error,112,209,TitleArt.Ink,.9f);}
        ui.End();device.EndFrame();
    }
    private void Sprout(long now)
    {
        float phase=(float)(now%1200000)/1200000,hop=(float)Math.Sin(phase*3.1415927f);
        float x=576,y=410-8*hop;
        TitleArt.Ellipse(ui,x,427,14-3*hop,2,new Color32(49,72,62,65));
        for(int i=0;i<3;i++)TitleArt.Ellipse(ui,538+i*11,426,1.5f,1.5f,new Color32(53,105,84,(byte)(((now/200000+i)%3==0)?170:60)));
        TitleArt.Ellipse(ui,x,y,10,12,TitleArt.Ink);TitleArt.Ellipse(ui,x,y-1,8,10,new Color32(232,183,86));
        ui.FillRectangle(new RectF(x-1,y-23,2,13),TitleArt.Leaf);
        ui.FillTriangle(new Vector2(x,y-18),new Vector2(x-14,y-28),new Vector2(x-2,y-28),TitleArt.Leaf);
        ui.FillTriangle(new Vector2(x,y-19),new Vector2(x+3,y-31),new Vector2(x+14,y-28),TitleArt.Leaf);
        float eye=now%3200000>3000000?.35f:1.2f;
        TitleArt.Ellipse(ui,x-3,y-2,1.2f,eye,TitleArt.Ink);TitleArt.Ellipse(ui,x+3,y-2,1.2f,eye,TitleArt.Ink);
        ui.FillTriangle(new Vector2(x-2,y+3),new Vector2(x+2,y+3),new Vector2(x,y+5),TitleArt.Ink);
    }
    public void Dispose(){ui.Dispose();background.Dispose();DebugOverlay.PerformanceVisible=performance;DebugOverlay.TextVisible=text;}
}
