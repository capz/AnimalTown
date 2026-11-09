namespace AnimalTown;

// Logical 640x480 UI coordinates; projection uses the actual curved world and
// the same camera basis as rendering. No matrix inversion or heap allocation.
public static class AttentionBubbleLayout
{
    public const float Width=208,BodyHeight=70,TabHeight=28,Height=92;
    public static bool Project(Town town,float x,float z,float focusX,float focusZ,float focusHeight,out float screenX,out float screenY)
    {
        float h=GroundSurface.HeightAt(town,(int)x,(int)z,x,z)+1.9f;
        var p=CylinderWorld.Surface(z,focusZ,h);
        float dx=x-focusX,dy=p.Y-CylinderWorld.CameraHeight-focusHeight,dz=p.Z-focusZ-CylinderWorld.CameraBack;
        float depth=dy*RenderView.ForwardY+dz*RenderView.ForwardZ;
        screenX=screenY=0;if(depth<=CylinderWorld.Near||depth>=CylinderWorld.Far)return false;
        float up=-dy*RenderView.ForwardZ+dz*RenderView.ForwardY;
        screenX=320+dx/depth/RenderView.TanX*320;screenY=240-up/depth/RenderView.TanY*240;
        return screenX>=24&&screenX<=616&&screenY>=56&&screenY<=350;
    }
    public static bool Place(float headX,float headY,bool occupied,float occupiedX,float occupiedY,out float x,out float y)
    {
        float baseX=Math.Max(18,Math.Min(622-Width,headX-Width*.5f)),baseY=Math.Max(22,Math.Min(300-Height,headY-Height-12));
        x=baseX;y=baseY;
        for(int choice=0;choice<5;choice++){
            x=baseX;y=baseY;
            if(choice==1)x=headX>=320?headX-Width-28:headX+28;
            if(choice==2)x=headX>=320?headX+28:headX-Width-28;
            if(choice==3)y=occupiedY-Height-12;
            if(choice==4)y=occupiedY+Height+12;
            if(choice>=3&&!occupied)continue;
            if(x<18||x+Width>622||y<22||y+Height>300)continue;
            if(x<348&&x+Width>292&&y<178&&y+Height>118)continue;
            if(headX<x-36||headX>x+Width+36||headY>y+Height+72)continue;
            // Near the horizon, put the bubble beside its speaker. Clamping
            // an overhead bubble down must never cover the speaker's face.
            if(headY<y+Height+8&&headX>x-12&&headX<x+Width+12)continue;
            if(!occupied||!Overlap(x,y,occupiedX,occupiedY))return true;
        }
        return false;
    }
    private static bool Overlap(float x,float y,float ox,float oy)=>x<ox+Width+10&&x+Width+10>ox&&y<oy+Height+10&&y+Height+10>oy;
}
