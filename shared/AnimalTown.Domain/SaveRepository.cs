namespace AnimalTown;

public interface ISaveStorage
{
    byte[] Read(string name);
    void Write(string name,byte[] bytes);
}

// Reference manifests publish exact component revisions after readback.
// Changed records use the other copy; existing references remain intact until
// publication. Mirror repair follows publication, never overwriting its primary.
public sealed class SaveRepository
{
    private readonly ISaveStorage storage;
    public SaveRepository(ISaveStorage storage){this.storage=storage;}
    private static void Match(byte[] actual,byte[] expected)
    {if(actual.Length!=expected.Length)throw new InvalidOperationException("Save readback length differs");for(int i=0;i<actual.Length;i++)if(actual[i]!=expected[i])throw new InvalidOperationException("Save readback differs from snapshot");}
    private static void MatchSnapshot(GameState actual,GameState expected)
    {Match(SaveCodec.Encode(actual),SaveCodec.Encode(expected));}
    private byte[][] ReadParts(int bank)
    {var town=storage.Read(SplitSaveCodec.FileName(0,bank));var parts=new byte[SplitSaveCodec.ReadVersion(town)>=4?SplitSaveCodec.PartCount:4][];parts[0]=town;for(int i=1;i<parts.Length;i++)parts[i]=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),bank));return parts;}
    private sealed class Current
    {
        internal required byte[] Commit;
        internal int Bank,Sequence,Seed;
        internal IncrementalSaveCodec.Manifest? References;
    }
    private Current? NewestManifest()
    {
        Current? best=null;
        for(int bank=0;bank<2;bank++)try{
            byte[] bytes=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.Commit,bank));
            var r=new SplitSaveCodec.Reader(bytes,SplitSaveCodec.Commit);
            var c=new Current{Commit=bytes,Bank=bank,Sequence=r.Sequence,Seed=r.Seed};
            if(r.Version==5)c.References=IncrementalSaveCodec.Read(bytes,bank);
            if(best==null||c.Sequence>best.Sequence)best=c;
        }catch(Exception){}
        return best;
    }
    private byte[] ReadReference(IncrementalSaveCodec.Manifest m,int part,bool remember=false)
    {
        for(int copy=0;copy<2;copy++)try{
            int bank=m.Banks[part]^copy;
            var bytes=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(part),bank));
            IncrementalSaveCodec.Verify(m,part,bytes);if(remember)m.Banks[part]=bank;return bytes;
        }catch(Exception){}
        throw new InvalidOperationException("No valid copy of a referenced save record");
    }
    private byte[][] ReadReferenced(IncrementalSaveCodec.Manifest m)
    {var parts=new byte[IncrementalSaveCodec.Count][];for(int i=0;i<parts.Length;i++)parts[i]=ReadReference(m,i);return parts;}
    private GameState DecodeReference(byte[] commit,int bank,Town? snapshot=null)
    {
        var m=IncrementalSaveCodec.Read(commit,bank);
        return SplitSaveCodec.Decode(IncrementalSaveCodec.Restore(m,ReadReferenced(m)),snapshot);
    }
    public GameState? Load()
    {
        GameState? best=null;
        for(int bank=0;bank<2;bank++)try{
            var commit=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.Commit,bank));
            var r=new SplitSaveCodec.Reader(commit,SplitSaveCodec.Commit);
            var s=r.Version==5?DecodeReference(commit,bank):SplitSaveCodec.DecodeCommitted(commit,ReadParts(bank));
            if(best==null||s.Sequence>best.Sequence)best=s;
        }catch(Exception){}
        // Legacy data is read-only migration input and is never overwritten.
        for(int bank=0;bank<2;bank++)try{var s=SaveCodec.Decode(storage.Read("AnimalTown"+bank));if(best==null||s.Sequence>best.Sequence)best=s;}catch(Exception){}
        return best;
    }
    public int LastWrittenRecords {get;private set;}
    public int LastWrittenBytes {get;private set;}
    public void Save(GameState state)
    {
        LastWrittenRecords=LastWrittenBytes=0;
        int previous=state.Sequence;var current=NewestManifest();
        if(current!=null&&current.Seed!=state.Town.Seed)throw new InvalidOperationException("The town on this card has changed. Reload before saving.");
        state.CaptureProfile();
        if(current!=null&&current.Sequence>previous){
            // An I/O exception may follow a durable publication. Only the exact
            // same snapshot may adopt that sequence and finish mirror repair.
            if(current.References==null||!MatchesCurrent(state,current.References))throw new InvalidOperationException("The town on this card has changed. Reload before saving.");
            state.Sequence=current.Sequence;Save(state);return;
        }
        if(current!=null&&current.References==null)SplitSaveCodec.VerifyCommitted(current.Commit,ReadParts(current.Bank));
        state.Sequence=previous==int.MaxValue?previous:previous+1;
        try{
            var m=new IncrementalSaveCodec.Manifest{Sequence=state.Sequence,Seed=state.Town.Seed,Bank=current==null?state.Sequence%2:1-current.Bank};
            bool changed=current?.References==null;
            for(int i=0;i<IncrementalSaveCodec.Count;i++)changed=SavePart(state,i,current?.References,m,previous==int.MaxValue)||changed;
            if(!changed){state.Sequence=previous;m.Sequence=previous;}
            else if(previous==int.MaxValue)throw new InvalidOperationException("Save sequence exhausted");
            var commit=IncrementalSaveCodec.Commit(state,m);
            // Validate the complete logical snapshot, but keep record-encoding
            // temporaries in SavePart's separate native GC frame.
            if(changed)DecodeReference(commit,m.Bank,state.Town);
            Publish(SplitSaveCodec.FileName(SplitSaveCodec.Commit,m.Bank),commit);
            // The new manifest is complete before repairing any old primary.
            // A cut during mirror repair always leaves its new references intact.
            for(int i=0;i<IncrementalSaveCodec.Count;i++)RepairCopy(m,i);
            Publish(SplitSaveCodec.FileName(SplitSaveCodec.Commit,1-m.Bank),commit);
            VerifyReferences(m);
        }catch{state.Sequence=previous;throw;}
    }
    private bool MatchesCurrent(GameState state,IncrementalSaveCodec.Manifest current)
    {for(int i=0;i<IncrementalSaveCodec.Count;i++)if(!MatchesCurrentPart(state,current,i))return false;return true;}
    private bool MatchesCurrentPart(GameState state,IncrementalSaveCodec.Manifest current,int part)
    {return IncrementalSaveCodec.SamePayload(ReadReference(current,part),IncrementalSaveCodec.Encode(state,part));}
    private bool SavePart(GameState state,int part,IncrementalSaveCodec.Manifest? current,IncrementalSaveCodec.Manifest next,bool exhausted)
    {
        var encoded=IncrementalSaveCodec.Encode(state,part);
        if(current!=null){
            var existing=ReadReference(current,part,true);
            if(IncrementalSaveCodec.SamePayload(existing,encoded)){
                IncrementalSaveCodec.Reference(next,part,current.Banks[part],existing);return false;
            }
        }
        if(exhausted)throw new InvalidOperationException("Save sequence exhausted");
        int bank=current==null?next.Bank:1-current.Banks[part];
        IncrementalSaveCodec.Reference(next,part,bank,encoded);
        string name=SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(part),bank);
        storage.Write(name,encoded);LastWrittenRecords++;LastWrittenBytes+=encoded.Length;
        Match(storage.Read(name),encoded);return true;
    }
    private void Publish(string name,byte[] bytes)
    {
        bool identical=false;try{var actual=storage.Read(name);Match(actual,bytes);identical=true;}catch(Exception){}
        if(identical)return;
        storage.Write(name,bytes);LastWrittenRecords++;LastWrittenBytes+=bytes.Length;Match(storage.Read(name),bytes);
    }
    private void RepairCopy(IncrementalSaveCodec.Manifest m,int part)
    {
        // Only a missing/mismatched mirror is written. Healthy no-op saves
        // inspect both copies without changing any file or publication number.
        var primary=ReadReference(m,part,true);
        string name=SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(part),1-m.Banks[part]);
        Publish(name,primary);
    }
    private void VerifyReferences(IncrementalSaveCodec.Manifest m)
    {for(int i=0;i<IncrementalSaveCodec.Count;i++){var a=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),m.Banks[i]));var b=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),1-m.Banks[i]));IncrementalSaveCodec.Verify(m,i,a);Match(a,b);}}
    internal bool MatchesAttemptedFiles(GameState attempted,string[] names)
    {
        int bank=attempted.Sequence%2;attempted.CaptureProfile();
        var parts=new byte[IncrementalSaveCodec.Count][];
        var manifest=new IncrementalSaveCodec.Manifest{Sequence=attempted.Sequence,Seed=attempted.Town.Seed,Bank=bank};
        for(int i=0;i<parts.Length;i++){parts[i]=IncrementalSaveCodec.Encode(attempted,i);IncrementalSaveCodec.Reference(manifest,i,bank,parts[i]);}
        bool found=false;
        foreach(string name in names){
            if(!SplitSaveCodec.IsFile(name))continue;
            byte[]? expected=null;
            for(int i=0;i<parts.Length;i++)if(name==SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),bank)){expected=parts[i];break;}
            if(name==SplitSaveCodec.FileName(SplitSaveCodec.Commit,bank))expected=IncrementalSaveCodec.Commit(attempted,manifest);
            // Includes legacy records and either record/manifest in the other
            // bank. No file outside this exact attempted generation is ours.
            if(expected==null)return false;
            try{Match(storage.Read(name),expected);}catch(Exception){return false;}
            found=true;
        }
        return found;
    }
    public GameState DeleteProfile(GameState expected,int profile)
    {
        if((uint)profile>=GameState.ProfileCount||!expected.Profiles[profile].Created)throw new InvalidOperationException("Villager does not exist");
        if(expected.Sequence==int.MaxValue)throw new InvalidOperationException("Save sequence exhausted");
        var candidate=Load()??throw new InvalidOperationException("No complete saved town");
        // Refuse a removed/swapped card or a newer/different town. The displayed
        // snapshot remains untouched until both verified banks are committed.
        // Keep serialized comparisons in their own native GC frame, so their
        // buffers can retire before the save/readback operation allocates.
        MatchSnapshot(candidate,expected);
        candidate.DeleteProfile(profile);
        Save(candidate);
        // Save verifies both reference heads and mirrors before reporting success;
        // fallback cannot resurrect the deleted resident.
        return candidate;
    }
}
