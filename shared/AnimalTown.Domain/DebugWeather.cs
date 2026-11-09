namespace AnimalTown;

// Session-only preview. No clock, generator or save code can enable weather.
public sealed class DebugWeather
{
    public bool RainEnabled,RoughOceanWhenRaining;
    public bool Raining(int room)=>RainEnabled&&room==0;
    public bool RoughOcean(int room)=>Raining(room)&&RoughOceanWhenRaining;
    public const int DropCount=64;
    public static void Drop(int index,float focusX,float focusZ,double seconds,out float x,out float z,out float height)
    {
        int cx=(int)Math.Floor(focusX/3)-4+index%8,cz=(int)Math.Floor(focusZ/3)-6+index/8;
        uint hash=unchecked((uint)cx*73856093u^(uint)cz*19349663u);hash^=hash>>13;hash=unchecked(hash*1274126177u);
        double phase=seconds*1.6+((hash>>16)&65535)/65536.0;phase-=Math.Floor(phase);
        x=cx*3+(hash&255)/85f+(float)phase*.4f;
        z=cz*3+((hash>>8)&255)/85f;height=(float)(1-phase)*7;
    }
}
