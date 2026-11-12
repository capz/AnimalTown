using AnimalTown;

internal static class IncrementalSaveChecks
{
    private sealed class Disk : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal readonly List<string> Names=new();
        internal int CutAt,Mode,Bytes;
        internal string? CorruptAfterWrite;
        public byte[] Read(string name){var a=(byte[])Files[name].Clone();if(name==CorruptAfterWrite&&Names.Contains(name)){a[32]^=1;uint crc=SaveCodec.Crc(a,0,a.Length-4);for(int i=0;i<4;i++)a[a.Length-4+i]=(byte)(crc>>(i*8));}return a;}
        public void Write(string name,byte[] bytes){Names.Add(name);Bytes+=bytes.Length;bool cut=Names.Count==CutAt;if(cut&&Mode==0)throw new IOException("Before write");Files[name]=cut&&Mode==1?bytes[..(bytes.Length/2)]:(byte[])bytes.Clone();if(cut)throw new IOException("Interrupted write");}
        internal Disk Copy(){var d=new Disk();foreach(var f in Files)d.Files[f.Key]=(byte[])f.Value.Clone();return d;}
        internal void Reset(){Names.Clear();Bytes=0;CutAt=0;CorruptAfterWrite=null;}
    }
    private static GameState Town(){var s=new GameState(7319){Name="Juniper",Wallet=1234};Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);s.Town.GroundCount=0;return s;}
    private static bool Fails(Action action){try{action();return false;}catch(Exception){return true;}}
    private static void Same(GameState a,GameState b,Action<bool,string> check,string label)=>check(SaveCodec.Encode(a).SequenceEqual(SaveCodec.Encode(b)),label);
    private static bool Kind(string name,string prefix)=>name==prefix+"0"||name==prefix+"1";
    private static void Only(Disk disk,string kind,Action<bool,string> check)
    {check(disk.Names.Count==4&&disk.Names.Count(n=>Kind(n,kind))==2&&disk.Names.Count(n=>Kind(n,"AT-Commit"))==2,"only changed component and both reference heads are written: "+kind);}
    internal static void Run(Action<bool,string> check)
    {
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
        d.Reset();s.Town.Residents[0].HomeX++;repo.Save(s);Only(d,"AT-Houses",check);
        // Reload/recreate the repository: tracking must not depend on a long-lived
        // in-memory dirty set, and inactive residents retain their own records.
        s=repo.Load()!;s.SelectProfile(1);s.Name="Milo";d.Reset();new SaveRepository(d).Save(s);Same(s,new SaveRepository(d).Load()!,check,"new profile and active selection round-trip without regenerating the town");
        s.SelectProfile(0);d.Reset();new SaveRepository(d).Save(s);Only(d,"AT-Residents",check);
        var before=SaveCodec.Encode(s);var same=new SaveRepository(d).Load()!;d.Reset();new SaveRepository(d).Save(same);check(d.Names.Count==0,"a freshly loaded snapshot does not rewrite records");
        // A failed write at every real phase must leave a complete old/new
        // snapshot. The first manifest publication is the atomic boundary.
        var baseDisk=d.Copy();var baseline=SaveCodec.Decode(before);
        var desired=SaveCodec.Decode(before);desired.Wallet+=5;desired.Town.Spending++;desired.Town.Residents[0].Dwell++;
        var measured=baseDisk.Copy();new SaveRepository(measured).Save(desired);int writes=measured.Names.Count,publication=measured.Names.FindIndex(n=>Kind(n,"AT-Commit"))+1;
        check(writes==8,"compound save writes only three changed pairs and two manifests");
        for(int mode=0;mode<3;mode++)for(int cut=1;cut<=writes;cut++){
            var fault=baseDisk.Copy();fault.Mode=mode;fault.CutAt=cut;var candidate=SaveCodec.Decode(before);candidate.Wallet+=5;candidate.Town.Spending++;candidate.Town.Residents[0].Dwell++;
            check(Fails(()=>new SaveRepository(fault).Save(candidate))&&candidate.Sequence==baseline.Sequence,"every interrupted update reports failure without changing the live sequence");
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
        check(Fails(()=>new SaveRepository(changed).Save(c)),"changed record readback rejects altered content even with a recomputed CRC");Same(baseline,new SaveRepository(changed).Load()!,check,"readback failure preserves the previous publication");
        // Import old combined and v3/v4 split saves without changing source data.
        foreach(int format in new[]{0,3,4}){
            var disk=new Disk();var old=Town();old.Sequence=7;
            if(format==0)disk.Files["AnimalTown1"]=SaveCodec.Encode(old);
            else{var p=format==3?SplitSaveCodec.EncodeLegacy(old):SplitSaveCodec.Encode(old);for(int i=0;i<p.Length;i++)disk.Files[SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),1)]=p[i];disk.Files["AT-Commit1"]=SplitSaveCodec.EncodeCommit(old,p);}
            var loaded=new SaveRepository(disk).Load()!;Same(old,loaded,check,"old save remains readable before migration");var preserved=format==0?(byte[])disk.Files["AnimalTown1"].Clone():null;
            new SaveRepository(disk).Save(loaded);Same(loaded,new SaveRepository(disk).Load()!,check,"migration preserves all existing progress and terrain");if(preserved!=null)check(preserved.SequenceEqual(disk.Files["AnimalTown1"]),"combined migration source is retained unchanged");
            disk.Reset();new SaveRepository(disk).Save(loaded);check(disk.Names.Count==0,"migrated town immediately benefits from zero-write saves");
        }
        var dense=Town();for(int i=0;i<dense.Town.Tiles.Length;i++)dense.Town.Tiles[i]=(byte)(i%2==0?Terrain.Grass:Terrain.Plaza);
        var denseDisk=new Disk();new SaveRepository(denseDisk).Save(dense);int townBytes=denseDisk.Files["AT-Town0"].Length;
        denseDisk.Reset();dense.Wallet++;new SaveRepository(denseDisk).Save(dense);Only(denseDisk,"AT-Player",check);
        check(townBytes>64000&&denseDisk.Bytes<4096,"a player update leaves a large edited terrain record unwritten");Same(dense,new SaveRepository(denseDisk).Load()!,check,"large edited terrain survives small incremental saves");
        var stale=baseDisk.Copy();var fresh=new SaveRepository(stale).Load()!;fresh.Wallet++;new SaveRepository(stale).Save(fresh);stale.Reset();
        check(Fails(()=>new SaveRepository(stale).Save(SaveCodec.Decode(before)))&&stale.Names.Count==0,"an older different snapshot cannot overwrite a newer save");
        check(new SaveRepository(new Disk()).Load()==null,"empty card does not invent a save");
        Console.WriteLine("Incremental saves: exact component writes/no-op, static town vs NPC runtime, mirrored references, corruption, power cuts, retries and migration passed");
    }
    private static int ReferenceBank(Disk d,int sequence,int part)
    {var b=d.Files["AT-Commit0"];int at=28+part*20+4;return b[at]|b[at+1]<<8|b[at+2]<<16|b[at+3]<<24;}
}
