using AnimalTown;

internal static class IncrementalSaveChecks
{
    private sealed class Disk : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal readonly List<string> Names=new();
        internal int CutAt,Mode,Bytes,Reads;
        internal string? CorruptAfterWrite;
        public byte[] Read(string name){Reads++;var a=(byte[])Files[name].Clone();if(name==CorruptAfterWrite&&Names.Contains(name)){a[32]^=1;uint crc=SaveCodec.Crc(a,0,a.Length-4);for(int i=0;i<4;i++)a[a.Length-4+i]=(byte)(crc>>(i*8));}return a;}
        public void Write(string name,byte[] bytes){Names.Add(name);Bytes+=bytes.Length;bool cut=Names.Count==CutAt;if(cut&&Mode==0)throw new IOException("Before write");Files[name]=cut&&Mode==1?bytes[..(bytes.Length/2)]:(byte[])bytes.Clone();if(cut)throw new IOException("Interrupted write");}
        internal Disk Copy(){var d=new Disk();foreach(var f in Files)d.Files[f.Key]=(byte[])f.Value.Clone();return d;}
        internal void Reset(){Names.Clear();Bytes=0;CutAt=0;CorruptAfterWrite=null;}
    }
    private sealed class PreparingDisk(Disk inner,Action preparation):ISaveStorage,ISaveWritePreparationStorage
    {
        internal int Preparations;private bool ready;
        public byte[] Read(string name)=>inner.Read(name);
        public void PrepareWrite(){Preparations++;preparation();ready=true;}
        public void Write(string name,byte[] bytes){if(!ready)throw new IOException("Unprepared storage");inner.Write(name,bytes);}
    }
    private static GameState Town(){var s=new GameState(7319){Name="Juniper",Wallet=1234};Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);s.Town.GroundCount=0;return s;}
    private static bool Fails(Action action){try{action();return false;}catch(Exception){return true;}}
    private static void Same(GameState a,GameState b,Action<bool,string> check,string label)=>check(SaveCodec.Encode(a).SequenceEqual(SaveCodec.Encode(b)),label);
    private static bool Kind(string name,string prefix)=>name==prefix+"0"||name==prefix+"1";
    private static void Only(Disk disk,string kind,Action<bool,string> check)
    {check(disk.Names.Count==4&&disk.Names.Count(n=>Kind(n,kind))==2&&disk.Names.Count(n=>Kind(n,"AT-Commit"))==2,"only changed component and both reference heads are written: "+kind);}
    internal static void Run(Action<bool,string> check)
    {
        var preparedState=Town();var preparedDisk=new Disk();int beforePreparation=preparedState.Sequence;
        var preparedStorage=new PreparingDisk(preparedDisk,()=>check(preparedState.Sequence==beforePreparation&&preparedDisk.Names.Count==0,"storage preparation precedes revision changes and record writes"));
        new SaveRepository(new SaveReadCache(preparedStorage)).Save(preparedState);
        check(preparedStorage.Preparations==1&&new SaveRepository(preparedDisk).Load()!=null,"the exclusive read cache forwards storage preparation and publishes a complete save");
        preparedDisk.Reset();int preparedSequence=preparedState.Sequence;
        var failedPreparation=new PreparingDisk(preparedDisk,()=>throw new IOException("Interrupted conversion"));
        check(Fails(()=>new SaveRepository(failedPreparation).Save(preparedState))&&preparedState.Sequence==preparedSequence&&preparedDisk.Names.Count==0,"an interrupted preparation preserves the live revision and all logical records");
        var wrongTown=new GameState(7320);var guardedPreparation=new PreparingDisk(preparedDisk,()=>throw new Exception("Must not prepare a changed town"));
        check(Fails(()=>new SaveRepository(guardedPreparation).Save(wrongTown))&&guardedPreparation.Preparations==0,"town identity is checked before storage preparation can write");
        var s=Town();var d=new Disk();var repo=new SaveRepository(d);repo.Save(s);
        check(d.Names.Count==18&&s.Sequence==1,"first save publishes eight component pairs and two complete manifests");Same(s,repo.Load()!,check,"initial reference save restores the exact logical snapshot");
        var original=d.Files.ToDictionary(f=>f.Key,f=>(byte[])f.Value.Clone());d.Reset();int seq=s.Sequence;
        long start=GC.GetAllocatedBytesForCurrentThread();repo.Save(s);long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
        check(d.Names.Count==0&&repo.LastWrittenRecords==0&&s.Sequence==seq&&original.All(f=>f.Value.SequenceEqual(d.Files[f.Key])),"healthy unchanged save is a true no-op, including publication sequence and every file byte");
        check(allocated<256*1024,"unchanged save stays inside the previous 256 KiB validation budget");Console.WriteLine("Incremental no-op allocated "+allocated+" bytes");
        var terrain=(byte[])d.Files["AT-Town1"].Clone();s.Wallet++;d.Reset();repo.Save(s);Only(d,"AT-Player",check);check(d.Bytes<4096&&d.Files["AT-Town1"].SequenceEqual(terrain),"wallet save writes under 4 KiB and leaves town bytes untouched");
        d.Reset();s.Town.Residents[0].PosX+=.25f;s.Town.Residents[0].Dwell=99;repo.Save(s);Only(d,"AT-Residents",check);check(d.Files["AT-Town1"].SequenceEqual(terrain),"NPC wandering/timers do not dirty static town data");
        d.Reset();s.Town.Stock[0]=s.Town.Stock[0]==0?1:0;repo.Save(s);Only(d,"AT-Shops",check);
        d.Reset();s.Town.Tiles[0]=(byte)Terrain.Plaza;s.Town.NotifyTerrainChanged();repo.Save(s);Only(d,"AT-Town",check);Same(s,repo.Load()!,check,"terrain edits survive incremental reference reconstruction");
        check(repo.Load()!.Town.DaylightLocation==0,"an old town starts with the Classic daylight schedule");
        d.Reset();s.Town.DaylightLocation=1;repo.Save(s);Only(d,"AT-Town",check);
        check(repo.Load()!.Town.DaylightLocation==1,"the location persists in the shared town card record");
        check(SaveCodec.Decode(SaveCodec.Encode(s)).Town.DaylightLocation==1&&SplitSaveCodec.Decode(SplitSaveCodec.Encode(s)).Town.DaylightLocation==1,"combined and server snapshot codecs preserve the same location");
        d.Reset();s.Town.Residents[0].HomeX++;repo.Save(s);Only(d,"AT-Houses",check);
        // Reload/recreate the repository: tracking must not depend on a long-lived
        // in-memory dirty set, and inactive residents retain their own records.
        s=repo.Load()!;s.SelectProfile(1);s.Name="Milo";d.Reset();new SaveRepository(d).Save(s);Same(s,new SaveRepository(d).Load()!,check,"new profile and active selection round-trip without regenerating the town");
        check(new SaveRepository(d).Load()!.Town.DaylightLocation==1,"switching residents keeps the shared daylight location");
        s.SelectProfile(0);d.Reset();new SaveRepository(d).Save(s);Only(d,"AT-Residents",check);
        var before=SaveCodec.Encode(s);var same=new SaveRepository(d).Load()!;d.Reset();new SaveRepository(d).Save(same);check(d.Names.Count==0,"a freshly loaded snapshot does not rewrite records");
        // A failed write at every real phase must leave a complete old/new
        // snapshot. The first manifest publication is the atomic boundary.
        var baseDisk=d.Copy();var baseline=SaveCodec.Decode(before);
        var desired=SaveCodec.Decode(before);desired.Wallet+=5;desired.Town.Spending++;desired.Town.Residents[0].Dwell++;
        var measured=baseDisk.Copy();new SaveRepository(measured).Save(desired);int writes=measured.Names.Count,publication=measured.Names.FindIndex(n=>Kind(n,"AT-Commit"))+1;
        check(writes==8,"compound save writes only three changed pairs and two manifests");
        var cachedDisk=baseDisk.Copy();var cachedCandidate=SaveCodec.Decode(before);
        cachedCandidate.Wallet+=5;cachedCandidate.Town.Spending++;cachedCandidate.Town.Residents[0].Dwell++;
        var cachedReads=new SaveReadCache(cachedDisk);new SaveRepository(cachedReads).Save(cachedCandidate);
        check(cachedReads.CacheHits>0&&cachedDisk.Reads<measured.Reads,"transaction cache reduces physical reads without reducing writes");
        check(cachedDisk.Names.SequenceEqual(measured.Names),"cache preserves write and publication ordering");
        Same(desired,new SaveRepository(cachedDisk).Load()!,check,"cached transaction saves the identical snapshot");
        Console.WriteLine("Save physical reads: "+measured.Reads+" -> "+cachedReads.PhysicalReads+" with "+cachedReads.CacheHits+" cache hits");
        for(int mode=0;mode<3;mode++)for(int cut=1;cut<=writes;cut++){
            var fault=baseDisk.Copy();fault.Mode=mode;fault.CutAt=cut;var candidate=SaveCodec.Decode(before);candidate.Wallet+=5;candidate.Town.Spending++;candidate.Town.Residents[0].Dwell++;
            check(Fails(()=>new SaveRepository(new SaveReadCache(fault)).Save(candidate))&&candidate.Sequence==baseline.Sequence,"every interrupted cached update reports failure without changing the live sequence");
            var recovered=new SaveRepository(fault).Load();bool published=cut>publication||cut==publication&&mode==2;
            check(recovered!=null,"interruption always retains a complete committed snapshot");Same(recovered!,published?desired:baseline,check,"power cut never mixes component revisions");
            fault.CutAt=0;fault.Names.Clear();var retry=recovered!;new SaveRepository(fault).Save(retry);Same(retry,new SaveRepository(fault).Load()!,check,"a recovered save repairs redundancy without losing its snapshot");
            new SaveRepository(fault).Save(candidate);Same(desired,new SaveRepository(fault).Load()!,check,"the original live snapshot retries idempotently after any publication/mirror interruption");
        }
        // Both files for unchanged data may be shared by many manifests. Exact
        // mirrored revision checks recover a single damaged file without rollback.
        for(int kind=0;kind<9;kind++)for(int bank=0;bank<2;bank++){
            var damaged=baseDisk.Copy();string name=SplitSaveCodec.FileName(kind,bank);damaged.Files[name][damaged.Files[name].Length/2]^=1;
            var recovered=new SaveRepository(damaged).Load();check(recovered!=null,"any one corrupt record or manifest retains its mirror");Same(baseline,recovered!,check,"single-file corruption preserves the latest complete save");
            damaged.Reset();new SaveRepository(damaged).Save(recovered!);check(damaged.Names.Count>0,"save repairs missing or damaged redundancy");Same(recovered!,new SaveRepository(damaged).Load()!,check,"repair retains exact logical state");
        }
        for(int invalid=0;invalid<3;invalid++){
            var disk=baseDisk.Copy();var candidate=SaveCodec.Decode(before);if(invalid==0)candidate.Wallet=100000;else if(invalid==1)candidate.Town.Residents[0].PosX=-2;else candidate.Town.Elevation[0]=255;
            var frozen=SaveCodec.Encode(candidate);check(Fails(()=>new SaveRepository(disk).Save(candidate)),"full semantic validation rejects invalid player, NPC and terrain snapshots");check(frozen.SequenceEqual(SaveCodec.Encode(candidate)),"invalid snapshots leave live data/sequence unchanged");Same(baseline,new SaveRepository(disk).Load()!,check,"invalid writes never publish over the last valid snapshot");
        }
        var changed=baseDisk.Copy();var c=SaveCodec.Decode(before);c.Wallet++;changed.CorruptAfterWrite="AT-Player"+(1-ReferenceBank(changed,c.Sequence,3));
        check(Fails(()=>new SaveRepository(new SaveReadCache(changed)).Save(c)),"cached changed record readback rejects altered content even with a recomputed CRC");Same(baseline,new SaveRepository(changed).Load()!,check,"readback failure preserves the previous publication");
        // Import old combined and v3/v4 split saves without changing source data.
        foreach(int format in new[]{0,3,4}){
            var disk=new Disk();var old=Town();old.Sequence=7;if(format==3)old.Town.Paths.Clear();
            if(format==0)disk.Files["AnimalTown1"]=SaveCodec.Encode(old);
            else{var p=format==3?SplitSaveCodec.EncodeLegacy(old):SplitSaveCodec.Encode(old);for(int i=0;i<p.Length;i++)disk.Files[SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),1)]=p[i];disk.Files["AT-Commit1"]=SplitSaveCodec.EncodeCommit(old,p);}
            var loaded=new SaveRepository(disk).Load()!;Same(old,loaded,check,"old save remains readable before migration");var preserved=format==0?(byte[])disk.Files["AnimalTown1"].Clone():null;
            new SaveRepository(disk).Save(loaded);Same(loaded,new SaveRepository(disk).Load()!,check,"migration preserves all existing progress and terrain");if(preserved!=null)check(preserved.SequenceEqual(disk.Files["AnimalTown1"]),"combined migration source is retained unchanged");
            disk.Reset();new SaveRepository(disk).Save(loaded);check(disk.Names.Count==0,"migrated town immediately benefits from zero-write saves");
        }
        var dense=Town();for(int i=0;i<dense.Town.Tiles.Length;i++)dense.Town.Tiles[i]=(byte)(i%2==0?Terrain.Grass:Terrain.Plaza);
        var denseDisk=new Disk();new SaveRepository(denseDisk).Save(dense);int townBytes=denseDisk.Files["AT-Town0"].Length;
        denseDisk.Reset();dense.Wallet++;new SaveRepository(denseDisk).Save(dense);Only(denseDisk,"AT-Player",check);
        check(townBytes>AnimalTown.Town.Cells*4&&denseDisk.Bytes<4096,"a player update leaves the worst-case edited terrain record unwritten");Same(dense,new SaveRepository(denseDisk).Load()!,check,"large edited terrain survives small incremental saves");
        var stale=baseDisk.Copy();var fresh=new SaveRepository(stale).Load()!;fresh.Wallet++;new SaveRepository(stale).Save(fresh);stale.Reset();
        check(Fails(()=>new SaveRepository(stale).Save(SaveCodec.Decode(before)))&&stale.Names.Count==0,"an older different snapshot cannot overwrite a newer save");
        check(new SaveRepository(new Disk()).Load()==null,"empty card does not invent a save");
        var classic=new SaveRepository(d).Load()!;classic.Town.DaylightLocation=0;d.Reset();new SaveRepository(d).Save(classic);Only(d,"AT-Town",check);
        check(new SaveRepository(d).Load()!.Town.DaylightLocation==0,"switching back to Classic is saved and reloaded");
        JobTransactions(check);
        SnowTransactions(check);
        OfflineClockMailTransactions(check);
        Console.WriteLine("Incremental saves: exact component writes/no-op, static town vs NPC runtime, mirrored references, corruption, power cuts, retries and migration passed");
    }
    private static void SnowTransactions(Action<bool,string> check)
    {
        var s=Town();var snow=s.Town.Traffic.Snow;
        var disk=new Disk();var repository=new SaveRepository(disk);
        repository.Save(s);
        check(!repository.Load()!.Town.Traffic.Snow.Winter,"older town records load without a winter checkpoint");

        snow.SetWinter(true);snow.ObserveSnowDay(100,false);
        for(int i=0;i<SnowWear.MaxVisits;i++)s.Town.Traffic.Enter(s.Town,20,30);
        for(int i=0;i<40*30-1;i++)snow.TickSnowfall(true);
        s.Town.Traffic.Enter(s.Town,21,30);
        repository.Save(s);
        var restored=repository.Load()!;
        Same(s,restored,check,"the card round-trips shared snow dents and their partial recovery");
        var split=SplitSaveCodec.Encode(s);
        check(SplitSaveCodec.ReadVersion(split[0])==11&&SplitSaveCodec.Decode(split).Town.Traffic.Snow.Packed.SequenceEqual(snow.Packed),
            "the split town schema retains its snow checkpoint");
        restored.SelectProfile(1);
        var shared=restored.Town.Traffic.Snow;
        check(shared.Packed.SequenceEqual(snow.Packed),"another profile in the same town sees the same dents");
        int revision=shared.Revision;
        shared.TickSnowfall(true);snow.TickSnowfall(true);
        check(shared.Revision==revision+1&&shared.Packed.SequenceEqual(snow.Packed),
            "reload preserves both tile ages and the fraction of a snowfall second");
        shared.ObserveSnowDay(101,true);
        byte[] traffic=(byte[])restored.Town.Traffic.Visits.Clone();
        byte[] paths=(byte[])restored.Town.Paths.Packed.Clone();
        repository.Save(restored);
        var cleared=repository.Load()!.Town;
        check(cleared.Traffic.Snow.Packed.All(b=>b==0)&&cleared.Traffic.Visits.SequenceEqual(traffic)&&cleared.Paths.Packed.SequenceEqual(paths),
            "a reset can be saved without changing shared paths or traffic");
    }
    private static void JobTransactions(Action<bool,string> check)
    {
        var baseline=Town();var jobs=baseline.Town.JobState;
        jobs.ObserveOutdoorAcre(0,0);jobs.Resident(0).StartNoJobCooldown();jobs.Resident(0).ConversationCompleted();
        var disk=new Disk();new SaveRepository(disk).Save(baseline);disk.Reset();
        var clockOnly=SaveCodec.Decode(SaveCodec.Encode(baseline));clockOnly.Town.JobState.Advance(100);
        new SaveRepository(disk).Save(clockOnly);Only(disk,"AT-Residents",check);
        check(disk.Bytes==1312,"a cooldown checkpoint writes two 464-byte NPC records and two 192-byte manifests, without terrain");
        var before=SaveCodec.Encode(clockOnly);var baseDisk=disk.Copy();
        void Change(GameState s){s.Wallet+=11;s.Town.JobState.Advance(234);s.Town.JobState.Offers.NextInt(17);s.Town.JobState.Rewards.NextInt(100);}
        var desired=SaveCodec.Decode(before);Change(desired);var measured=baseDisk.Copy();measured.Reset();new SaveRepository(measured).Save(desired);
        int writes=measured.Names.Count,publication=measured.Names.FindIndex(n=>Kind(n,"AT-Commit"))+1;
        check(writes==6&&!measured.Names.Any(n=>Kind(n,"AT-Town")),"wallet and job RNG/counters share one transaction while the town record is reused");
        for(int mode=0;mode<3;mode++)for(int cut=1;cut<=writes;cut++){
            var fault=baseDisk.Copy();fault.Reset();fault.Mode=mode;fault.CutAt=cut;var candidate=SaveCodec.Decode(before);Change(candidate);
            check(Fails(()=>new SaveRepository(new SaveReadCache(fault)).Save(candidate)),"interrupted job checkpoint reports failure");
            var recovered=new SaveRepository(fault).Load();bool published=cut>publication||cut==publication&&mode==2;
            check(recovered!=null,"interrupted job checkpoint retains a complete committed save");
            Same(recovered!,published?desired:clockOnly,check,"all interruption phases recover either the old wallet/RNG/clock or the new wallet/RNG/clock, never a mix");
            fault.CutAt=0;new SaveRepository(fault).Save(candidate);
            Same(desired,new SaveRepository(fault).Load()!,check,"retrying an interrupted job checkpoint preserves the exact RNG and economic revision");
        }
        Console.WriteLine("Job clock save: 164-byte extension; 1,312 encoded bytes/checkpoint; zero terrain rewrites; all 18 write interruptions recovered atomically");
    }
    private static int ReferenceBank(Disk d,int sequence,int part)
    {var b=d.Files["AT-Commit0"];int at=28+part*20+4;return b[at]|b[at+1]<<8|b[at+2]<<16|b[at+3]<<24;}
    private static void OfflineClockMailTransactions(Action<bool,string> check)
    {
        var state=Town();state.Tutorial=7;long now=AnimalTown.Jobs.JobCalendar.At(2027,7,15,12);state.ResidentJobs.Enable(49321,now);
        string body=string.Concat(Enumerable.Repeat("\U0001f642",1000));
        for(int batch=0;batch<2;batch++){
            for(int i=0;i<64;i++){state.Receive(Catalog.WritingPaper);check(state.ResidentJobs.SendLetter(state.Town.Jobs.Player(0),body,state.Find(Catalog.WritingPaper),-1,state.ResidentJobs.NextCommandSequence).Result==AnimalTown.Jobs.JobResult.Ok,"clock fixture queues actual full-length mail");}
            state.ResidentJobs.AdvanceTo(now+=86400);
        }
        var disk=new Disk();new SaveRepository(disk).Save(state);var before=SaveCodec.Encode(state);var original=disk.Copy();
        var economic0=(byte[])disk.Files["AT-Shops0"].Clone();var economic1=(byte[])disk.Files["AT-Shops1"].Clone();
        state.ResidentJobs.AdvanceTo(now+1);disk.Reset();new SaveRepository(disk).Save(state);Only(disk,"AT-Residents",check);
        check(disk.Bytes<1400&&economic0.SequenceEqual(disk.Files["AT-Shops0"])&&economic1.SequenceEqual(disk.Files["AT-Shops1"]),"a dated-second checkpoint preserves both full-mail economic records byte for byte");
        var loaded=new SaveRepository(disk).Load()!;check(loaded.Town.Jobs.Watermark==now+1&&loaded.MailCount==128,"the independent dated-second watermark and full inbox reload together");
        loaded.ResidentJobs.Enable(loaded.Town.Jobs.TownId,now+1);check(loaded.ResidentJobs.ReadLetter(127)==body,"full Unicode mail remains exact after a clock-only save");
        var split=SplitSaveCodec.Encode(state);split[2]=IncrementalSaveCodec.Encode(state,2);check(Fails(()=>SplitSaveCodec.Decode(split)),"a projected economy cannot be decoded without its committed clock record");
        for(int mode=0;mode<3;mode++)for(int cut=1;cut<=4;cut++){
            var fault=original.Copy();fault.Reset();fault.Mode=mode;fault.CutAt=cut;var candidate=SaveCodec.Decode(before);candidate.ResidentJobs.Enable(candidate.Town.Jobs.TownId,now+1);
            check(Fails(()=>new SaveRepository(fault).Save(candidate)),"dated-second checkpoint interruption reaches each actual write");
            var recovered=new SaveRepository(fault).Load()!;bool published=cut>2||cut==2&&mode==2;
            Same(recovered,published?state:SaveCodec.Decode(before),check,"clock and mail recover as one complete old or new snapshot");
            fault.CutAt=0;new SaveRepository(fault).Save(candidate);check(new SaveRepository(fault).Load()!.Town.Jobs.Watermark==now+1,"dated-second checkpoint retry restores the exact watermark");
        }
        Console.WriteLine("Offline job clock: 1,336 encoded bytes in four logical records/checkpoint with 128 full Unicode letters; zero economic/mail rewrites; every publication cut and retry passed.");
    }
}
