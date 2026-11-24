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
        t.Traffic.Traverse(t,5.8f,40.2f,6.2f,40.2f);
        check(snow.Revision==0,"nonwinter travel leaves snow untouched");
        snow.SetWinter(true);
        t.Traffic.Enter(t,7,40);
        for(int visit=1;visit<=6;visit++){
            t.Traffic.Traverse(t,5.8f,40.2f,6.2f,40.2f);
            t.Traffic.Traverse(t,6.2f,40.2f,6.6f,40.5f);
            check(snow.VisitCount(6,40)==Math.Min(5,visit)&&snow.VisitCount(7,40)==1,"five snow visits saturate without changing the neighboring tile");
        }
        check(snow.Revision==6,"fully worn snow and movement within a tile leave the mask revision unchanged");
        snow.SetWinter(false);snow.SetWinter(true);
        check(snow.VisitCount(6,40)==5&&snow.VisitCount(7,40)==1,"switching the season preview preserves the current town's dents");
        SnowfallRecovery(check,Flat().Town);
        SnowCoverage(check);
        SparseSnowRecovery(check);
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
        var walker=s.Town.NpcAt(0);walker.PosX=14.5f;walker.PosZ=14.5f;
        var nav=new VillagerNavigation();Array.Fill(nav.Paused,true);nav.Paused[0]=false;
        nav.Tick(s);nav.Target(0,20,14);for(int i=0;i<600;i++)nav.Tick(s);
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
        var bytes=SaveCodec.Encode(s);check(BitConverter.ToInt32(bytes,4)==16&&t.Traffic.Visits.SequenceEqual(SaveCodec.Decode(bytes).Town.Traffic.Visits),"combined v16 preserves traffic");
        var parts=SplitSaveCodec.Encode(s);check(SplitSaveCodec.ReadVersion(parts[0])==11&&t.Traffic.Visits.SequenceEqual(SplitSaveCodec.Decode(parts).Town.Traffic.Visits),"split town v11 preserves traffic");
        var storage=new Storage();var repository=new SaveRepository(storage);repository.Save(s);t.Traffic.Enter(t,22,40);repository.Save(s);
        check(t.Traffic.Visits.SequenceEqual(new SaveRepository(storage).Load()!.Town.Traffic.Visits),"incremental saves include traffic before the next classification");
        // Derive the old twelve-building, paths-only shape explicitly. New
        // snow/jobs/koban tails are not part of either historical schema.
        var reader=new SplitSaveCodec.Reader(parts[0],SplitSaveCodec.TownData);reader.SkipWords(5);reader.SkipTerrain();reader.SkipTerrain();
        for(int i=0;i<6;i++){reader.SkipWords(2);reader.SkipText(24);reader.SkipWords(2);}
        reader.SkipWords(reader.Range(0,512)*6);reader.SkipWords(reader.Range(0,256)*6);reader.Int();
        int splitKoban=reader.Offset+BuildingSave.ProfileBuildingCount*4;reader.SkipWords(BuildingWorld.Count+42);reader.Paths(new TownPaths());
        var old=WithoutKobanType(parts[0],reader.Offset+4,splitKoban);Put(old,4,6);Put(old,24,old.Length);Crc(old);parts[0]=old;
        var legacy=SplitSaveCodec.Decode(parts);check(legacy.Town.Traffic.Visits.All(v=>v==0)&&legacy.Town.Paths.Packed.SequenceEqual(t.Paths.Packed),"old shared towns retain roads without fabricated visits");
        int tailBytes=4*(7+AnimalTown.Jobs.TownJobState.EncodedWordCount+s.Town.LostAndFound.EncodedWordCount);
        // tail words: daylight, job length/data, empty snow/ledger, site x/z,
        // recovery length/data (the fixture has neither snow nor a job ledger).
        old=WithoutKobanType(bytes,bytes.Length-tailBytes-TileTraffic.Bytes-8,CombinedBuildingOffset(bytes)+(2+BuildingSave.ProfileBuildingCount)*4);Put(old,4,10);Put(old,16,old.Length);Put(old,20,(int)SaveCodec.Crc(old,24,old.Length-24));check(SaveCodec.Decode(old).Town.Traffic.Visits.All(v=>v==0),"combined v10 remains readable");
        var broken=SplitSaveCodec.Encode(s);broken[0][reader.Offset-1]=255;Crc(broken[0]);bool rejected=false;try{SplitSaveCodec.Decode(broken);}catch(InvalidOperationException){rejected=true;}check(rejected,"invalid saved path styles remain rejected");
        Console.WriteLine($"Traffic paths: 15360 counter + 512 histogram + 3840 existing path bytes; 8192 native mask bytes on demand; 10000 entries {traversalMs:F2}ms host, classification {selectionMs:F3}ms host; no warm allocations");
    }
    private static void SnowCoverage(Action<bool,string> check)
    {
        var town=Flat().Town;var snow=town.Traffic.Snow;var pages=new SnowGroundVisibility();
        int section=TerrainArchivePlan.Index(8,40);
        bool Buried()=>SnowVisibility.Buried(town,10.25f,42.25f,.5f,.5f,.05f);
        pages.Prepare(town);
        check(!Buried()&&!pages.Covered(section),"summer keeps small objects and the base ground visible");
        snow.SetWinter(true);pages.Prepare(town);
        check(Buried()&&pages.Covered(section),"unbroken level snow hides buried seeds and the underlying ground page");
        check(!SnowVisibility.Buried(town,10.25f,42.25f,.5f,.5f,.27f)&&
            !SnowVisibility.Buried(town,10.25f,42.25f,.5f,.5f,DroppedItemPlan.Size),"flowers and dropped billboards protruding through snow stay visible");
        check(!SnowVisibility.Buried(town,float.NaN,42,.5f,.5f,.05f)&&
            !SnowVisibility.Buried(town,10,42,0,.5f,.05f),"invalid and empty footprints never disappear");
        check(!SnowVisibility.CoveredGround(town,0,40,8,8)&&
            !SnowVisibility.CoveredGround(town,120,40,8,8),"outer snow edges keep their base ground");
        town.Paths.Set(9,42,1);pages.Prepare(town);
        check(!Buried()&&!pages.Covered(section),"a path touching the footprint halo preserves objects and the ground beneath cutouts");
        town.Paths.Clear();pages.Prepare(town);
        check(Buried()&&pages.Covered(section),"clearing the path restores complete snow coverage");
        town.Traffic.Enter(town,9,42);pages.Prepare(town);
        check(!Buried()&&!pages.Covered(section),"even a shallow adjacent dent preserves the base and potentially exposed objects");
        for(int i=0;i<SnowWear.RecoverySeconds*30;i++)snow.TickSnowfall(true);
        pages.Prepare(town);
        check(Buried()&&pages.Covered(section),"complete recovery closes coverage again without camera movement");
        town.SetTerrain(7,42,Terrain.Camp);pages.Prepare(town);
        check(!pages.Covered(section)&&Buried(),"page-edge dirt preserves its base without invalidating distant buried objects");
        town.SetTerrain(7,42,Terrain.Grass);pages.Prepare(town);
        town.Elevation[42*Town.Width+10]=TerrainElevation.Block;town.NotifyTerrainChanged();pages.Prepare(town);
        check(!Buried()&&!pages.Covered(section),"raised cells and exposed vertical silhouettes keep complete geometry");
        town.Elevation[42*Town.Width+10]=TerrainElevation.RampNorth;town.NotifyTerrainChanged();pages.Prepare(town);
        check(!Buried()&&!pages.Covered(section),"sloped snow never culls ground or objects using a flat-height estimate");
        town.Elevation[42*Town.Width+10]=0;town.NotifyTerrainChanged();
        town.SetTerrain(10,42,Terrain.Plaza);pages.Prepare(town);
        check(!Buried()&&!pages.Covered(section),"non-snow material cutouts stay visible");
        town.SetTerrain(10,42,Terrain.Grass);snow.SetWinter(false);pages.Prepare(town);
        check(!Buried()&&!pages.Covered(section),"leaving winter restores geometry independently of camera and dent revisions");
    }
    private static void SparseSnowRecovery(Action<bool,string> check)
    {
        var state=Flat();var town=state.Town;var snow=town.Traffic.Snow;snow.SetWinter(true);
        long before=GC.GetAllocatedBytesForCurrentThread();town.Traffic.Enter(town,1,1);
        check(GC.GetAllocatedBytesForCurrentThread()-before<4096,"an isolated dent does not allocate a full-town recovery field");
        void Seconds(int count){for(int i=0;i<count*30;i++)snow.TickSnowfall(true);}
        before=GC.GetAllocatedBytesForCurrentThread();Seconds(300);
        check(GC.GetAllocatedBytesForCurrentThread()==before,"steady sparse snowfall recovery allocates no per-tick scratch");
        town.Traffic.Enter(town,Town.Width-1,Town.Height-1);Seconds(300);
        check(snow.VisitCount(1,1)==0&&snow.Packed[^1]!=0,"retiring the oldest recovery region preserves a newer distant dent");
        town.Traffic.Enter(town,1,1);town.Traffic.Enter(town,64,60);Seconds(300);
        check(snow.Packed[^1]==0&&snow.Packed[(Town.Width+1)/2]!=0&&snow.Packed[(60*Town.Width+64)/2]!=0,
            "re-entering a retired region and adding another survive active-region removal");
        Seconds(300);check(snow.Packed.All(v=>v==0),"all sparse regions fully recover and release their timestamp storage");
        snow.ObserveSnowDay(400,false);
        town.Traffic.Enter(town,1,1);town.Traffic.Enter(town,Town.Width-1,Town.Height-1);
        // Checkpoint import reconstructs both distant regions, including their
        // independent ages and the partial snowfall second.
        Seconds(137);for(int i=0;i<17;i++)snow.TickSnowfall(true);
        var restored=SaveCodec.Decode(SaveCodec.Encode(state)).Town.Traffic.Snow;
        for(int i=0;i<(SnowWear.RecoverySeconds-137)*30-17;i++){snow.TickSnowfall(true);restored.TickSnowfall(true);}
        check(snow.Packed.All(v=>v==0)&&snow.Packed.SequenceEqual(restored.Packed),
            "sparse checkpoint restoration retains exact recovery times across distant regions");
    }
    private static byte[] WithoutKobanType(byte[] source,int end,int offset)
    {var result=new byte[end-4];Array.Copy(source,result,offset);Array.Copy(source,offset+4,result,offset,end-offset-4);return result;}
    private static int CombinedBuildingOffset(byte[] bytes)
    {
        int at=32,mail=BitConverter.ToInt32(bytes,at+21*4);at+=35*4;
        void Text(){int n=BitConverter.ToInt32(bytes,at);at+=4+n*4;}
        void Records(int words){int n=BitConverter.ToInt32(bytes,at);at+=4+n*words*4;}
        Text();Text();at+=(GameState.MaximumPockets+64+mail)*4;Records(1);at+=(12+6*6)*4;Records(6);Records(6);at+=4;Records(1);
        for(int i=0;i<6;i++){Text();at+=16;}return at;
    }
    private static void SnowfallRecovery(Action<bool,string> check,Town town)
    {
        var weather=new DebugWeather();
        weather.Update(town.Seed,0,DebugWeatherChecks.SnowStart(town.Seed,0),3);
        var snow=town.Traffic.Snow;
        snow.SetWinter(true);
        town.Traffic.Visits[40*Town.Width+20]=TileTraffic.FullOpacityVisits;
        town.Paths.Set(20,40,1);
        for(int i=0;i<SnowWear.MaxVisits;i++)town.Traffic.Enter(town,6,40);
        town.Traffic.Enter(town,7,40);
        byte[] traffic=town.Traffic.Visits.ToArray(),paths=town.Paths.Packed.ToArray();
        int pathRevision=town.Paths.Revision,opacityRevision=town.Traffic.OpacityRevision;
        int revision=snow.Revision,cell=40*Town.Width+6;
        byte startingWear=snow.Packed[cell/2];
        void SnowSeconds(int seconds)
        {
            for(int i=0;i<seconds*30;i++)snow.TickSnowfall(weather.Snowing(0));
        }
        SnowSeconds(4);
        for(int i=0;i<29;i++)snow.TickSnowfall(weather.Snowing(0));
        check(snow.Revision==revision,"recovery publishes only when the visible depth changes");
        weather.SnowEnabled=false;
        SnowSeconds(600);
        check(snow.Packed[cell/2]==startingWear&&snow.Revision==revision,"disabled snowfall pauses recovery without catch-up");
        weather.SnowEnabled=true;weather.RainEnabled=true;
        SnowSeconds(600);
        check(snow.Revision==revision,"rain does not refill winter dents");
        weather.RainEnabled=false;
        snow.TickSnowfall(weather.Snowing(0));
        check(snow.Packed[cell/2]==startingWear-1&&snow.Revision==revision+1,"resumed snowfall preserves the partial second and reduces wear by one substep");
        SnowSeconds(35);
        check(snow.CenterCovered(6,40),"the ground closes over before the dent finishes smoothing");
        SnowSeconds(19);
        check(snow.Packed[cell/2]==0x34,"at 59 seconds the deeper dent is one step above the first level");
        SnowSeconds(1);
        check(snow.Packed[cell/2]==0x33,"at 60 seconds all dents have reached the first indentation level");
        check(traffic.SequenceEqual(town.Traffic.Visits)&&paths.SequenceEqual(town.Paths.Packed)&&
              town.Paths.Revision==pathRevision&&town.Traffic.OpacityRevision==opacityRevision,"snowfall recovery leaves path counts, coverage and revisions intact");

        for(int i=0;i<SnowWear.MaxVisits;i++)town.Traffic.Enter(town,6,40);
        SnowSeconds(4);
        town.Traffic.Enter(town,6,40);
        SnowSeconds(1);
        check(snow.VisitCount(6,40)==SnowWear.MaxVisits,"a fresh visit restarts even fully worn snow");
        SnowSeconds(29);
        town.Traffic.Enter(town,7,40);
        SnowSeconds(30);
        check((snow.Packed[cell/2]&15)==3&&(snow.Packed[cell/2]>>4)>3,"each tile uses time since its own last entry");
        SnowSeconds(30);
        check(snow.Packed[cell/2]==0x33,"the later tile also settles at the first indentation level");
        revision=snow.Revision;
        SnowSeconds(600);
        check(snow.Packed[cell/2]==0x33&&snow.Revision==revision,"continued snowfall preserves the shallow tracks without publishing changes");
        snow.ResetForTownEntry();
        check(snow.Packed.All(v=>v==0)&&snow.Revision==revision+1,"re-entering town fully clears the remaining tracks");
    }
    private static void Put(byte[] b,int at,int value){for(int i=0;i<4;i++)b[at+i]=(byte)((uint)value>>(8*i));}
    private static void Crc(byte[] b)=>Put(b,b.Length-4,(int)SaveCodec.Crc(b,0,b.Length-4));
}
