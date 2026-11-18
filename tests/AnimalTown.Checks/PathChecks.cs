using AnimalTown;

internal static class PathChecks
{
    private sealed class Storage : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        public byte[] Read(string name)=>Files[name];
        public void Write(string name,byte[] bytes)=>Files[name]=(byte[])bytes.Clone();
    }
    private static GameState Flat()
    {
        var s=new GameState(3);Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);
        s.Town.GroundCount=s.Town.PlacedCount=0;s.HousePlaced=false;s.Town.Paths.Clear();return s;
    }
    internal static void Run(Action<bool,string> check)
    {
        var s=Flat();var t=s.Town;
        check(t.Paths.Count==0&&t.Traffic.Visits.Length==15360,"new towns have no invented traffic or preplanned paths");
        for(int i=0;i<100;i++)t.Traffic.Visits[40*Town.Width+i]=(byte)(i+1);
        t.Traffic.Refresh(t);
        check(t.Paths.Count==20&&t.Traffic.Cutoff==81&&t.Traffic.HighOutliers==0,"the busiest fifth is ranked by visited eligible tiles, not a fraction of the largest count");
        for(int i=0;i<100;i++)check(t.Paths.At(i,40)==(i>=80?1:0),"ranked selection follows anonymous traffic");
        t.Traffic.Visits[40*Town.Width+99]=250;t.Traffic.Refresh(t);
        check(t.Traffic.HighOutliers==1&&t.Traffic.Cutoff==80&&t.Paths.Count==21&&t.Paths.At(99,40)==1,"high Tukey outliers do not distort the quota and are still paths");
        s=Flat();t=s.Town;for(int i=0;i<20;i++)t.Traffic.Visits[40*Town.Width+i]=35;t.Traffic.Visits[40*Town.Width+100]=200;
        t.Paths.Set(18,40,1);t.Paths.Set(19,40,1);t.Traffic.Refresh(t);
        check(t.Paths.Count==5&&t.Paths.At(18,40)==1&&t.Paths.At(19,40)==1&&t.Paths.At(100,40)==1,"ties retain established tiles without selecting every equal count");
        var selected=(byte[])t.Paths.Packed.Clone();int revision=t.Paths.Revision;t.Traffic.Refresh(t);
        check(selected.SequenceEqual(t.Paths.Packed)&&revision==t.Paths.Revision,"unchanged traffic preserves ties and the render revision");
        t.Tiles[40*Town.Width+100]=(byte)Terrain.Plaza;t.Traffic.Refresh(t);
        check(t.Paths.Count==4&&t.Paths.At(100,40)==0&&t.Traffic.Visits[40*Town.Width+100]==200,"plaza traffic is retained but not ranked or painted");
        s=Flat();t=s.Town;t.Traffic.Visits[40*Town.Width+20]=34;t.Paths.Set(20,40,1);t.Traffic.Refresh(t);
        check(t.Paths.Count==0,"even a previously selected tile remains hidden before visit 35");
        t.Traffic.Enter(t,20,40);t.Traffic.Refresh(t);
        check(t.Paths.Count==1&&t.Paths.At(20,40)==1,"visit 35 admits a ranked tile");
        s=Flat();t=s.Town;
        for(int i=0;i<10;i++)t.Traffic.Visits[40*Town.Width+i]=(byte)(i<5?i+1:35+i-5);
        t.Traffic.Refresh(t);
        check(t.Paths.Count==2&&t.Paths.At(8,40)==1&&t.Paths.At(9,40)==1,"minimum is an extra visibility gate, preserving the busiest fifth of all visited eligible tiles");
        s=Flat();t=s.Town;for(int i=0;i<20;i++)t.Traffic.Visits[40*Town.Width+i]=1;t.Traffic.Visits[40*Town.Width+100]=34;t.Traffic.Refresh(t);
        check(t.Traffic.HighOutliers==1&&t.Paths.Count==0,"high outliers also require at least 35 visits");
        t.Traffic.Enter(t,100,40);t.Traffic.Refresh(t);check(t.Paths.Count==1&&t.Paths.At(100,40)==1,"an outlier becomes visible at the same minimum");
        int previousOpacity=0;
        for(int i=0;i<256;i++){
            int opacity=TileTraffic.OpacityLevel(i);
            check(opacity>=previousOpacity&&opacity<=15,"opacity is monotonic and fits the existing four-bit mask");
            if(i<35)check(opacity==0,"unqualified counts have zero opacity");
            if(i>=100)check(opacity==15,"full-opacity counts remain fully visible");
            previousOpacity=opacity;
        }
        check(TileTraffic.OpacityLevel(35)==2&&TileTraffic.OpacityLevel(50)==5&&TileTraffic.OpacityLevel(70)==9&&TileTraffic.OpacityLevel(99)==14,"path opacity starts faint and increases toward visit 100");
        s=Flat();t=s.Town;t.Traffic.Visits[40*Town.Width+20]=35;t.Paths.Set(20,40,1);
        int opacityRevision=t.Traffic.OpacityRevision;t.Traffic.Enter(t,20,40);
        check(t.Traffic.OpacityRevision==opacityRevision,"counts within one opacity step cause no mask upload");
        for(int i=0;i<4;i++)t.Traffic.Enter(t,20,40);
        check(t.Traffic.OpacityRevision==opacityRevision+1&&t.Paths.At(20,40)==1,"opacity changes publish without changing selected tiles");
        opacityRevision=t.Traffic.OpacityRevision;t.Traffic.Visits[40*Town.Width+21]=35;
        for(int i=0;i<5;i++)t.Traffic.Enter(t,21,40);
        t.Traffic.Visits[40*Town.Width+20]=100;t.Traffic.Enter(t,20,40);
        check(t.Traffic.OpacityRevision==opacityRevision,"unselected traffic and full-opacity traffic do not dirty the mask");
        s=Flat();t=s.Town;var snow=t.Traffic.Snow;
        int snowCell=40*Town.Width+6,shift=(snowCell%4)*2;
        t.Traffic.Traverse(t,5.8f,40.2f,6.2f,40.2f);
        check(snow.Revision==0,"nonwinter travel leaves snow untouched");
        snow.SetWinter(true);
        for(int visit=1;visit<=4;visit++){
            t.Traffic.Traverse(t,5.8f,40.2f,6.2f,40.2f);
            t.Traffic.Traverse(t,6.2f,40.2f,6.6f,40.5f);
            check(((snow.Packed[snowCell/4]>>shift)&3)==Math.Min(3,visit),"snow advances on tile entry and caps after three visits");
        }
        snow.SetWinter(false);snow.SetWinter(true);
        check(snow.Packed.All(v=>v==0),"a new winter starts with fresh snow");
        s=Flat();t=s.Town;t.Traffic.Traverse(t,5.1f,40.2f,8.2f,40.2f);
        check(t.Traffic.Visits[40*Town.Width+5]==0&&t.Traffic.Visits[40*Town.Width+6]==1&&t.Traffic.Visits[40*Town.Width+7]==1&&t.Traffic.Visits[40*Town.Width+8]==1,"all entered cells are recorded, not the departure cell");
        t.Traffic.Traverse(t,8.2f,40.2f,8.9f,40.8f);check(t.Traffic.Visits[40*Town.Width+8]==1,"standing and walking within a tile add no visits");
        t.Traffic.Traverse(t,8.2f,40.2f,5.2f,40.2f);check(t.Traffic.Visits[40*Town.Width+5]==1&&t.Traffic.Visits[40*Town.Width+6]==2,"re-entering a tile counts again");
        t.Traffic.Traverse(t,10.5f,10.5f,12.5f,12.5f);check(t.Traffic.Visits[11*Town.Width+11]==1&&t.Traffic.Visits[12*Town.Width+12]==1&&t.Traffic.Visits[10*Town.Width+11]==0,"diagonal corner touches do not count as traversed area");
        t.Traffic.Traverse(t,float.NaN,3,4,5);t.Traffic.Enter(t,-1,0);t.Traffic.Enter(t,10000,10000);
        t.Tiles[12*Town.Width+12]=(byte)Terrain.Water;t.Traffic.Enter(t,12,12);check(t.Traffic.Visits[12*Town.Width+12]==1,"invalid coordinates and water never acquire visits");
        t.Traffic.Visits[40*Town.Width+5]=255;t.Traffic.Visits[40*Town.Width+6]=200;
        t.Traffic.Visits[40*Town.Width+7]=34;t.Traffic.Visits[40*Town.Width+8]=35;t.Traffic.Visits[40*Town.Width+9]=50;t.Traffic.Visits[40*Town.Width+10]=100;
        t.Traffic.Enter(t,5,40);
        check(t.Traffic.Normalizations==1&&t.Traffic.Visits[40*Town.Width+5]==179&&t.Traffic.Visits[40*Town.Width+6]==150&&t.Traffic.Visits[1]==0,"overflow rescales only excess above full opacity without wrapping or saturation");
        check(t.Traffic.Visits[40*Town.Width+7]==34&&t.Traffic.Visits[40*Town.Width+8]==35&&t.Traffic.Visits[40*Town.Width+9]==50&&t.Traffic.Visits[40*Town.Width+10]==100,"traffic elsewhere cannot reset minimum or fade milestones");
        s=Flat();t=s.Town;s.X=40.98f;s.Z=45.5f;PlayerMovement.Move(s,0,1,0,0,false,out _,out _);
        check(t.Traffic.Visits[45*Town.Width+41]==1,"player movement records normal outdoor entry");
        s.X=41.98f;PlayerMovement.Move(s,16,1,0,0,false,out _,out _);check(t.Traffic.Visits[45*Town.Width+42]==0,"indoor movement adds no town traffic");
        s.X=42.98f;PlayerMovement.Move(s,0,1,0,0,false,out _,out _,false);check(t.Traffic.Visits[45*Town.Width+43]==0,"onboarding previews suppress traffic");
        var nav=new VillagerNavigation();nav.Tick(s);nav.Target(0,20,14);for(int i=0;i<600;i++)nav.Tick(s);
        check(t.Traffic.Visits[14*Town.Width+15]>0,"NPC movement uses the same anonymous history");
        s=Flat();t=s.Town;int version=t.TerrainVersion;for(int i=0;i<35;i++)t.Traffic.Enter(t,20,40);
        for(int i=0;i<TileTraffic.RefreshTicks-1;i++)t.Traffic.Tick(t);check(t.Paths.Count==0,"classification is batched");t.Traffic.Tick(t);
        check(t.Paths.Count==1&&t.TerrainVersion==version,"forming paths does not invalidate terrain or shadow geometry");
        for(int i=0;i<100;i++)t.Traffic.Refresh(t);
        long allocated=GC.GetAllocatedBytesForCurrentThread(),started=System.Diagnostics.Stopwatch.GetTimestamp();
        for(int i=0;i<10000;i++)t.Traffic.Traverse(t,20.5f,40.5f,21.5f,40.5f);
        double traversalMs=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        started=System.Diagnostics.Stopwatch.GetTimestamp();for(int i=0;i<100;i++)t.Traffic.Refresh(t);
        double selectionMs=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency/100;
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"warm entry, normalization and classification allocate no managed objects");
        var bytes=SaveCodec.Encode(s);check(BitConverter.ToInt32(bytes,4)==11&&t.Traffic.Visits.SequenceEqual(SaveCodec.Decode(bytes).Town.Traffic.Visits),"combined v11 preserves traffic");
        var parts=SplitSaveCodec.Encode(s);check(SplitSaveCodec.ReadVersion(parts[0])==7&&t.Traffic.Visits.SequenceEqual(SplitSaveCodec.Decode(parts).Town.Traffic.Visits),"split town v7 preserves traffic");
        var storage=new Storage();var repository=new SaveRepository(storage);repository.Save(s);t.Traffic.Enter(t,22,40);repository.Save(s);
        check(t.Traffic.Visits.SequenceEqual(new SaveRepository(storage).Load()!.Town.Traffic.Visits),"incremental saves include traffic before the next classification");
        var old=parts[0][..^(TileTraffic.Bytes+8+4)];Array.Resize(ref old,old.Length+4);Put(old,4,6);Put(old,24,old.Length);Crc(old);parts[0]=old;
        var legacy=SplitSaveCodec.Decode(parts);check(legacy.Town.Traffic.Visits.All(v=>v==0)&&legacy.Town.Paths.Packed.SequenceEqual(t.Paths.Packed),"old shared towns retain roads without fabricated visits");
        old=bytes[..^(TileTraffic.Bytes+8)];Put(old,4,10);Put(old,16,old.Length);Put(old,20,(int)SaveCodec.Crc(old,24,old.Length-24));check(SaveCodec.Decode(old).Town.Traffic.Visits.All(v=>v==0),"combined v10 remains readable");
        var broken=SplitSaveCodec.Encode(s);broken[0][^(TileTraffic.Bytes+13)]=255;Crc(broken[0]);bool rejected=false;try{SplitSaveCodec.Decode(broken);}catch(InvalidOperationException){rejected=true;}check(rejected,"invalid saved path styles remain rejected");
        Console.WriteLine($"Traffic paths: 15360 counter + 512 histogram + 3840 existing path bytes; 8192 native mask bytes on demand; 10000 entries {traversalMs:F2}ms host, classification {selectionMs:F3}ms host; no warm allocations");
    }
    private static void Put(byte[] b,int at,int value){for(int i=0;i<4;i++)b[at+i]=(byte)((uint)value>>(8*i));}
    private static void Crc(byte[] b)=>Put(b,b.Length-4,(int)SaveCodec.Crc(b,0,b.Length-4));
}
