using System;
using AnimalTown;
using Dolphin.Graphics;

// Optional rough-water patch. It is *not* part of the ARAM terrain archive.
// All ordinary/shoreline water remains immutable. Only these 289 vertices are
// updated while a rough-sea scene is actually within view.
internal sealed class LocalOceanWaves : IDisposable
{
    private readonly VertexPositionColorTexture[] vertices=new VertexPositionColorTexture[(WindWakerWaveField.Cells+1)*(WindWakerWaveField.Cells+1)];
    private readonly ushort[] indices=new ushort[WindWakerWaveField.Cells*WindWakerWaveField.Cells*6];
    private readonly CylinderRows rows=new CylinderRows();
    private DynamicMesh? mesh;
    private bool active;
    private int x0,z0;
    private Color32 tint;
    internal bool Active=>active;
    internal int Triangles=>active?indices.Length/3:0;
    internal LocalOceanWaves()
    {
        int width=WindWakerWaveField.Cells+1;
        for(int z=0;z<WindWakerWaveField.Cells;z++)for(int x=0;x<WindWakerWaveField.Cells;x++){
            int at=(z*WindWakerWaveField.Cells+x)*6,a=z*width+x,b=a+width;
            indices[at++]=(ushort)a;indices[at++]=(ushort)(a+1);indices[at++]=(ushort)(b+1);
            indices[at++]=(ushort)a;indices[at++]=(ushort)(b+1);indices[at]=(ushort)b;
        }
    }
    internal void Prepare(GameState state,bool rough,bool gallery,Color32 color)
    {
        active=false;
        if(!rough||gallery||state.Z<WaterTerrain.SeaStart-22)return;
        int centerX=(int)Math.Floor(state.X/2)*2;
        int centerZ=Math.Max(WaterTerrain.SeaStart+6,(int)Math.Floor((state.Z+6)/2)*2);
        x0=centerX-(int)(WindWakerWaveField.Extent*.5f);
        z0=centerZ-(int)(WindWakerWaveField.Extent*.5f);
        // A wave patch must never overlap land, beach, river or cliff geometry.
        // One check per covered logical sea cell is cheap, only in storm mode.
        for(int z=z0;z<z0+WindWakerWaveField.Extent;z++)
            for(int x=x0;x<x0+WindWakerWaveField.Extent;x++)
                if(!WaterTerrain.IsSea(state.Town,x,z))return;
        tint=color;active=true;
    }
    internal void Update(double seconds)
    {
        if(!active)return;
        const int width=WindWakerWaveField.Cells+1;
        for(int z=0;z<width;z++)
        {
            float worldZ=z0+z*WindWakerWaveField.CellSize;
            var chord=rows.Chord(worldZ,WaterSurface.Height);
            for(int x=0;x<width;x++)
            {
                float worldX=x0+x*WindWakerWaveField.CellSize;
                float y=WindWakerWaveField.Height(worldX,worldZ,seconds,WindWakerWaveField.EdgeWeight(x,z));
                var position=new Vector3(worldX,chord.Y+chord.UpY*y,chord.Z+chord.UpZ*y);
                vertices[z*width+x]=new VertexPositionColorTexture(position,tint,
                    new Vector2(worldX*WaterSurface.UvScale,worldZ*WaterSurface.UvScale));
            }
        }
        if(mesh==null)mesh=DynamicMesh.Create(vertices,indices);
        else mesh.SetVertices(vertices);
    }
    internal void Draw(Renderer renderer,Material water,Transform scroll)
    {
        if(active&&mesh!=null)renderer.DrawMesh(mesh,water,scroll);
    }
    public void Dispose(){mesh?.Dispose();mesh=null;}
}
