using AnimalTown;
using AnimalTown.Jobs;
using System.Security.Cryptography;

// These fixtures were encoded by the original commits, not by this decoder's
// matching writer. See Fixtures/README.md before changing their pinned bytes.
internal static class SaveFormatCompatibilityChecks
{
    private const long JobTime=1796904000;
    private static byte[] Fixture(string name,string sha)
    {
        var bytes=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures",name));
        if(!Convert.ToHexString(SHA256.HashData(bytes)).Equals(sha,StringComparison.OrdinalIgnoreCase))throw new Exception("Historical fixture changed: "+name);
        return bytes;
    }
    private static byte[] SnowFixture()=>Fixture("snow-main-v14-catalog1.bin","7802ae2429edfccff92c920b5fcace3b7517fc1e965d337e7f435e04d7c2518e");
    private static byte[] JobsFixture()=>Fixture("jobs-pr136-v14-catalog2.bin","28f7707602f4fc98008bb7afbf1100d1e2cf5bb4f01a48194c02795497ddc0e5");
    private static bool Fails(Action action){try{action();return false;}catch(InvalidOperationException){return true;}catch(ArgumentException){return true;}}
    private static void Put(byte[] bytes,int at,int value){for(int i=0;i<4;i++)bytes[at+i]=(byte)((uint)value>>(8*i));}
    private static void RepairCombinedHeader(byte[] bytes){Put(bytes,16,bytes.Length);Put(bytes,20,unchecked((int)SaveCodec.Crc(bytes,24,bytes.Length-24)));}
    private static GameState Jobs()=>SaveCodec.Decode(JobsFixture());
    private static void AddSnow(GameState state)
    {
        state.Town.Traffic.SetWinter(true);var snow=state.Town.Traffic.Snow!;
        for(int i=0;i<SnowWear.MaxVisits;i++)state.Town.Traffic.Enter(state.Town,20,30);
        for(int i=0;i<(SnowWear.RecoverySeconds*2/3)*30-1;i++)snow.TickSnowfall(true);
        state.Town.Traffic.Enter(state.Town,21,30);
    }
    private static void Same(GameState expected,GameState actual,Action<bool,string> check,string label)
    {check(SaveCodec.Encode(expected).SequenceEqual(SaveCodec.Encode(actual)),label);}
    internal static void Run(Action<bool,string> check)
    {
        Historical(check);PreKoban(check);Combined(check);Incremental(check);
        Console.WriteLine("Historical snow/job v14 and split-v10 migration, combined v16, split v11 and incremental compatibility checks passed");
    }
    private static void Historical(Action<bool,string> check)
    {
        var snowBytes=SnowFixture();var jobBytes=JobsFixture();
        check(BitConverter.ToInt32(snowBytes,4)==14&&BitConverter.ToInt32(snowBytes,8)==1&&BitConverter.ToInt32(jobBytes,4)==14&&BitConverter.ToInt32(jobBytes,8)==2,"historical v14 fixtures retain their distinct original catalog identities");
        var snowState=SaveCodec.Decode(snowBytes);var snow=snowState.Town.Traffic.Snow!;
        check(snowState.Sequence==17&&snowState.Name=="Juniper"&&snowState.Wallet==1234&&snowState.Profiles[1].Name=="Milo"&&snowState.Profiles[1].Wallet==321&&snowState.Town.DaylightLocation==1,"main's catalog-1 snow save preserves both profiles, money and daylight");
        int at=(30*Town.Width+20)/2;
        check(snow.Winter&&snow.Packed[at]==0x3e&&snow.Packed.Count(b=>b!=0)==1&&!snowState.Town.Jobs.Initialized,"main's v14 snow extension restores its dents using the current recovery duration");
        snowState.SelectProfile(1);check(snowState.Town.Traffic.Snow!.Packed[at]==0x3e,"historical snow remains shared after selecting another profile");
        snow.TickSnowfall(true);check(snow.Packed[at]==0x3e&&snow.SnowfallTicks==30,"historical snow preserves the partial second and uses seven-minute recovery");
        snow.ResetForTownEntry();check(snow.Packed.All(b=>b==0),"re-entering the town clears migrated historical dents");
        var aged=new SnowWear();
        aged.RestoreCheckpoint(new[]{100,0,2,((0x8000|(Town.Cells-1))<<16)|(599<<4)|15});
        check(aged.VisitCount(0,0)==1,"a recovery age from the old ten-minute window loads as a settled shallow dent");
        var jobs=SaveCodec.Decode(jobBytes);var ledger=jobs.Town.Jobs;
        var active=Enumerable.Range(0,OfflineJobLedger.JobCapacity).Select(ledger.Job).Where(j=>j.Occupying).ToArray();
        check(jobs.Sequence==27&&ledger.TownId==12345&&ledger.Watermark==JobTime&&active.Length==1&&active[0].Id.Serial==9&&active[0].Template==JobTemplate.DeliveryClothing&&active[0].Item==Catalog.WorkShirt,"PR136's catalog-2 v14 save retains its accepted delivery identity, payload and clock");
        check(Enumerable.Range(0,jobs.Capacity).Count(jobs.IsJobItem)==1&&!jobs.Town.Traffic.Winter,"legacy job decoding retains the protected parcel without interpreting its ledger as snow");
        var mislabeled=(byte[])snowBytes.Clone();Put(mislabeled,8,2);check(Fails(()=>SaveCodec.Decode(mislabeled)),"catalog-2 v14 does not silently accept a catalog-1 snow extension");
        mislabeled=(byte[])jobBytes.Clone();Put(mislabeled,8,1);check(Fails(()=>SaveCodec.Decode(mislabeled)),"catalog-1 v14 does not silently accept a catalog-2 job extension");
        var splitBytes=Fixture("snow-main-split-v10-catalog1.bin","a075d7aaaff998f52b4592203f1d67d5a74e7f4db09c68843c057babf0200eaa");
        using var stream=new MemoryStream(splitBytes);using var reader=new BinaryReader(stream);
        int count=reader.ReadInt32();check(count==SplitSaveCodec.PartCount,"historical split fixture contains all seven original records");
        var records=new byte[count][];
        for(int i=0;i<count;i++){int size=reader.ReadInt32();records[i]=reader.ReadBytes(size);if(records[i].Length!=size)throw new Exception("Truncated historical split fixture");}
        check(stream.Position==stream.Length&&SplitSaveCodec.ReadVersion(records[0])==10,"historical split fixture retains its snow-v10 town record");
        Same(SaveCodec.Decode(snowBytes),SplitSaveCodec.Decode(records),check,"main's original split-v10 records restore the same complete snow snapshot as its monolithic save");
    }
    private static void PreKoban(Action<bool,string> check)
    {
        var combined=Fixture("pre-koban-main-v15.bin","87a93b3bdd28a36c57e4a9a82dec6b0b353f47087e836dd81787f0843b0c3217");
        var split=Fixture("pre-koban-main-split-v10.bin","3d27fb54519efd79c77da2dc7c2766f988353f8a410230cc0b347710888ec8b5");
        using var stream=new MemoryStream(split);using var reader=new BinaryReader(stream);
        int count=reader.ReadInt32();check(count==SplitSaveCodec.PartCount,"pre-koban fixture contains all original shared records");
        var records=new byte[count][];for(int i=0;i<count;i++)records[i]=reader.ReadBytes(reader.ReadInt32());
        check(stream.Position==stream.Length&&combined[4]==15&&SplitSaveCodec.ReadVersion(records[0])==10,"pre-koban fixtures retain the actual main v15 and split-v10 layouts");
        var a=SaveCodec.Decode(combined);var b=SplitSaveCodec.Decode(records);
        foreach(var s in new[]{a,b}){
            int ground=s.Town.FindGround(40,25);
            check(s.Sequence==23&&s.Name=="Amber"&&s.Wallet==444&&s.Profiles[1].Name=="Milo"&&s.Profiles[1].Wallet==222&&s.Space==16,"pre-koban main saves preserve resident profiles and the occupied original room");
            check(s.Town.Jobs.TownId==2222008&&s.Town.Traffic.Snow!.Packed.Any(v=>v!=0)&&s.Town.Paths.At(41,26)==1,"pre-koban migration preserves simultaneous jobs, snow and painted paths");
            check(s.Town.Koban.Enabled&&!s.Town.Koban.Contains(40,25)&&ground>=0&&s.Town.Ground[ground].Item==10&&s.Town.LostAndFound.Count==1,"migration adds the koban without replacing the original item or pre-existing custody");
        }
        Same(a,b,check,"original combined-v15 and split-v10 sources migrate to identical current towns");
    }
    private static void Combined(Action<bool,string> check)
    {
        var both=Jobs();AddSnow(both);
        foreach(var state in new[]{SaveCodec.Decode(SnowFixture()),Jobs(),both}){
            var bytes=SaveCodec.Encode(state);
            check(BitConverter.ToInt32(bytes,4)==16&&BitConverter.ToInt32(bytes,8)==Catalog.Version,"new saves keep independent snow/job fields and the koban extension in schema 16");
            Same(state,SaveCodec.Decode(bytes),check,"schema 16 preserves every field with either optional extension absent or both present");
            var broken=bytes[..^4];RepairCombinedHeader(broken);check(Fails(()=>SaveCodec.Decode(broken)),"a checksum-valid truncated optional extension is rejected");
            broken=(byte[])bytes.Clone();Put(broken,4,17);check(Fails(()=>SaveCodec.Decode(broken)),"unknown future combined schemas are rejected");
        }
        var split=SplitSaveCodec.Encode(both);
        check(SplitSaveCodec.ReadVersion(split[0])==11,"snow and the koban extension use split-v11 while the economy ledger remains independent");
        Same(both,SplitSaveCodec.DecodeCommitted(SplitSaveCodec.EncodeCommit(both,split),split),check,"combined snow and jobs survive a committed seven-record server snapshot");
        var sourceSnow=both.Town.Traffic.Snow!;var checkpoint=sourceSnow.EncodeCheckpoint()!;
        byte[] packed=(byte[])sourceSnow.Packed.Clone();int revision=sourceSnow.Revision;
        var validated=SaveCodec.ValidateSnapshot(SaveCodec.Encode(both),both.Town);
        check(!ReferenceEquals(validated.Town.Traffic,both.Town.Traffic)&&!ReferenceEquals(validated.Town.Traffic.Snow,sourceSnow),"combined readback validation owns fresh mutable traffic and snow while borrowing static terrain");
        check(sourceSnow.Revision==revision&&sourceSnow.Packed.SequenceEqual(packed)&&sourceSnow.EncodeCheckpoint()!.SequenceEqual(checkpoint),"combined readback validation preserves the live snow checkpoint, pixels and revision");
        SplitSaveCodec.ValidateSnapshot(SplitSaveCodec.EncodeCommit(both,split),split,both.Town);
        check(sourceSnow.Revision==revision&&sourceSnow.Packed.SequenceEqual(packed)&&sourceSnow.EncodeCheckpoint()!.SequenceEqual(checkpoint),"split readback validation also leaves the live snow checkpoint and revision untouched");
        check(Fails(()=>SaveCodec.EncodeLegacy(both))&&Fails(()=>SplitSaveCodec.EncodeLegacy(both)),"single-profile legacy export cannot silently discard combined jobs and snow");
    }
    private sealed class Disk:ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal readonly List<string> Names=new();
        internal bool FailCommit;
        public byte[] Read(string name)=>(byte[])Files[name].Clone();
        public void Write(string name,byte[] bytes){if(FailCommit&&name.StartsWith("AT-Commit",StringComparison.Ordinal))throw new IOException("Intentional failure before publication");Names.Add(name);Files[name]=(byte[])bytes.Clone();}
        internal Disk Copy(){var copy=new Disk();foreach(var file in Files)copy.Files[file.Key]=(byte[])file.Value.Clone();return copy;}
    }
    private static bool Only(Disk disk,string kind)=>disk.Names.Count==4&&disk.Names.Count(n=>n==kind+"0"||n==kind+"1")==2&&disk.Names.Count(n=>n=="AT-Commit0"||n=="AT-Commit1")==2;
    private static void Incremental(Action<bool,string> check)
    {
        var state=Jobs();AddSnow(state);
        var service=new VillagerJobService(state);service.Enable(state.Town.Jobs.TownId,JobTime);
        var disk=new Disk();var repository=new SaveRepository(disk);repository.Save(state);
        Same(state,repository.Load()!,check,"incremental v10 projection restores both the snow checkpoint and job runtime clock");
        disk.Names.Clear();int sequence=state.Sequence;repository.Save(state);
        check(disk.Names.Count==0&&state.Sequence==sequence,"an unchanged combined snow/job save is still a true no-op");
        byte[] economy=(byte[])disk.Files["AT-Shops0"].Clone();byte[] town=(byte[])disk.Files["AT-Town0"].Clone();
        service.AdvanceTo(JobTime+1);repository.Save(state);
        check(Only(disk,"AT-Residents")&&disk.Files["AT-Town0"].SequenceEqual(town)&&disk.Files["AT-Shops0"].SequenceEqual(economy),"a job-clock tick with snow present changes only runtime records and manifests");
        Same(state,repository.Load()!,check,"the projected job clock reloads alongside unchanged snow and economy records");
        disk.Names.Clear();byte[] runtime=(byte[])disk.Files["AT-Residents0"].Clone();state.Town.Traffic.Snow!.TickSnowfall(true);repository.Save(state);
        check(Only(disk,"AT-Town")&&disk.Files["AT-Residents0"].SequenceEqual(runtime)&&disk.Files["AT-Shops0"].SequenceEqual(economy),"snow recovery with an accepted job changes only town records and manifests");
        Same(state,repository.Load()!,check,"independent snow recovery reloads without losing job identities, bindings or the runtime watermark");
        var interrupted=disk.Copy();interrupted.FailCommit=true;var pending=SaveCodec.Decode(SaveCodec.Encode(state));
        pending.Town.Traffic.Snow!.TickSnowfall(true);var pendingService=new VillagerJobService(pending);pendingService.Enable(pending.Town.Jobs.TownId,JobTime+2);
        bool failed=false;try{new SaveRepository(interrupted).Save(pending);}catch(IOException){failed=true;}
        check(failed,"a combined update reports an interruption before manifest publication");
        Same(state,new SaveRepository(interrupted).Load()!,check,"a failed combined update cannot mix new snow or job-clock records into the prior snapshot");
    }
}
