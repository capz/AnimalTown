namespace AnimalTown;

// Fixed clock keyframes; no textures, save fields, or per-frame allocations.
public static class WorldLighting
{
    private static int Blend(int a,int b,float t)=>(int)(a+(b-a)*t+.5f);
    private static int Between(int a,int b,int minute,int start,int end)
    {
        float t=(minute-start)/(float)(end-start);t=t*t*(3-2*t);
        return Blend(a>>16,b>>16,t)<<16|Blend(a>>8&255,b>>8&255,t)<<8|Blend(a&255,b&255,t);
    }
    public static int Tint(int minute)
    {
        minute=(minute%1440+1440)%1440;
        if(minute<300||minute>=1200)return 0x596c91;
        if(minute<390)return Between(0x596c91,0xffac71,minute,300,390);
        if(minute<480)return Between(0xffac71,0xffffff,minute,390,480);
        if(minute<990)return 0xffffff;
        if(minute<1110)return Between(0xffffff,0xff9860,minute,990,1110);
        return Between(0xff9860,0x596c91,minute,1110,1200);
    }
    public static int Lamps(int minute)
    {
        minute=(minute%1440+1440)%1440;
        if(minute<330||minute>=1170)return 255;
        if(minute<450)return 255-(minute-330)*255/120;
        if(minute<1020)return 0;
        return (minute-1020)*255/150;
    }
    // One original lamp beside each player home and the two public buildings.
    public static bool StreetLamp(GameState state,int building,out float x,out float z)
    {
        BuildingWorld.Entrance(state,building,out x,out z);
        if(!BuildingWorld.Exists(state,building)||(BuildingWorld.BuildingProfile(building)<0&&building!=7&&building!=8))return false;
        float doorX=x,doorZ=z,height=TerrainElevation.At(state.Town,x,z);z+=.9f;
        x=doorX+2.2f;
        if(state.CanWalk(x,z,0)&&TerrainElevation.At(state.Town,x,z)==height)return true;
        x=doorX-2.2f;
        return state.CanWalk(x,z,0)&&TerrainElevation.At(state.Town,x,z)==TerrainElevation.At(state.Town,doorX,doorZ);
    }
}
