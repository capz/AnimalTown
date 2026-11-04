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
    private byte[][] ReadParts(int bank)
    {var parts=new byte[4][];for(int i=0;i<4;i++)parts[i]=storage.Read(SplitSaveCodec.FileName(i,bank));return parts;}
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
            for(int i=0;i<4;i++)storage.Write(SplitSaveCodec.FileName(i,bank),parts[i]);
            SplitSaveCodec.DecodeCommitted(commit,ReadParts(bank));
            storage.Write(SplitSaveCodec.FileName(4,bank),commit);
            SplitSaveCodec.DecodeCommitted(storage.Read(SplitSaveCodec.FileName(4,bank)),ReadParts(bank));
        }catch{state.Sequence=previous;throw;}
    }
}
