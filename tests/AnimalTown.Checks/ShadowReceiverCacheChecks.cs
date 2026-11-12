using AnimalTown;

internal static class ShadowReceiverCacheChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var town=new Town(7319);Array.Fill(town.Tiles,(byte)Terrain.Grass);Array.Clear(town.Elevation);
        long before=GC.GetAllocatedBytesForCurrentThread();var cache=new ShadowReceiverCache();
        check(GC.GetAllocatedBytesForCurrentThread()-before<40000,"receiver cache construction does not reserve 64 maximum-size UV streams");
        check(cache.ResidentPayloadBytes==13440,"all-flat receiver storage starts at 13,440 payload bytes");
        var casters=new ShadowCaster[128];var input=new float[casters.Length][];var counts=new int[casters.Length];
        for(int i=0;i<casters.Length;i++){
            casters[i]=new ShadowCaster(4+(i%16)*9,4+(i/16)*18,1,0,0);
            input[i]=new float[GroundShadow.MaximumTriangles*6];
            for(int n=0;n<input[i].Length;n++)input[i][n]=i*1000+n+.125f;
            counts[i]=i%4==0?0:i%4==1?3:i%4==2?6:GroundShadow.MaximumTriangles*3;
        }
        var output=new float[input[0].Length];
        for(int i=0;i<64;i++)cache.Store(town,casters[i],input[i],6,true);
        for(int i=0;i<64;i++){
            check(cache.Restore(town,casters[i],output,out int count,out bool flat)&&count==6&&flat,"all 64 flat receivers retain their independent logical slots");
            for(int n=0;n<12;n++)check(output[n]==input[i][n],"flat receivers retain supplied UVs exactly rather than assuming rectangle coordinates");
        }
        check(cache.ResidentPayloadBytes==13440,"warming every flat slot does not grow the arena");

        // An independent 64-slot recency model checks hits, misses, and payloads
        // while mixed spans repeatedly fragment and compact the packed arena.
        var ids=new int[64];var recency=new long[64];Array.Fill(ids,-1);long clock=0;int hits=0,misses=0;
        cache=new ShadowReceiverCache();
        for(int frame=0;frame<7000;frame++){
            int key=(frame*17+frame/31)%128,slot=-1;
            for(int i=0;i<ids.Length;i++)if(ids[i]==key){slot=i;break;}
            bool found=cache.Restore(town,casters[key],output,out int count,out bool flat);
            check(found==(slot>=0),"packed receiver storage preserves baseline LRU hit/miss decisions");
            if(found){
                hits++;recency[slot]=++clock;
                check(count==counts[key]&&flat==(key%2==0),"compaction preserves the count and flat flag of every receiver");
                for(int n=0;n<count*2;n++)check(output[n]==input[key][n],"mixed UV spans restore byte-exact coordinates after reuse and compaction");
            }else{
                misses++;int oldest=0;
                for(int i=0;i<ids.Length;i++){if(ids[i]<0){oldest=i;break;}if(recency[i]<recency[oldest])oldest=i;}
                cache.Store(town,casters[key],input[key],counts[key],key%2==0);ids[oldest]=key;recency[oldest]=++clock;
            }
            check(cache.ResidentPayloadBytes<=ShadowReceiverCache.MaximumPayloadBytes,"complex receivers never exceed the original bounded payload ceiling");
        }
        check(hits>100&&misses>100,"mixed receiver trace exercises both warm hits and cold replacements");

        // Warm the maximum representable payload once, then prove arbitrary
        // clipping shapes and flat/empty replacements require no allocation.
        cache=new ShadowReceiverCache();
        for(int i=0;i<64;i++)cache.Store(town,casters[i],input[i],GroundShadow.MaximumTriangles*3,false);
        check(cache.ResidentPayloadBytes==ShadowReceiverCache.MaximumPayloadBytes,"all 64 maximum-complexity receivers fit without reducing complex capacity");
        for(int i=0;i<64;i++){
            check(cache.Restore(town,casters[i],output,out int count,out bool flat)&&count==288&&!flat,"every maximum-complexity receiver survives arena growth");
            for(int n=0;n<count*2;n++)check(output[n]==input[i][n],"growth preserves existing maximum receiver coordinates");
        }
        before=GC.GetAllocatedBytesForCurrentThread();
        for(int frame=0;frame<7000;frame++){
            int key=(frame*17+frame/31)%128;
            if(!cache.Restore(town,casters[key],output,out _,out _))cache.Store(town,casters[key],input[key],counts[key],key%2==0);
        }
        long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated==0,"a warmed high-water arena has zero allocations across mixed hits, eviction, reuse and compaction");
        Console.WriteLine("Receiver cache: all-flat payload 13440 / 157824 bytes; mixed trace "+hits+" hits / "+misses+" misses; 7000 warm operations allocated "+allocated+" bytes");
    }
}
