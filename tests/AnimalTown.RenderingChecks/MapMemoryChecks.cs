using AnimalTown;
using Dolphin.Graphics;

internal static class MapMemoryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        GC.KeepAlive(new MapBackground());
        long before=GC.GetAllocatedBytesForCurrentThread();var map=new MapBackground();
        check(GC.GetAllocatedBytesForCurrentThread()-before<8192,"map construction retains silhouette geometry without a 64 KiB upload array");
        var town=new Town(7319);Array.Clear(town.Elevation);
        int[] kinds={(int)Terrain.Grass,(int)Terrain.Water,(int)Terrain.Bridge,(int)Terrain.Beach,(int)Terrain.Cliff,(int)Terrain.Plaza,(int)Terrain.Building,(int)Terrain.Sea,(int)Terrain.CoastNorth,(int)Terrain.CoastEast,(int)Terrain.CoastSouth,(int)Terrain.CoastWest};
        for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++)town.Tiles[z*Town.Width+x]=(byte)kinds[(x/5+z/7)%kinds.Length];
        int uploads=0;byte[]? pixels=null;
        Action<byte[]> upload=bytes=>{uploads++;pixels=bytes;};
        map.Prepare(town,upload);
        check(uploads==1&&pixels!=null&&pixels.Length==65536,"initial map emits one complete native RGBA upload");
        CheckPixels(check,town,pixels!);
        before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++){map.Prepare(town,upload);map.Prepare(town);}
        check(GC.GetAllocatedBytesForCurrentThread()==before&&uploads==1,"unchanged map frames allocate neither scratch nor production upload delegates");

        var previous=pixels;town.Tiles[1*Town.Width+1]=(byte)Terrain.Sea;town.NotifyTerrainChanged();
        before=GC.GetAllocatedBytesForCurrentThread();map.Prepare(town,upload);long refreshBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        check(uploads==2&&!ReferenceEquals(previous,pixels)&&refreshBytes>=65536&&refreshBytes<67000,"a dirty map allocates one bounded upload scratch and retains no previous source array");
        CheckPixels(check,town,pixels!);
        var second=new Town(781);Array.Fill(second.Tiles,(byte)Terrain.Grass);Array.Clear(second.Elevation);
        map.Prepare(second,upload);check(uploads==3,"switching town identity rebuilds a map even if revision numbers agree");
        CheckPixels(check,second,pixels!);

        second.Tiles[3*Town.Width+3]=(byte)Terrain.Water;second.NotifyTerrainChanged();
        int failures=0;bool threw=false;
        try{map.Prepare(second,bytes=>{failures++;throw new InvalidOperationException("fixture upload failed");});}catch(InvalidOperationException){threw=true;}
        check(threw&&failures==1,"map upload failure propagates without publishing stale content as current");
        map.Prepare(second,upload);check(uploads==4,"the same dirty revision retries after an upload failure");
        CheckPixels(check,second,pixels!);
        map.Dispose();map.Dispose();map.Prepare(second,upload);
        check(uploads==5,"disposal retires map identity so a later session rebuilds on first use");
        CheckPixels(check,second,pixels!);map.Dispose();
    }
    private static void CheckPixels(Action<bool,string> check,Town town,byte[] pixels)
    {
        // Original palette and sample equations, including transparent padding.
        const int edge=128,samples=(Town.Width+1)/2;
        for(int y=0;y<edge;y++)for(int x=0;x<edge;x++){
            int at=(y*edge+x)*4;
            if(x>=samples||y>=samples){check(pixels[at]==0&&pixels[at+1]==0&&pixels[at+2]==0&&pixels[at+3]==0,"map upload preserves all transparent texture padding");continue;}
            int cx=x*2+1,cz=y*2+1,kind=WaterSurface.IsWater(town,cx,cz)?(int)Terrain.Sea:town.At(cx,cz);
            var expected=kind==(int)Terrain.Water||kind==(int)Terrain.Sea?new Color32(75,154,187):kind==(int)Terrain.Bridge?new Color32(167,119,73):kind==(int)Terrain.Beach?new Color32(229,209,150):kind==(int)Terrain.Cliff||WaterTerrain.IsCoast(kind)?new Color32(130,127,92):kind==(int)Terrain.Plaza?new Color32(191,179,147):new Color32(119,168,88);
            check(pixels[at]==expected.R&&pixels[at+1]==expected.G&&pixels[at+2]==expected.B&&pixels[at+3]==255,"retired map scratch preserves every palette byte and water/coast sample");
        }
    }
}
