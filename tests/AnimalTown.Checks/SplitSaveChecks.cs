using AnimalTown;

internal static class SplitSaveChecks
{
    private sealed class Storage : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal int CutAt,Mode,Writes;
        public byte[] Read(string name)=>(byte[])Files[name].Clone();
        public void Write(string name,byte[] bytes)
        {
            Writes++;bool cut=Writes==CutAt;
            if(cut&&Mode==0)throw new IOException("Power cut before write");
            Files[name]=cut&&Mode==1?bytes[..(bytes.Length/2)]:(byte[])bytes.Clone();
            if(cut)throw new IOException("Power cut during/after write");
        }
        internal Storage Copy(){var result=new Storage();foreach(var p in Files)result.Files[p.Key]=(byte[])p.Value.Clone();return result;}
    }
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(7319){Sequence=42,Wallet=1234,Name="Juniper",Species=3,Palette=5,Roof=2,Savings=9876543210};s.MailCount=2;s.Mail[0]=4;s.Mail[1]=-3;
        s.Town.Residents[0].Name="Savedfox";s.Town.Residents[0].Friendship=73;s.Town.Residents[0].PosX=12.125f;s.Town.Stock[0]=1;s.Town.Stock[2]=0;s.Town.Spending=777;
        var parts=SplitSaveCodec.Encode(s);var restored=SplitSaveCodec.Decode(parts);
        check(SaveCodec.Encode(s).SequenceEqual(SaveCodec.Encode(restored)),"all fields round-trip through four separate records");
        var commit=SplitSaveCodec.EncodeCommit(s,parts);check(SplitSaveCodec.DecodeCommitted(commit,parts).Wallet==1234,"commit binds complete save set");
        void Reject(Action operation,string reason){bool rejected=false;try{operation();}catch{rejected=true;}check(rejected,reason);}
        for(int kind=0;kind<4;kind++){
            var broken=parts.Select(p=>(byte[])p.Clone()).ToArray();broken[kind][32]^=1;Reject(()=>SplitSaveCodec.DecodeCommitted(commit,broken),"corrupt part rejected");
            broken=parts.Select(p=>(byte[])p.Clone()).ToArray();broken[kind]=broken[kind][..^1];Reject(()=>SplitSaveCodec.Decode(broken),"truncated part rejected");
        }
        var swapped=(byte[][])parts.Clone();swapped[1]=parts[2];Reject(()=>SplitSaveCodec.Decode(swapped),"wrong record kind rejected");
        s.Sequence++;var newer=SplitSaveCodec.Encode(s);newer[0]=parts[0];Reject(()=>SplitSaveCodec.Decode(newer),"mixed generations rejected");s.Sequence--;
        var foreign=SplitSaveCodec.Encode(new GameState(123){Sequence=42});foreign[0]=parts[0];Reject(()=>SplitSaveCodec.Decode(foreign),"mixed towns rejected");
        s.Wallet++;var changed=SplitSaveCodec.Encode(s);Reject(()=>SplitSaveCodec.DecodeCommitted(commit,changed),"commit rejects valid but different same-sequence payload");
        check(parts[0].SequenceEqual(changed[0])&&parts[1].SequenceEqual(changed[1])&&parts[2].SequenceEqual(changed[2])&&!parts[3].SequenceEqual(changed[3]),"wallet resides only in player record");s.Wallet--;
        s.Town.Stock[0]=0;changed=SplitSaveCodec.Encode(s);check(parts[0].SequenceEqual(changed[0])&&parts[1].SequenceEqual(changed[1])&&!parts[2].SequenceEqual(changed[2])&&parts[3].SequenceEqual(changed[3]),"stock resides only in shop record");s.Town.Stock[0]=restored.Town.Stock[0];
        s.Town.Residents[0].HomeX++;changed=SplitSaveCodec.Encode(s);check(parts[0].SequenceEqual(changed[0])&&!parts[1].SequenceEqual(changed[1])&&parts[2].SequenceEqual(changed[2])&&parts[3].SequenceEqual(changed[3]),"house anchors reside only in house-position record");s.Town.Residents[0].HomeX--;
        s.Town.Tiles[0]=(byte)((s.Town.Tiles[0]+1)%8);changed=SplitSaveCodec.Encode(s);check(!parts[0].SequenceEqual(changed[0])&&parts[1].SequenceEqual(changed[1])&&parts[2].SequenceEqual(changed[2])&&parts[3].SequenceEqual(changed[3]),"terrain resides only in town record");s.Town.Tiles[0]=restored.Town.Tiles[0];
        var storage=new Storage();var repository=new SaveRepository(storage);s.Sequence=0;repository.Save(s);s.Wallet=2000;repository.Save(s);
        for(int mode=0;mode<3;mode++)for(int cut=1;cut<=5;cut++){
            var disk=storage.Copy();disk.Mode=mode;disk.CutAt=cut;var game=SaveCodec.Decode(SaveCodec.Encode(s));game.Wallet=3000;game.Town.Stock[0]=0;
            Reject(()=>new SaveRepository(disk).Save(game),"injected interrupted write reports failure");check(game.Sequence==2,"failed write restores in-memory sequence");
            var recovered=new SaveRepository(disk).Load();bool committed=mode==2&&cut==5;
            check(recovered!=null&&recovered.Sequence==(committed?3:2)&&recovered.Wallet==(committed?3000:2000)&&recovered.Town.Stock[0]==(committed?0:s.Town.Stock[0]),"power-cut recovery uses one whole generation");
        }
        for(int kind=0;kind<5;kind++){
            var disk=storage.Copy();disk.Files.Remove(SplitSaveCodec.FileName(kind,0));var recovered=new SaveRepository(disk).Load();check(recovered!=null&&recovered.Sequence==1&&recovered.Wallet==1234,"missing record falls back to previous complete bank");
        }
        var legacy=new Storage();legacy.Files["AnimalTown1"]=SaveCodec.Encode(s);var imported=new SaveRepository(legacy).Load()!;check(imported.Sequence==2&&imported.Wallet==2000,"legacy combined save imports");new SaveRepository(legacy).Save(imported);
        check(legacy.Files.ContainsKey("AnimalTown1")&&new SaveRepository(legacy).Load()!.Sequence==3,"migration preserves original legacy file and selects split save");
        check(new SaveRepository(new Storage()).Load()==null,"empty card starts new town");
        Console.WriteLine("Split save records, ownership, corruption, interrupted-write fallback and legacy migration checks passed");
    }
}
