using System;
using AnimalTown;
using Dolphin.Graphics;

// Uses the already resident IPL font. Only a small outlined badge is retained;
// the previous filled clock panel and its blank right-hand space are gone.
internal sealed class FloatingClockHud : IDisposable
{
    private const float Left=32,Bottom=430,TimeScale=1.8f,DateScale=.82f,Gap=7;
    private static readonly Color32 Shadow=new Color32(18,35,27,150);
    private readonly UiFont font;
    private Geometry2D? badge;
    private string time="",date="",day="";
    private RectF timeBounds,dateBounds,dayBounds;
    private float width,rowHeight,badgeWidth,badgeX,rowY,ruleY,timeY;
    internal FloatingClockHud(UiFont font){this.font=font;}
    internal void Trim(){badge?.Dispose();badge=null;day="";}
    public void Dispose()=>Trim();
    internal void Prepare(TownClock clock)
    {
        if(time==clock.Time&&date==clock.MonthDay&&day==clock.Weekday)return;
        bool dateChanged=date!=clock.MonthDay||day!=clock.Weekday;
        time=clock.Time;date=clock.MonthDay;day=clock.Weekday;
        timeBounds=font.GlyphBounds(time,TimeScale);
        if(dateChanged){
            dateBounds=font.GlyphBounds(date,DateScale);dayBounds=font.GlyphBounds(day,DateScale);
            float nextHeight=Math.Max(dateBounds.Height,dayBounds.Height)+6,nextWidth=dayBounds.Width+12;
            if(badge==null||rowHeight!=nextHeight||badgeWidth!=nextWidth){
                var builder=new Geometry2DBuilder(216);
                Outline(builder,nextWidth,nextHeight,1,1,Shadow);
                Outline(builder,nextWidth,nextHeight,0,0,Color32.White);
                var replacement=builder.Build();badge?.Dispose();badge=replacement;
            }
            rowHeight=nextHeight;badgeWidth=nextWidth;
        }
        badgeX=Left+dateBounds.Width+Gap;
        width=Math.Max(timeBounds.Width+.6f,dateBounds.Width+Gap+badgeWidth);
        rowY=Bottom-rowHeight;ruleY=rowY-6;timeY=ruleY-5-timeBounds.Height;
    }
    private static void Outline(Geometry2DBuilder b,float width,float height,float x,float y,Color32 color)
    {
        const float thickness=1.25f;
        float r=height*.5f,inner=r-thickness;
        b.AddRectangle(new RectF(x+r,y,width-height,thickness),color);
        b.AddRectangle(new RectF(x+r,y+height-thickness,width-height,thickness),color);
        for(int side=0;side<2;side++)for(int i=0;i<8;i++){
            float a=(side==0?.5f:-.5f)*(float)Math.PI+i*(float)Math.PI/8,c=a+(float)Math.PI/8;
            float cx=x+(side==0?r:width-r),cy=y+r;
            var outerA=new Vector2(cx+r*(float)Math.Cos(a),cy+r*(float)Math.Sin(a));
            var outerB=new Vector2(cx+r*(float)Math.Cos(c),cy+r*(float)Math.Sin(c));
            var innerA=new Vector2(cx+inner*(float)Math.Cos(a),cy+inner*(float)Math.Sin(a));
            var innerB=new Vector2(cx+inner*(float)Math.Cos(c),cy+inner*(float)Math.Sin(c));
            b.AddTriangle(outerA,outerB,innerB,color);b.AddTriangle(outerA,innerB,innerA,color);
        }
    }
    private void Label(Draw2D ui,string text,float x,float y,float scale,float weight=.25f)
    {font.Draw(ui,text,x+1,y+1,Shadow,scale);font.Draw(ui,text,x+weight,y,Color32.White,scale);font.Draw(ui,text,x,y,Color32.White,scale);}
    internal void Draw(Draw2D ui)
    {
        Label(ui,time,Left-timeBounds.X,timeY-timeBounds.Y,TimeScale,.6f);
        ui.FillRectangle(new RectF(Left+1,ruleY+1,width,1.5f),Shadow);
        ui.FillRectangle(new RectF(Left,ruleY,width,1.5f),Color32.White);
        Label(ui,date,Left-dateBounds.X,rowY+(rowHeight-dateBounds.Height)*.5f-dateBounds.Y,DateScale);
        if(badge!=null)ui.DrawGeometry(badge,new Vector2(badgeX,rowY),Color32.White);
        Label(ui,day,badgeX+6-dayBounds.X,rowY+(rowHeight-dayBounds.Height)*.5f-dayBounds.Y,DateScale);
    }
}
