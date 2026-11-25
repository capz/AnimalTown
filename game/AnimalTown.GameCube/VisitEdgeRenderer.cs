using System;
using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

internal sealed class VisitEdgeRenderer
{
    private static readonly Color32[] Colors={
        Color32.Transparent,new Color32(50,133,255),new Color32(51,220,99),
        new Color32(255,150,26),new Color32(255,55,53)
    };
    private VertexPositionColor[]? lines;
    private int count;
    internal void Prepare(bool visible)
    {
        if(!visible)lines=null;
        else if(lines==null)lines=new VertexPositionColor[768];
    }
    internal void Draw(Renderer renderer,Transform transform,GameState state,bool snow)
    {
        if(lines==null)return;
        var town=state.Town;
        int left=Math.Max(0,(int)state.X-CylinderWorld.ViewHalfWidth),right=Math.Min(Town.Width,(int)state.X+CylinderWorld.ViewHalfWidth+1);
        int top=Math.Max(0,(int)state.Z-TerrainTiles.GroundRowsAhead),bottom=Math.Min(Town.Height,(int)state.Z+TerrainTiles.GroundRowsBehind+1);
        for(int z=top;z<bottom;z++)for(int x=left;x<right;x++){
            int level=TileTraffic.VisitEdgeLevel(town.Traffic.Visits[z*Town.Width+x]);
            if(level==0)continue;
            if(x+1<right)Add(renderer,transform,state,x,z,x+1,z,level,snow);
            if(z+1<bottom)Add(renderer,transform,state,x,z,x,z+1,level,snow);
        }
        Flush(renderer,transform);
    }
    private void Add(Renderer renderer,Transform transform,GameState state,int ax,int az,int bx,int bz,int level,bool snow)
    {
        var town=state.Town;
        int other=TileTraffic.VisitEdgeLevel(town.Traffic.Visits[bz*Town.Width+bx]);
        if(other==0)return;
        lines![count++]=new VertexPositionColor(Center(state,ax,az,snow),Colors[level]);
        lines[count++]=new VertexPositionColor(Center(state,bx,bz,snow),Colors[other]);
        if(count==lines.Length)Flush(renderer,transform);
    }
    private static Vector3 Center(GameState state,int x,int z,bool snow)
    {
        float cx=x+.5f,cz=z+.5f,height=TerrainElevation.At(state.Town,cx,cz)+.035f;
        if(snow&&state.Town.Traffic.Winter&&SnowWear.Covers(state.Town,x,z)&&state.Town.Paths.At(x,z)==0&&state.Town.Traffic.Snow!.CenterCovered(x,z))
            height+=TerrainRenderer.SnowHeight;
        var surface=CylinderWorld.Surface(cz,state.Z,height);
        return new Vector3(cx,surface.Y,surface.Z);
    }
    private void Flush(Renderer renderer,Transform transform)
    {
        if(count==0)return;
        renderer.DrawPrimitives(lines!,0,count,PrimitiveType.Lines,transform);
        count=0;
    }
}
