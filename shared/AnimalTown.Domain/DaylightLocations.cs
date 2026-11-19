namespace AnimalTown;

public readonly struct DaylightLocation
{
    public readonly int Region;
    public readonly string City;
    internal readonly double Latitude,Longitude;
    internal readonly DaylightZone Zone;

    internal DaylightLocation(int region,string city,double latitude,double longitude,DaylightZone zone)
    {
        Region=region;
        City=city;
        Latitude=latitude;
        Longitude=longitude;
        Zone=zone;
    }
}

public static partial class DaylightLocations
{
    public static int RegionCount=>Regions.Length;
    public static string RegionName(int region)=>Regions[region];
    // Zero is Classic. The on-disk identifiers of the presets are one-based.
    public static DaylightLocation Get(int id)=>Cities[id-1];
}

internal readonly struct DaylightZone
{
    private readonly short[] offsets;
    private readonly int[] transitions;

    internal DaylightZone(short[] offsets,int[] transitions)
    {
        this.offsets=offsets;
        this.transitions=transitions;
    }

    internal int OffsetAt(int utcMinute)
    {
        int low=0,high=transitions.Length;
        while(low<high){
            int middle=(low+high)/2;
            if((transitions[middle]>>3)<=utcMinute)low=middle+1;
            else high=middle;
        }
        return offsets[low==0?0:transitions[low-1]&7];
    }

    internal int ToUtc(int localMinute,int expectedUtc=int.MinValue)
    {
        int best=int.MinValue,bestDistance=int.MaxValue;
        int afterGap=localMinute-offsets[0],smallestGap=int.MaxValue;
        foreach(int offset in offsets){
            int utc=localMinute-offset;
            int difference=OffsetAt(utc)-offset;
            if(difference==0){
                // During a repeated hour, follow the running clock when possible.
                // A cold start chooses the later occurrence deterministically.
                int distance=expectedUtc==int.MinValue?0:Math.Abs(utc-expectedUtc);
                if(distance<bestDistance||distance==bestDistance&&utc>best){
                    best=utc;
                    bestDistance=distance;
                }
            }else if(difference>0&&difference<smallestGap){
                // A skipped wall time is interpreted after the gap, keeping
                // the minutes within the hour. No valid instant maps to it.
                smallestGap=difference;
                afterGap=utc;
            }
        }
        return best==int.MinValue?afterGap:best;
    }
}
