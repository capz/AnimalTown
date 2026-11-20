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
    private GameState DecodeReference(byte[] commit,int bank,Town? snapshot=null,Jobs.OfflineJobLedger? textMemo=null)
    {
        var m=IncrementalSaveCodec.Read(commit,bank);
        return SplitSaveCodec.Decode(IncrementalSaveCodec.Restore(m,ReadReferenced(m)),snapshot,textMemo,m.Sequences[2]);
    }
    public GameState? Load()=>Load(null);
    public void VerifyRevision(GameState expected)
    {
        var current=NewestManifest();
        if(current?.References!=null){
            if(current.Sequence!=expected.Sequence||current.Seed!=expected.Town.Seed)throw new InvalidOperationException("Travel card changed. Recheck both towns");
            // CRC/reference validation still covers the complete economic
            // record. Read only its fixed header/nonce; decoding every mailbox
            // and another private town is unnecessary for this identity check.
            if(SplitSaveCodec.EconomicTownIdentity(ReadReference(current.References,2))!=expected.Town.Jobs.TownId)throw new InvalidOperationException("Travel town identity changed");
            return;
        }
        var saved=Load(expected)??throw new InvalidOperationException("Travel card was removed or changed");
        if(saved.Sequence!=expected.Sequence||saved.Town.Jobs.TownId!=expected.Town.Jobs.TownId)throw new InvalidOperationException("Travel card changed. Recheck both towns");
    }
    public GameState? Load(GameState? textReuse)
    {
        GameState? best=null;
        for(int bank=0;bank<2;bank++)try{
            var commit=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.Commit,bank));
            var r=new SplitSaveCodec.Reader(commit,SplitSaveCodec.Commit);
            if(best!=null&&r.Sequence<=best.Sequence)continue;
            var s=r.Version==5?DecodeReference(commit,bank,null,best?.Town.Jobs??textReuse?.Town.Jobs):SplitSaveCodec.DecodeCommitted(commit,ReadParts(bank));
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
    private void MatchStored(string name,byte[] expected)
    {if(expected.Length>8192&&storage is ISaveSnapshotStorage verified){if(!verified.Matches(name,expected,-1,expected.Length,0,false))throw new InvalidOperationException("Save readback differs from snapshot");}else Match(storage.Read(name),expected);}
    private int LargeReference(IncrementalSaveCodec.Manifest m,int part,byte[]? expected,out bool same)
    {
        var verified=(ISaveSnapshotStorage)storage;
        for(int copy=0;copy<2;copy++)try{
            int bank=m.Banks[part]^copy;string name=SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(part),bank);
            if(verified.Matches(name,expected,m.Sequences[part],m.Lengths[part],m.Checksums[part],expected!=null)){same=true;m.Banks[part]=bank;return bank;}
            if(expected!=null&&verified.Matches(name,null,m.Sequences[part],m.Lengths[part],m.Checksums[part],false)){same=false;m.Banks[part]=bank;return bank;}
        }catch(Exception){}
        throw new InvalidOperationException("No valid copy of a referenced save record");
    }
    private static void ReuseReference(IncrementalSaveCodec.Manifest current,IncrementalSaveCodec.Manifest next,int part)
    {next.Banks[part]=current.Banks[part];next.Sequences[part]=current.Sequences[part];next.Lengths[part]=current.Lengths[part];next.Checksums[part]=current.Checksums[part];}
    private bool MatchesCurrent(GameState state,IncrementalSaveCodec.Manifest current)
    {for(int i=0;i<IncrementalSaveCodec.Count;i++)if(!MatchesCurrentPart(state,current,i))return false;return true;}
    private bool MatchesCurrentPart(GameState state,IncrementalSaveCodec.Manifest current,int part)
    {var encoded=IncrementalSaveCodec.Encode(state,part);if(current.Lengths[part]>8192&&storage is ISaveSnapshotStorage){LargeReference(current,part,encoded,out bool same);return same;}return IncrementalSaveCodec.SamePayload(ReadReference(current,part),encoded);}
    private bool SavePart(GameState state,int part,IncrementalSaveCodec.Manifest? current,IncrementalSaveCodec.Manifest next,bool exhausted)
    {
        var encoded=IncrementalSaveCodec.Encode(state,part);
        if(current!=null){
            if(current.Lengths[part]>8192&&storage is ISaveSnapshotStorage){LargeReference(current,part,encoded,out bool same);if(same){ReuseReference(current,next,part);return false;}}
            else{var existing=ReadReference(current,part,true);if(IncrementalSaveCodec.SamePayload(existing,encoded)){IncrementalSaveCodec.Reference(next,part,current.Banks[part],existing);return false;}}
        }
        if(exhausted)throw new InvalidOperationException("Save sequence exhausted");
        int bank=current==null?next.Bank:1-current.Banks[part];
        IncrementalSaveCodec.Reference(next,part,bank,encoded);
        string name=SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(part),bank);
        storage.Write(name,encoded);LastWrittenRecords++;LastWrittenBytes+=encoded.Length;
        MatchStored(name,encoded);return true;
    }
    private void Publish(string name,byte[] bytes)
    {
        bool identical=false;try{if(bytes.Length>8192&&storage is ISaveSnapshotStorage verified)identical=verified.Matches(name,bytes,-1,bytes.Length,0,false);else{var actual=storage.Read(name);Match(actual,bytes);identical=true;}}catch(Exception){}
        if(identical)return;
        storage.Write(name,bytes);LastWrittenRecords++;LastWrittenBytes+=bytes.Length;MatchStored(name,bytes);
    }
    private void RepairCopy(IncrementalSaveCodec.Manifest m,int part)
    {
        // Only a missing/mismatched mirror is written. Healthy no-op saves
        // inspect both copies without changing any file or publication number.
        if(m.Lengths[part]>8192&&storage is ISaveSnapshotStorage verified){LargeReference(m,part,null,out _);string mirror=SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(part),1-m.Banks[part]);try{if(verified.Matches(mirror,null,m.Sequences[part],m.Lengths[part],m.Checksums[part],false))return;}catch(Exception){}}
        var primary=ReadReference(m,part,true);
        string name=SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(part),1-m.Banks[part]);
        Publish(name,primary);
    }
    private void VerifyReferences(IncrementalSaveCodec.Manifest m)
    {for(int i=0;i<IncrementalSaveCodec.Count;i++){if(m.Lengths[i]>8192&&storage is ISaveSnapshotStorage verified){for(int copy=0;copy<2;copy++)if(!verified.Matches(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),copy),null,m.Sequences[i],m.Lengths[i],m.Checksums[i],false))throw new InvalidOperationException("Save reference mismatch");continue;}var a=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),m.Banks[i]));var b=storage.Read(SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),1-m.Banks[i]));IncrementalSaveCodec.Verify(m,i,a);Match(a,b);}}
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
        var candidate=Load(expected)??throw new InvalidOperationException("No complete saved town");
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
