using System;
using Dolphin.Graphics;

// Native geometry is built/evicted between frames. Misses use the original
// immediate path for one frame; dimensions, colors and painter order are exact.
internal sealed class UiShapes
{
    private sealed class Entry
    {
        internal float Width,Height,Radius;
        internal bool Bulge;
        internal int LastUse;
        internal Geometry2D? Geometry;
    }
    private readonly Entry[] entries=new Entry[64];
    private int frame,bytes;
    private bool overflow;
    internal int ResidentBytes=>bytes;
    internal UiShapes(){for(int i=0;i<entries.Length;i++)entries[i]=new Entry();}
    internal void Prepare()
    {
        frame++;
        if(overflow){for(int n=0;n<8;n++){int oldest=-1;for(int i=0;i<entries.Length;i++)if(entries[i].LastUse>0&&entries[i].LastUse<frame&&(oldest<0||entries[i].LastUse<entries[oldest].LastUse))oldest=i;if(oldest<0)break;Release(entries[oldest]);entries[oldest].LastUse=0;}overflow=false;}
        for(int i=0;i<entries.Length;i++){
            var e=entries[i];if(e.LastUse==0||e.Geometry!=null)continue;
            while(bytes+10000>384*1024){int oldest=-1;for(int j=0;j<entries.Length;j++)if(entries[j].Geometry!=null&&(oldest<0||entries[j].LastUse<entries[oldest].LastUse))oldest=j;
                if(oldest<0)break;Release(entries[oldest]);entries[oldest].LastUse=0;}
            e.Geometry=Build(e.Width,e.Height,e.Radius,e.Bulge);bytes+=e.Geometry.NativeBytes;
        }
    }
    private void Release(Entry e){if(e.Geometry!=null){bytes-=e.Geometry.NativeBytes;e.Geometry.Dispose();e.Geometry=null;}}
    internal bool Draw(Draw2D ui,float x,float y,float w,float h,float radius,bool bulge,Color32 color)
    {
        bulge=bulge&&h>radius*2+1;
        int empty=-1;
        for(int i=0;i<entries.Length;i++){
            var e=entries[i];if(e.LastUse==0){if(empty<0)empty=i;continue;}
            if(e.Width!=w||e.Height!=h||e.Radius!=radius||e.Bulge!=bulge)continue;
            e.LastUse=frame+1;if(e.Geometry==null)return false;
            ui.DrawGeometry(e.Geometry,new Vector2(x,y),color);return true;
        }
        // Never release a native resource from inside a render pass.
        if(empty>=0){var e=entries[empty];e.Width=w;e.Height=h;e.Radius=radius;e.Bulge=bulge;e.LastUse=frame+1;}else overflow=true;
        return false;
    }
    private static Geometry2D Build(float w,float h,float radius,bool bulge)
    {
        var b=new Geometry2DBuilder(258);var white=Color32.White;
        if(w>radius*2)b.AddRectangle(new RectF(radius,0,w-radius*2,h),white);
        if(h>radius*2){b.AddRectangle(new RectF(0,radius,radius,h-radius*2),white);b.AddRectangle(new RectF(w-radius,radius,radius,h-radius*2),white);}
        for(int corner=0;corner<4;corner++){
            float cx=corner==0||corner==3?radius:w-radius,cy=corner<2?radius:h-radius;
            float start=corner==0?3.1415927f:corner==1?4.712389f:corner==2?0:1.5707964f;
            for(int i=0;i<10;i++){float a=start+i*.15707964f,c=a+.15707964f;b.AddTriangle(new Vector2(cx,cy),new Vector2(cx+radius*(float)Math.Cos(a),cy+radius*(float)Math.Sin(a)),new Vector2(cx+radius*(float)Math.Cos(c),cy+radius*(float)Math.Sin(c)),white);}
        }
        if(bulge)for(int side=0;side<2;side++){
            float edge=side==0?0:w,direction=side==0?-1:1,span=h-radius*2;
            for(int i=0;i<20;i++){float a=i/20f,c=(i+1)/20f;b.AddTriangle(new Vector2(edge-direction*2,h*.5f),new Vector2(edge+direction*9*(float)Math.Sin(a*3.1415927f),radius+a*span),new Vector2(edge+direction*9*(float)Math.Sin(c*3.1415927f),radius+c*span),white);}
        }
        return b.Build();
    }
}
