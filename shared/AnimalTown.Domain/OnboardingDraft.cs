namespace AnimalTown;

// The draft owns only mutable personal/world records plus the encoded baseline.
// Terrain is shared read-only: onboarding previews/house placement never edit it.
// Dispose drops the baseline and candidate references before ordinary gameplay.
public sealed class OnboardingDraft : IDisposable
{
    private Town? sourceTerrain;
    private byte[]? baseline;
    private byte[]? attempted;
    private GameState? state;
    private int[]? removedTreeIndices;
    private int removedTreeCount;
    private readonly bool savedTown;
    private readonly bool hostedCommerceLocked;
    private readonly int sourceSequence;
    private bool committed;
    public readonly int Profile;
    public readonly bool IsNewProfile;
    public GameState State=>state??throw new ObjectDisposedException(nameof(OnboardingDraft));

    public OnboardingDraft(GameState source,int profile,bool savedTown)
    {
        if((uint)profile>=GameState.ProfileCount)throw new ArgumentOutOfRangeException(nameof(profile));
        if(source.HomePlaced(profile))throw new InvalidOperationException("This villager already has a home");
        Profile=profile;IsNewProfile=!source.Profiles[profile].Created;this.savedTown=savedTown;sourceSequence=source.Sequence;
        hostedCommerceLocked=source.HostedCommerceLocked;
        sourceTerrain=source.Town;baseline=SaveCodec.Encode(source);
        state=SaveCodec.ValidateSnapshot(baseline,source.Town);
        state.HostedCommerceLocked=hostedCommerceLocked;
        state.SelectProfile(profile);state.Space=0;
        if(!state.CanWalk(state.X,state.Z,0))for(int cell=0;cell<Town.Cells;cell++){
            float x=cell%Town.Width+.5f,z=cell/Town.Width+.5f;
            if(state.CanWalk(x,z,0)){state.X=x;state.Z=z;break;}
        }
    }

    public static bool ValidName(string name)
    {
        if(name==null||name.Length<1||name.Length>16)return false;
        bool visible=false;
        for(int i=0;i<name.Length;i++){
            char c=name[i];
            if(!(c>='A'&&c<='Z'||c>='a'&&c<='z'||c>='0'&&c<='9'||c==' '||c=='.'||c=='!'||c=='?'))return false;
            if(c!=' ')visible=true;
        }
        return visible;
    }
    public bool SetName(string name)
    {
        EnsureEditable();if(!ValidName(name))return false;State.Name=name.Trim();return true;
    }
    public void SetSpecies(int species)
    {EnsureEditable();if((uint)species>=6)throw new ArgumentOutOfRangeException(nameof(species));State.Species=species;}

    public bool CanPlaceHome(int x,int z,out string reason)
        =>State.CanPlaceHouse(x,z,out reason);
    public bool PlaceHome(int x,int z)
    {
        EnsureEditable();if(!CanPlaceHome(x,z,out _))return false;
        if(State.HousePlaced)ClearHome();
        removedTreeIndices??=new int[State.Town.Ground.Length];
        State.Space=0;return State.PlaceHouse(x,z,false,false,removedTreeIndices,out removedTreeCount);
    }
    public void ClearHome()
    {
        EnsureEditable();
        while(removedTreeCount>0)State.Town.RestoreRemovedTree(removedTreeIndices![--removedTreeCount]);
        State.HousePlaced=false;State.Space=0;
        Array.Copy(sourceTerrain!.Paths.Packed,State.Town.Paths.Packed,TownPaths.PackedBytes);State.Town.NotifyTerrainChanged();
    }

    // Returns a fresh mutable source graph for discard. No Generate call or
    // duplicate terrain arrays are required, and draft-only home/name edits vanish.
    public GameState Restore()
    {if(baseline==null)throw new ObjectDisposedException(nameof(OnboardingDraft));var restored=SaveCodec.ValidateSnapshot(baseline,sourceTerrain!);restored.HostedCommerceLocked=hostedCommerceLocked;return restored;}

    public void Commit(SaveRepository repository,bool hasTownFiles,string[]? townFiles=null)
    {
        if(committed)return;
        ValidateCandidate();var s=State;
        var actual=repository.Load();
        if(actual!=null&&actual.Sequence>sourceSequence&&MatchesCandidate(actual)){
            repository.Save(actual);s.Sequence=actual.Sequence;committed=true;return;
        }
        if(savedTown){
            if(actual==null||!MatchesBaseline(actual))throw new InvalidOperationException("The town on this card has changed. Return to the title and reload.");
        }else if(actual!=null||hasTownFiles&&!MatchesAttemptedFiles(repository,townFiles)){
            throw new InvalidOperationException("This card already contains town data. Return to the title and reload.");
        }
        if(!savedTown)attempted=CaptureAttempt(s);
        repository.Save(s);committed=true;
    }
    // Finish a temporary resident without publishing a save or incrementing its
    // sequence. The caller retains State before releasing the private draft.
    public void BeginWithoutSaving()
    {if(committed)return;ValidateCandidate();committed=true;}
    private void ValidateCandidate()
    {
        var s=State;
        if(s.ActiveProfile!=Profile||!s.Profiles[Profile].Created)throw new InvalidOperationException("The selected villager has changed");
        if(!ValidName(s.Name)||(uint)s.Species>=6)throw new InvalidOperationException("Choose a name and look first");
        if(!s.HousePlaced)throw new InvalidOperationException("Choose a home before saving");
    }
    // A fresh town has no older bank to fall back to. After an interrupted
    // first save, only exact records from this draft's own previous attempt
    // may be replaced. Directory names are authoritative: unreadable present
    // files must never be mistaken for absent records or an empty card.
    private bool MatchesAttemptedFiles(SaveRepository repository,string[]? townFiles)
    {return attempted!=null&&townFiles!=null&&repository.MatchesAttemptedFiles(SaveCodec.ValidateSnapshot(attempted,State.Town),townFiles);}
    private static byte[] CaptureAttempt(GameState s)
    {
        int sequence=s.Sequence;if(sequence==int.MaxValue)throw new InvalidOperationException("Save sequence exhausted");
        s.Sequence++;
        try{return SaveCodec.Encode(s);}finally{s.Sequence=sequence;}
    }
    private bool MatchesBaseline(GameState actual)
    {return Same(SaveCodec.Encode(actual),baseline!);}
    private bool MatchesCandidate(GameState actual)
    {
        var s=State;int sequence=s.Sequence;s.Sequence=actual.Sequence;
        try{return Same(SaveCodec.Encode(actual),SaveCodec.Encode(s));}finally{s.Sequence=sequence;}
    }
    private static bool Same(byte[] a,byte[] b)
    {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    private void EnsureEditable()
    {if(state==null)throw new ObjectDisposedException(nameof(OnboardingDraft));if(committed)throw new InvalidOperationException("Onboarding is already saved");}
    public void Dispose(){sourceTerrain=null;baseline=null;attempted=null;state=null;removedTreeIndices=null;removedTreeCount=0;}
}
