using System;
using Dolphin.Graphics;

// Native geometry is built/evicted between frames. Misses use the original
// immediate path for one frame; dimensions, colors and painter order are exact.
internal sealed class UiShapes : IDisposable
{
    private readonly UiShapePlan plan=new UiShapePlan();
    private readonly Geometry2D?[] geometry=new Geometry2D?[UiShapePlan.Capacity];
    private int bytes;
    internal int ResidentBytes=>bytes;
    public void Dispose(){for(int i=0;i<geometry.Length;i++)if(geometry[i]!=null)Release(i);}
    internal void Prepare(string view,int tab,bool enabled)
    {
        plan.BeginFrame(view,tab,enabled);
        if(!enabled||!plan.HasWork)return;
        if(plan.TakeOverflow())for(int n=0;n<8;n++){int oldest=plan.Oldest(true);if(oldest<0)break;Release(oldest);}
        for(int slot=plan.TakePending();slot>=0;slot=plan.TakePending()){
            while(bytes+(plan.Panels[slot]?20000:10000)>384*1024){int oldest=plan.Oldest();if(oldest<0)break;Release(oldest);}
            var built=Build(plan.Widths[slot],plan.Heights[slot],plan.Radii[slot],plan.Bulges[slot],plan.Panels[slot]);
            geometry[slot]=built;bytes+=built.NativeBytes;plan.Complete(slot);
        }
    }
    private void Release(int slot){var cached=geometry[slot]!;bytes-=cached.NativeBytes;cached.Dispose();geometry[slot]=null;plan.Remove(slot);}
    internal bool Draw(Draw2D ui,float x,float y,float w,float h,float radius,bool bulge,Color32 color)
    {
        bulge=bulge&&h>radius*2+1;
        int slot=plan.Request(w,h,radius,bulge);
        if(slot<0||geometry[slot]==null)return false;
        ui.DrawGeometry(geometry[slot]!,new Vector2(x,y),color);return true;
    }
    internal bool DrawPanel(Draw2D ui,float x,float y,float w,float h,float radius)
    {
        int slot=plan.Request(w,h,radius,h>radius*2+1,true);
        if(slot<0||geometry[slot]==null)return false;
        ui.DrawGeometry(geometry[slot]!,new Vector2(x,y),Color32.White);return true;
    }
    private static Geometry2D Build(float w,float h,float radius,bool bulge,bool panel)
    {
        var b=new Geometry2DBuilder(panel?516:258);
        if(panel){Append(b,w,h,radius,bulge,4,5,new Color32(49,72,62,130));Append(b,w,h,radius,bulge,0,0,new Color32(255,245,221));}
        else Append(b,w,h,radius,bulge,0,0,Color32.White);
        return b.Build();
    }
    private static void Append(Geometry2DBuilder b,float w,float h,float radius,bool bulge,float x,float y,Color32 color)
    {
        if(w>radius*2)b.AddRectangle(new RectF(x+radius,y,w-radius*2,h),color);
        if(h>radius*2){b.AddRectangle(new RectF(x,y+radius,radius,h-radius*2),color);b.AddRectangle(new RectF(x+w-radius,y+radius,radius,h-radius*2),color);}
        for(int corner=0;corner<4;corner++){
            float cx=x+(corner==0||corner==3?radius:w-radius),cy=y+(corner<2?radius:h-radius);
            float start=corner==0?3.1415927f:corner==1?4.712389f:corner==2?0:1.5707964f;
            for(int i=0;i<10;i++){float a=start+i*.15707964f,c=a+.15707964f;b.AddTriangle(new Vector2(cx,cy),new Vector2(cx+radius*(float)Math.Cos(a),cy+radius*(float)Math.Sin(a)),new Vector2(cx+radius*(float)Math.Cos(c),cy+radius*(float)Math.Sin(c)),color);}
        }
        if(bulge)for(int side=0;side<2;side++){
            float edge=x+(side==0?0:w),direction=side==0?-1:1,span=h-radius*2;
            for(int i=0;i<20;i++){float a=i/20f,c=(i+1)/20f;b.AddTriangle(new Vector2(edge-direction*2,y+h*.5f),new Vector2(edge+direction*9*(float)Math.Sin(a*3.1415927f),y+radius+a*span),new Vector2(edge+direction*9*(float)Math.Sin(c*3.1415927f),y+radius+c*span),color);}
        }
    }
}
