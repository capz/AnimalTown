using System;
using Dolphin.Graphics;

// The original title's cream panels, tree emblem and warm accent colours.
internal static class TitleArt
{
    internal static readonly Color32 Ink=new Color32(49,72,62),Cream=new Color32(255,245,221),Leaf=new Color32(53,105,84);
    internal static void Rounded(Draw2D ui,float x,float y,float w,float h,float radius,Color32 color)
    {
        if(w>radius*2)ui.FillRectangle(new RectF(x+radius,y,w-radius*2,h),color);
        if(h>radius*2){ui.FillRectangle(new RectF(x,y+radius,radius,h-radius*2),color);ui.FillRectangle(new RectF(x+w-radius,y+radius,radius,h-radius*2),color);}
        for(int corner=0;corner<4;corner++){
            float cx=x+(corner==0||corner==3?radius:w-radius),cy=y+(corner<2?radius:h-radius);
            float start=corner==0?3.1415927f:corner==1?4.712389f:corner==2?0:1.5707964f;
            for(int i=0;i<10;i++){float a=start+i*.15707964f,b=a+.15707964f;ui.FillTriangle(new Vector2(cx,cy),new Vector2(cx+radius*(float)Math.Cos(a),cy+radius*(float)Math.Sin(a)),new Vector2(cx+radius*(float)Math.Cos(b),cy+radius*(float)Math.Sin(b)),color);}
        }
    }
    private static void Bulged(Draw2D ui,float x,float y,float w,float h,Color32 color)
    {
        const float radius=30;Rounded(ui,x,y,w,h,radius,color);
        for(int side=0;side<2;side++){
            float edge=side==0?x:x+w,direction=side==0?-1:1,span=h-2*radius;
            for(int i=0;i<20;i++){float a=i/20f,b=(i+1)/20f;ui.FillTriangle(new Vector2(edge-direction*2,y+h*.5f),new Vector2(edge+direction*9*(float)Math.Sin(a*3.1415927f),y+radius+a*span),new Vector2(edge+direction*9*(float)Math.Sin(b*3.1415927f),y+radius+b*span),color);}
        }
    }
    internal static void Panel(Draw2D ui,float x,float y,float w,float h)
    {Bulged(ui,x+4,y+5,w,h,new Color32(49,72,62,130));Bulged(ui,x,y,w,h,Cream);}
    internal static void BrandMark(Draw2D ui,float x,float y)
    {
        Rounded(ui,x-17,y-8,34,34,15,Leaf);Rounded(ui,x-6,y+5,12,23,6,Cream);
        Rounded(ui,x-22,y-21,17,10,5,new Color32(232,183,86));Rounded(ui,x+5,y-21,17,10,5,new Color32(232,183,86));
        Rounded(ui,x-30,y+29,60,4,2,new Color32(195,109,84));
    }
    internal static void Ellipse(Draw2D ui,float x,float y,float rx,float ry,Color32 color)
    {
        for(int i=0;i<12;i++){float a=i*.5235988f,b=a+.5235988f;ui.FillTriangle(new Vector2(x,y),new Vector2(x+rx*(float)Math.Cos(a),y+ry*(float)Math.Sin(a)),new Vector2(x+rx*(float)Math.Cos(b),y+ry*(float)Math.Sin(b)),color);}
    }
}
