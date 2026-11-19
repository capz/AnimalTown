namespace AnimalTown;

public readonly struct OutdoorLighting
{
    public readonly int Tint,Sunlight,Lamps;
    internal OutdoorLighting(int tint,int sunlight,int lamps){Tint=tint;Sunlight=sunlight;Lamps=lamps;}
}

public static class WorldLighting
{
    // Geometric elevation thresholds expressed as sines. See USNO rise/set
    // definitions for the apparent horizon (-0.8333 degrees) and civil twilight.
    internal const double HorizonSine=-.01454389765158;
    private const double CivilTwilightSine=-.10452846326765,FullDaySine=.13917310096007;
    private const int NightTint=0x596c91,DawnTint=0xffac71,DayTint=0xffffff;
    private static int Blend(int a,int b,float t)=>(int)(a+(b-a)*t+.5f);
    private static int BlendTint(int a,int b,float t)=>Blend(a>>16,b>>16,t)<<16|Blend(a>>8&255,b>>8&255,t)<<8|Blend(a&255,b&255,t);
    private static int Between(int a,int b,int minute,int start,int end)
    {
        float t=(minute-start)/(float)(end-start);t=t*t*(3-2*t);
        return BlendTint(a,b,t);
    }
    private static float Fade(double height,double low,double high)
    {
        float t=(float)Math.Max(0,Math.Min(1,(height-low)/(high-low)));
        return t*t*(3-2*t);
    }
    internal static OutdoorLighting FromSolarElevation(double height)
    {
        int tint=height<HorizonSine?BlendTint(NightTint,DawnTint,Fade(height,CivilTwilightSine,HorizonSine)):
            BlendTint(DawnTint,DayTint,Fade(height,HorizonSine,FullDaySine));
        int sunlight=Blend(0,255,Fade(height,HorizonSine,FullDaySine));
        int lamps=Blend(255,0,Fade(height,CivilTwilightSine,FullDaySine));
        return new OutdoorLighting(tint,sunlight,lamps);
    }
    internal static OutdoorLighting Classic(int minute)
    {int lamps=Lamps(minute);return new OutdoorLighting(Tint(minute),255-lamps,lamps);}
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
    // Share the existing dawn (05:30-07:30) and dusk (17:00-19:30)
    // transitions. At full darkness no outdoor sun/cloud shadows remain.
    public static int Sunlight(int minute)=>255-Lamps(minute);
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
