using System;
using Dolphin.Graphics;

// Native geometry is built/evicted between frames. Misses use the original
// immediate path for one frame; dimensions, colors and painter order are exact.
internal sealed class UiShapes
{
    private readonly UiShapePlan plan=new UiShapePlan();
    private readonly Geometry2D?[] geometry=new Geometry2D?[UiShapePlan.Capacity];
    private int bytes;
    internal int ResidentBytes=>bytes;
    internal void Prepare(string view,int tab,bool enabled)
    {
        plan.BeginFrame(view,tab,enabled);
        if(!enabled||!plan.HasWork)return;
        if(plan.TakeOverflow())for(int n=0;n<8;n++){int oldest=plan.Oldest(true);if(oldest<0)break;Release(oldest);}
        for(int slot=plan.TakePending();slot>=0;slot=plan.TakePending()){
            while(bytes+10000>384*1024){int oldest=plan.Oldest();if(oldest<0)break;Release(oldest);}
            var built=Build(plan.Widths[slot],plan.Heights[slot],plan.Radii[slot],plan.Bulges[slot]);
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
