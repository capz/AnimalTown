namespace AnimalTown;

public interface ISaveStorage
{
    byte[] Read(string name);
    void Write(string name,byte[] bytes);
}

// A bank is visible only after all four records pass readback and the commit
// record is written. Never combine records from different banks/generations.
public sealed class SaveRepository
{
    private readonly ISaveStorage storage;
    public SaveRepository(ISaveStorage storage){this.storage=storage;}
    private static void Match(byte[] actual,byte[] expected)
    {if(actual.Length!=expected.Length)throw new InvalidOperationException("Save readback length differs");for(int i=0;i<actual.Length;i++)if(actual[i]!=expected[i])throw new InvalidOperationException("Save readback differs from snapshot");}
    private byte[][] ReadParts(int bank,byte[][]? expected=null)
    {var town=storage.Read(SplitSaveCodec.FileName(0,bank));var parts=new byte[SplitSaveCodec.ReadVersion(town)>=4?SplitSaveCodec.PartCount:4][];parts[0]=town;if(expected!=null)Match(town,expected[0]);for(int i=1;i<parts.Length;i++){parts[i]=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),bank));if(expected!=null)Match(parts[i],expected[i]);}return parts;}
    // Separate native GC frames release readback/validation temporaries before
    // the final verification. No decoded town or card buffer escapes either call.
    private void ValidateReadback(int bank,byte[][] parts,byte[] commit,Town snapshot)
    {SplitSaveCodec.ValidateSnapshot(commit,ReadParts(bank,parts),snapshot);}
    private void VerifyReadback(int bank,byte[][] parts,byte[] commit)
    {var durable=storage.Read(SplitSaveCodec.FileName(4,bank));Match(durable,commit);SplitSaveCodec.VerifyCommitted(durable,ReadParts(bank,parts));}
    public GameState? Load()
    {
        GameState? best=null;
        for(int bank=0;bank<2;bank++)try{var s=SplitSaveCodec.DecodeCommitted(storage.Read(SplitSaveCodec.FileName(4,bank)),ReadParts(bank));if(best==null||s.Sequence>best.Sequence)best=s;}catch(Exception){}
        // Legacy data is read-only migration input and is never overwritten.
        for(int bank=0;bank<2;bank++)try{var s=SaveCodec.Decode(storage.Read("AnimalTown"+bank));if(best==null||s.Sequence>best.Sequence)best=s;}catch(Exception){}
        return best;
    }
    public void Save(GameState state)
    {
        int previous=state.Sequence;if(previous==int.MaxValue)throw new InvalidOperationException("Save sequence exhausted");state.Sequence++;
        try{
            int bank=state.Sequence%2;var parts=SplitSaveCodec.Encode(state);var commit=SplitSaveCodec.EncodeCommit(state,parts);
            for(int i=0;i<parts.Length;i++)storage.Write(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),bank),parts[i]);
            ValidateReadback(bank,parts,commit,state.Town);
            storage.Write(SplitSaveCodec.FileName(4,bank),commit);
            // Preserve full semantic validation and exact readback while sharing
            // the live terrain read-only instead of allocating 25 KiB arrays.
            VerifyReadback(bank,parts,commit);
        }catch{state.Sequence=previous;throw;}
    }
}
