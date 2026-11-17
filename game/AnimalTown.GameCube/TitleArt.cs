using System;
using Dolphin.Graphics;

// Shared cream panels and the approved full-colour Smalltown Forest logo.
internal static class TitleArt
{
    internal static readonly Color32 Ink=new Color32(49,72,62),Cream=new Color32(255,245,221),Leaf=new Color32(53,105,84);
    internal static void Rounded(Draw2D ui,float x,float y,float w,float h,float radius,Color32 color)
    {UiNoise.Begin();
        if(w>radius*2)ui.FillRectangle(new RectF(x+radius,y,w-radius*2,h),color);
        if(h>radius*2){ui.FillRectangle(new RectF(x,y+radius,radius,h-radius*2),color);ui.FillRectangle(new RectF(x+w-radius,y+radius,radius,h-radius*2),color);}
        for(int corner=0;corner<4;corner++){
            float cx=x+(corner==0||corner==3?radius:w-radius),cy=y+(corner<2?radius:h-radius);
            float start=corner==0?3.1415927f:corner==1?4.712389f:corner==2?0:1.5707964f;
            for(int i=0;i<10;i++){float a=start+i*.15707964f,b=a+.15707964f;ui.FillTriangle(new Vector2(cx,cy),new Vector2(cx+radius*(float)Math.Cos(a),cy+radius*(float)Math.Sin(a)),new Vector2(cx+radius*(float)Math.Cos(b),cy+radius*(float)Math.Sin(b)),color);}
        }
    UiNoise.End();}
    private static void Bulged(Draw2D ui,float x,float y,float w,float h,Color32 color)
    {UiNoise.Begin();
        const float radius=30;Rounded(ui,x,y,w,h,radius,color);
        for(int side=0;side<2;side++){
            float edge=side==0?x:x+w,direction=side==0?-1:1,span=h-2*radius;
            for(int i=0;i<20;i++){float a=i/20f,b=(i+1)/20f;ui.FillTriangle(new Vector2(edge-direction*2,y+h*.5f),new Vector2(edge+direction*9*(float)Math.Sin(a*3.1415927f),y+radius+a*span),new Vector2(edge+direction*9*(float)Math.Sin(b*3.1415927f),y+radius+b*span),color);}
        }
    UiNoise.End();}
    internal static void Panel(Draw2D ui,float x,float y,float w,float h)
    {UiNoise.Begin();Bulged(ui,x+4,y+5,w,h,new Color32(49,72,62,130));Bulged(ui,x,y,w,h,Cream);UiNoise.End();}
    internal static void Logo(Draw2D ui,CachedTexture logo,RectF bounds)
    {
        // The old wordmark was a tinted 4:1 alpha mask. Preserve the new
        // artwork's aspect ratio and RGB colours in every shared header.
        float scale=Math.Min(bounds.Width/logo.SourceWidth,bounds.Height/logo.SourceHeight);
        float width=logo.SourceWidth*scale,height=logo.SourceHeight*scale;
        logo.Draw(ui,new RectF(0,0,logo.SourceWidth,logo.SourceHeight),
            new RectF(bounds.X+(bounds.Width-width)*.5f,bounds.Y+(bounds.Height-height)*.5f,width,height),Color32.White);
    }
    internal static void Ellipse(Draw2D ui,float x,float y,float rx,float ry,Color32 color)
    {UiNoise.Begin();
        for(int i=0;i<12;i++){float a=i*.5235988f,b=a+.5235988f;ui.FillTriangle(new Vector2(x,y),new Vector2(x+rx*(float)Math.Cos(a),y+ry*(float)Math.Sin(a)),new Vector2(x+rx*(float)Math.Cos(b),y+ry*(float)Math.Sin(b)),color);}
    UiNoise.End();}
}
