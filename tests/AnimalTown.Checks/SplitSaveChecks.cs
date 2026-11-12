using AnimalTown;

internal static class SplitSaveChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(7319){Sequence=42,Wallet=1234,Name="Juniper",Species=3,Palette=5,Roof=2,Savings=9876543210};s.MailCount=2;s.Mail[0]=4;s.Mail[1]=-3;
        s.Town.Residents[0].Name="Savedfox";s.Town.Residents[0].Friendship=73;s.Town.Residents[0].PosX=12.125f;s.Town.Stock[0]=1;s.Town.Stock[2]=0;s.Town.Spending=777;
        var parts=SplitSaveCodec.Encode(s);var restored=SplitSaveCodec.Decode(parts);
        check(SaveCodec.Encode(s).SequenceEqual(SaveCodec.Encode(restored)),"all fields round-trip through shared records and four profiles");
        var commit=SplitSaveCodec.EncodeCommit(s,parts);check(SplitSaveCodec.DecodeCommitted(commit,parts).Wallet==1234,"commit binds complete save set");
        long allocated=GC.GetAllocatedBytesForCurrentThread();SplitSaveCodec.VerifyCommitted(commit,parts);
        check(GC.GetAllocatedBytesForCurrentThread()-allocated<16384,"final commit verification fits a small allocation budget without reconstructing a town");
        void Reject(Action operation,string reason){bool rejected=false;try{operation();}catch{rejected=true;}check(rejected,reason);}
        for(int kind=0;kind<SplitSaveCodec.PartCount;kind++){
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
        IncrementalSaveChecks.Run(check);
        Console.WriteLine("Split save records, ownership, corruption, interrupted-write fallback and legacy migration checks passed");
    }
}
