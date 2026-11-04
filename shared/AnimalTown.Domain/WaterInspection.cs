namespace AnimalTown;

// Read-only developer camera tour of the actual town. Program restores player
// coordinates in a finally block after drawing; no terrain or save is changed.
public static class WaterInspection
{
    public static void Focus(GameState state,int scene)
    {
        var t=state.Town;
        for(int z=4;z<Town.Height-4;z++)for(int x=4;x<Town.Width-4;x++){
            int kind=t.At(x,z);bool match=false;
            if(scene==1)match=kind==1&&t.At(x+1,z)==0&&t.At(x,z+1)==1;
            if(scene==2)match=kind==1&&WaterSurface.IsBank(t,x-1,z)&&WaterSurface.IsBank(t,x,z-1);
            if(scene==3)match=kind==(int)Terrain.Bridge;
            if(scene==4)match=kind==(int)Terrain.CoastSouth;
            if(scene==5)match=kind==1&&WaterTerrain.IsSea(t,x,z+1);
            if(match){state.X=x+.5f;state.Z=z+6.5f;return;}
        }
    }
}
