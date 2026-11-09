using AnimalTown;

internal static class DebugWeatherChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var weather=new DebugWeather();
        check(!weather.Raining(0)&&!weather.RoughOcean(0),"new sessions never enable weather previews");
        weather.RoughOceanWhenRaining=true;
        check(!weather.RoughOcean(0),"rough ocean requires rain even when opted in");
        weather.RainEnabled=true;
        check(weather.Raining(0)&&weather.RoughOcean(0),"both explicit debug options enable rainy ocean animation");
        for(int room=1;room<=21;room++)check(!weather.Raining(room)&&!weather.RoughOcean(room),"indoors suppress rain and storm-water work");
        weather.RainEnabled=false;check(!weather.RoughOcean(0),"stopping rain immediately restores calm ocean");
        weather.RainEnabled=true;weather.RoughOceanWhenRaining=false;
        check(weather.Raining(0)&&!weather.RoughOcean(0),"rain can be previewed with calm water");
        for(int i=0;i<DebugWeather.DropCount;i++)foreach(double time in new[]{-1e8,0,1.0,86400,1e8}){
            DebugWeather.Drop(i,80,80,time,out float x,out float z,out float h);
            check(float.IsFinite(x)&&float.IsFinite(z)&&h>=0&&h<=7&&x>=66&&x<=94&&z>=60&&z<=90,"rain stays in a fixed bounded camera neighborhood at long/negative times");
            DebugWeather.Drop(i,80.1f,80.1f,time,out float nx,out float nz,out float nh);
            check(x==nx&&z==nz&&h==nh,"rain is world anchored between grid crossings, not dragged by the camera");
        }
        DebugWeather.Drop(36,80,80,1,out float ax,out float az,out float ah);
        DebugWeather.Drop(35,83,80,1,out float bx,out float bz,out float bh);
        check(ax==bx&&az==bz&&ah==bh,"retained rain cells keep the same phase across a scrolling grid boundary");
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(int n=0;n<1000;n++)for(int i=0;i<DebugWeather.DropCount;i++)DebugWeather.Drop(i,80,80,n*.016,out _,out _,out _);
        check(GC.GetAllocatedBytesForCurrentThread()==before,"rain selection has no per-frame managed allocation");
        var whitecaps=WaterSurface.StormTexture();int at=0,crests=0;
        check(whitecaps.Length==4096,"storm whitecaps add one shared four KiB mask");
        for(int by=0;by<64;by+=4)for(int bx0=0;bx0<64;bx0+=8)for(int y=by;y<by+4;y++)for(int x=bx0;x<bx0+8;x++){
            int sample=whitecaps[at++];
            check(sample==WaterSurface.StormSample(x,y)&&sample==WaterSurface.StormSample(x-64,y+64),"whitecap texture repeats without a tile seam and uses GX intensity packing");
            if(sample>72)crests++;
        }
        check(crests>0&&crests<4096/4,"storm crests are stronger than calm ripples and leave open water visible");
    }
}
