using AnimalTown;
using AnimalTown.Jobs;

internal static class CheckpointExportChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(22651){Name="Juniper",Wallet=4200,Tutorial=7};
        long now=JobCalendar.At(2027,7,15,12);
        state.ResidentJobs.Enable(0xA012345678901234UL,now);
        var disk=new Disk();
        var repository=new SaveRepository(disk);
        repository.Save(state);
        state.Wallet++;
        state.Town.Residents[0].Dwell=34;
        state.ResidentJobs.Enable(state.Town.Jobs.TownId,now+33);
        repository.Save(state);
        int writes=disk.Writes;

        byte[][] canonical=SplitSaveCodec.Encode(state);
        byte[] canonicalCommit=SplitSaveCodec.EncodeCommit(state,canonical);
        using(var cursor=repository.OpenCheckpointExport(state))
        {
            var buffer=new byte[521];
            check(cursor.Read(buffer,0,0)==0&&cursor.Position==0,"Zero-sized card upload read leaves the position unchanged");
            using var envelope=new MemoryStream();
            int count;
            while((count=cursor.Read(buffer,3,envelope.Length<8?1:buffer.Length-5))!=0)
                envelope.Write(buffer,3,count);
            check(cursor.Position==cursor.Length&&envelope.Length==cursor.Length,"Card export reaches the declared envelope length");
            check(cursor.Read(buffer,0,buffer.Length)==0,"Card export stays at EOF");
            envelope.Position=0;
            using var reader=new BinaryReader(envelope);
            byte[] commit=reader.ReadBytes(reader.ReadInt32());
            var records=new byte[SplitSaveCodec.PartCount][];
            for(int i=0;i<records.Length;i++)records[i]=reader.ReadBytes(reader.ReadInt32());
            check(envelope.Position==envelope.Length&&commit.SequenceEqual(canonicalCommit),
                "The export emits the expected little-endian lengths and canonical commit");
            for(int i=0;i<records.Length;i++)check(records[i].SequenceEqual(canonical[i]),"Card upload emits canonical split record "+i);
            var decoded=SplitSaveCodec.DecodeCommitted(commit,records);
            check(decoded.Sequence==state.Sequence&&decoded.Town.Residents[0].Dwell==34&&decoded.Wallet==4201&&
                decoded.Town.Jobs.TownId==state.Town.Jobs.TownId&&decoded.Town.Jobs.Watermark==state.Town.Jobs.Watermark,
                "Card upload restores the exact committed town and projected runtime fields");
        }
        check(disk.Writes==writes,"Export leaves both save banks untouched");
        var streamed=new StreamingDisk(disk);
        using(var cursor=new SaveRepository(streamed).OpenCheckpointExport(state))
            check(cursor.Length>canonicalCommit.Length&&streamed.Verifications==8,
                "Card-capable storage verifies each referenced record without retaining a second record array");

        var legacy=new Disk();
        for(int i=0;i<canonical.Length;i++)legacy.Write(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),0),canonical[i]);
        legacy.Write(SplitSaveCodec.FileName(SplitSaveCodec.Commit,0),canonicalCommit);
        using(var cursor=new SaveRepository(legacy).OpenCheckpointExport(state))
            check(cursor.Length==canonicalCommit.Length+canonical.Sum(record=>record.Length)+32,
                "A canonical seven-record commit can also be verified and exported");

        state.Wallet++;
        check(Rejects(()=>repository.OpenCheckpointExport(state).Dispose()),"Export refuses unsaved inventory edits at the same card sequence");
        state.Wallet--;
        state.Sequence--;
        check(Rejects(()=>repository.OpenCheckpointExport(state).Dispose()),"Export refuses a stale selected sequence");
        state.Sequence++;

        using(var changed=repository.OpenCheckpointExport(state))
        {
            state.Wallet++;
            var buffer=new byte[512];
            check(Rejects(()=>{while(changed.Read(buffer,0,buffer.Length)!=0){}}),
                "A world changed after export preparation is refused before emitting the changed component");
            state.Wallet--;
        }
        var missing=new SaveRepository(new Disk());
        check(Rejects(()=>missing.OpenCheckpointExport(state).Dispose()),"Export refuses a town without a committed card");
        string corrupt=SplitSaveCodec.FileName(SplitSaveCodec.TownData,0);
        disk.Files[corrupt][32]^=1;
        using(var cursor=repository.OpenCheckpointExport(state))
            check(cursor.Length>canonicalCommit.Length,"Export accepts a valid mirrored card record");
        disk.Files[SplitSaveCodec.FileName(SplitSaveCodec.TownData,1)][32]^=1;
        check(Rejects(()=>repository.OpenCheckpointExport(state).Dispose()),"Export rejects a corrupted card before exposing bytes");
    }

    private static bool Rejects(Action work)
    {
        try{work();return false;}
        catch(InvalidOperationException){return true;}
    }

    private sealed class StreamingDisk(Disk disk) : ISaveStorage,ISaveSnapshotStorage
    {
        internal int Verifications;
        public byte[] Read(string name)
        {
            if(!name.StartsWith("AT-Commit"))throw new InvalidOperationException("Export must verify this record with bounded storage");
            return disk.Read(name);
        }
        public void Write(string name,byte[] bytes)=>throw new InvalidOperationException("Export is read-only");
        public bool Matches(string name,byte[]? expected,int sequence,int length,uint checksum,bool ignoreSequence)
        {
            Verifications++;
            if(!disk.Files.TryGetValue(name,out var bytes)||bytes.Length!=length||expected!=null&&expected.Length!=length)
                return false;
            if(sequence>=0){
                int found=bytes[16]|bytes[17]<<8|bytes[18]<<16|bytes[19]<<24;
                if(found!=sequence||SaveCodec.Crc(bytes,0,length-4)!=checksum)return false;
            }
            if(expected!=null)for(int i=0;i<length;i++){
                if(ignoreSequence&&(i>=16&&i<20||i>=length-4))continue;
                if(bytes[i]!=expected[i])return false;
            }
            return true;
        }
    }

    private sealed class Disk : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal int Writes;
        public byte[] Read(string name)=>(byte[])Files[name].Clone();
        public void Write(string name,byte[] bytes){Files[name]=(byte[])bytes.Clone();Writes++;}
    }
}
