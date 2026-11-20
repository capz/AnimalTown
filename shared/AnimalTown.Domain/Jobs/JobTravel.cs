namespace AnimalTown.Jobs;

public enum OfflineTripPhase { None,Prepared,Decided,Active,ReturnPrepared,ReturnDecided,Finished }
public struct OfflineTrip
{
    public JobIdentity Id,Player;
    public ulong Home,Destination;
    public int HomeProfile,ResidentAnchor;
    public bool HomeSide;
    public OfflineTripPhase Phase;
    public bool Pending=>Phase!=OfflineTripPhase.None&&Phase!=OfflineTripPhase.Finished;
}
public sealed partial class OfflineJobLedger
{
    public OfflineTrip Trip {get;internal set;}
    public VillagerProfile? GuestProfile {get;internal set;}
    public bool ActiveVisitor {get;internal set;}
    internal int GuestAcre=-1;
    internal readonly int[] NpcSpecies={0,1,2,3,4,5};
    internal readonly JobIdentity[] KnownNpcs=new JobIdentity[32];
    internal readonly ulong[] KnownNpcTowns=new ulong[32];
    internal readonly string[] KnownNpcNames=new string[32];
    public int Species(int npc)=>NpcSpecies[npc];
    public ulong NpcTown(JobIdentity id){if(PermanentlyRemoved(id))return 0;int local=FindNpc(id);if(local>=0)return TownId;for(int i=0;i<KnownNpcs.Length;i++)if(KnownNpcs[i]==id)return KnownNpcTowns[i];return 0;}
    public string KnownName(JobIdentity id){for(int i=0;i<KnownNpcs.Length;i++)if(KnownNpcs[i]==id)return KnownNpcNames[i];return "the villager";}
    internal void RememberNpc(JobIdentity id,ulong town,string name)
    {
        int at=-1;for(int i=0;i<KnownNpcs.Length;i++){if(KnownNpcs[i]==id){at=i;break;}if(KnownNpcs[i].IsEmpty&&at<0)at=i;}
        if(at<0)throw new InvalidOperationException("Remembered town residents are full");KnownNpcs[at]=id;KnownNpcTowns[at]=town;KnownNpcNames[at]=name;if(LastMovedNpc==id)LastMovedTown=town;for(int p=0;p<OwnerCount;p++)if(ForeignNpcs[p]==id)ForeignTowns[p]=town;
    }
    internal bool CanRememberNpcs(OfflineJobLedger roster)
    {
        int free=0,needed=0;for(int i=0;i<KnownNpcs.Length;i++)if(KnownNpcs[i].IsEmpty)free++;
        for(int n=0;n<6;n++){bool found=false;for(int i=0;i<KnownNpcs.Length;i++)if(KnownNpcs[i]==roster.Npc(n)){found=true;break;}if(!found)needed++;}
        return needed<=free;
    }
    internal bool CanRememberNpcPair(JobIdentity first,JobIdentity second)
    {
        int free=0;bool haveFirst=false,haveSecond=false;
        for(int i=0;i<KnownNpcs.Length;i++){if(KnownNpcs[i].IsEmpty)free++;if(KnownNpcs[i]==first)haveFirst=true;if(KnownNpcs[i]==second)haveSecond=true;}
        return (haveFirst?0:1)+(haveSecond||second==first?0:1)<=free;
    }
    internal bool CanAdoptNpc(int profile,JobIdentity arriving,bool newIdentity=false)
    {
        int free=0,needed=0,first=profile*RelationsPerPlayer;
        for(int i=first;i<first+RelationsPerPlayer;i++)if(Relations[i].Npc.IsEmpty)free++;
        for(int n=0;n<6;n++)if(RelationSlot(profile,Npc(n),false)<0)needed++;
        if(newIdentity||FindNpc(arriving)<0&&RelationSlot(profile,arriving,false)<0)needed++;
        return needed<=free;
    }
    private void WriteTravel(JobWireWriter w)
    {
        var t=Trip;w.Key(t.Id);w.Key(t.Player);w.ULong(t.Home);w.ULong(t.Destination);w.Int(t.HomeProfile);w.Int(t.ResidentAnchor);w.Int(t.HomeSide?1:0);w.Int((int)t.Phase);w.Int(ActiveVisitor?1:0);w.Int(GuestAcre);
        w.Int(GuestProfile==null?0:1);if(GuestProfile!=null)ProfileCodec.WritePortable(w,GuestProfile);
        for(int i=0;i<6;i++)w.Int(NpcSpecies[i]);
        int count=0;for(int i=0;i<KnownNpcs.Length;i++)if(!KnownNpcs[i].IsEmpty)count++;w.Int(count);
        for(int i=0;i<KnownNpcs.Length;i++)if(!KnownNpcs[i].IsEmpty){w.Key(KnownNpcs[i]);w.ULong(KnownNpcTowns[i]);w.Text(KnownNpcNames[i]);}
    }
    private void ReadTravel(JobWireReader r)
    {
        Trip=new OfflineTrip{Id=r.Key(),Player=r.Key(),Home=r.ULong(),Destination=r.ULong(),HomeProfile=r.Range(0,3),ResidentAnchor=r.Range(0,3),HomeSide=r.Range(0,1)==1,Phase=(OfflineTripPhase)r.Range(0,6)};ActiveVisitor=r.Range(0,1)==1;GuestAcre=r.Range(-1,Town.ScreenColumns*Town.ScreenRows-1);
        if(r.Range(0,1)==1)GuestProfile=ProfileCodec.ReadPortable(r);
        if(Trip.Pending&&(!Trip.Id.IsValid||!Trip.Player.IsValid||Trip.Home==0||Trip.Destination==0||Trip.Home==Trip.Destination||TownId!=(Trip.HomeSide?Trip.Home:Trip.Destination))||ActiveVisitor&&(GuestProfile==null||Trip.HomeSide||Trip.Phase!=OfflineTripPhase.Active))throw new InvalidOperationException("Invalid offline travel ownership");
        for(int i=0;i<6;i++)NpcSpecies[i]=r.Range(0,5);
        int count=r.Range(0,KnownNpcs.Length);for(int i=0;i<count;i++){var id=r.Key(false);ulong town=r.ULong();if(town==0)throw new InvalidOperationException("Invalid known town");RememberNpc(id,town,r.Text(24));}
    }
    internal void SetPlayerIdentity(int profile,JobIdentity id){players[profile]=id;}
    internal void SetNpcIdentity(int slot,JobIdentity id){npcs[slot]=id;}
}

// The coordinator only mutates private repository loads. Publication on each
// card is atomic; the two cards are linked by a durable canonical decision.
// Recovery always requires both matching identities. Removal never unlocks home.
public sealed partial class OfflineTownTravel : IOfflineJobTravel
{
    private readonly SaveRepository first,second;
    private GameState? knownFirst,knownSecond;
    private GameState? textReuse;
    public OfflineTownTravel(SaveRepository first,SaveRepository second,GameState? textReuse=null){this.first=first;this.second=second;this.textReuse=textReuse;}
    public bool Reachable(ulong town,JobIdentity npc)
    {return knownFirst!=null&&knownFirst.Town.Jobs.TownId==town&&knownFirst.Town.Jobs.FindNpc(npc)>=0||knownSecond!=null&&knownSecond.Town.Jobs.TownId==town&&knownSecond.Town.Jobs.FindNpc(npc)>=0;}
    public void Inspect(out GameState home,out GameState destination)
    {
        home=first.Load(knownFirst??textReuse)??throw new InvalidOperationException("Home card has no complete town");destination=second.Load(knownSecond??textReuse)??throw new InvalidOperationException("Destination card has no complete town");
        if(!home.Town.Jobs.Initialized||!destination.Town.Jobs.Initialized)throw new InvalidOperationException("Open and save both towns in this version before travelling");
        if(home.Town.Jobs.TownId==destination.Town.Jobs.TownId)throw new InvalidOperationException("These saves are copies of the same town");knownFirst=home;knownSecond=destination;
    }
    private void TakeSnapshots(out GameState home,out GameState destination)
    {
        var a=knownFirst;var b=knownSecond;knownFirst=knownSecond=null;
        if(a!=null&&b!=null){first.VerifyRevision(a);second.VerifyRevision(b);home=a;destination=b;}
        else{Inspect(out home,out destination);knownFirst=knownSecond=null;}
        textReuse=null;
        // Mutations now own these private candidates. Failure leaves no cached
        // speculative phase; another attempt must read the durable cards again.
    }
    private static void SaveChecked(SaveRepository repository,GameState candidate)
    {
        repository.VerifyRevision(candidate);repository.Save(candidate);
    }
    public GameState Depart(int profile,long now)
    {
        TakeSnapshots(out var home,out var dest);var h=home.Town.Jobs;var d=dest.Town.Jobs;
        if((uint)profile>=GameState.ProfileCount||!home.Profiles[profile].Created||home.Profiles[profile].Tutorial<7||h.NpcMove.Pending||d.NpcMove.Pending||h.Trip.Pending||d.Trip.Pending||d.GuestProfile!=null||!h.CanAllocate(1))throw new InvalidOperationException("A town already has an unsettled trip");
        home.SelectProfile(profile);home.CaptureProfile();CheckCapacity(home,profile,dest);
        var t=new OfflineTrip{Id=h.Allocate(),Player=h.Player(profile),Home=h.TownId,Destination=d.TownId,HomeProfile=profile,ResidentAnchor=dest.ActiveProfile,HomeSide=true,Phase=OfflineTripPhase.Prepared};h.Trip=t;
        SaveChecked(first,home); // locks the source before a destination capsule exists
        return Continue(home,dest,now);
    }
    public GameState Recover(long now)
    {TakeSnapshots(out var home,out var dest);if(home.Town.Jobs.Trip.Phase==OfflineTripPhase.Finished&&home.Town.Jobs.Trip.Id==dest.Town.Jobs.Trip.Id&&dest.Town.Jobs.Trip.Phase==OfflineTripPhase.ReturnDecided){var t=dest.Town.Jobs.Trip;t.Phase=OfflineTripPhase.Finished;dest.Town.Jobs.Trip=t;SaveChecked(second,dest);home.RestoreProfile(home.Town.Jobs.Trip.HomeProfile);knownFirst=home;knownSecond=dest;return home;}return Continue(home,dest,now);}
    private GameState Continue(GameState home,GameState dest,long now)
    {
        var h=home.Town.Jobs;var d=dest.Town.Jobs;var t=h.Trip;
        if(!t.Pending||!t.HomeSide||t.Home!=h.TownId||t.Destination!=d.TownId)throw new InvalidOperationException("These are not the two cards for this trip");
        if(d.Trip.Pending&&d.Trip.Id!=t.Id)throw new InvalidOperationException("Destination belongs to another trip");
        if(t.Phase==OfflineTripPhase.ReturnPrepared||d.Trip.Phase==OfflineTripPhase.ReturnPrepared||d.Trip.Phase==OfflineTripPhase.ReturnDecided)return FinishReturn(home,dest,now);
        if(!d.Trip.Pending){
            CheckCapacity(home,t.HomeProfile,dest);CopyPortable(home,t.HomeProfile,dest,OfflineJobLedger.Visitor);
            t.HomeSide=false;t.Phase=OfflineTripPhase.Prepared;d.Trip=t;d.ActiveVisitor=false;SaveChecked(second,dest);
        }
        if(h.Trip.Phase==OfflineTripPhase.Prepared){RemovePortable(home,h.Trip.HomeProfile);var decision=h.Trip;decision.Phase=OfflineTripPhase.Decided;h.Trip=decision;SaveChecked(first,home);}
        if(d.Trip.Phase==OfflineTripPhase.Prepared){var activation=d.Trip;activation.Phase=OfflineTripPhase.Active;d.Trip=activation;d.ActiveVisitor=true;SaveChecked(second,dest);}
        dest.ActivateSavedVisitor();dest.ResidentJobs.Enable(d.TownId,now);dest.ResidentJobs.Travel=this;knownFirst=home;knownSecond=dest;return dest;
    }
    public GameState Return(long now)
    {
        TakeSnapshots(out var home,out var dest);if(home.Town.Jobs.Trip.Id!=dest.Town.Jobs.Trip.Id||dest.Town.Jobs.Trip.Phase!=OfflineTripPhase.Active)throw new InvalidOperationException("No matching active trip");
        CheckCapacity(dest,OfflineJobLedger.Visitor,home);dest.CaptureProfile();var t=dest.Town.Jobs.Trip;t.Phase=OfflineTripPhase.ReturnPrepared;dest.Town.Jobs.Trip=t;dest.Town.Jobs.ActiveVisitor=false;SaveChecked(second,dest);
        return FinishReturn(home,dest,now);
    }
    private GameState FinishReturn(GameState home,GameState dest,long now)
    {
        var h=home.Town.Jobs;var d=dest.Town.Jobs;
        if(h.Trip.Id!=d.Trip.Id)throw new InvalidOperationException("Return receipts do not match");
        if(h.Trip.Phase!=OfflineTripPhase.ReturnPrepared){
            if(d.Trip.Phase==OfflineTripPhase.ReturnDecided)throw new InvalidOperationException("Home return preparation is missing. Keep both cards");
            CopyPortable(dest,OfflineJobLedger.Visitor,home,h.Trip.HomeProfile);var t=h.Trip;t.Phase=OfflineTripPhase.ReturnPrepared;h.Trip=t;SaveChecked(first,home);
        }
        if(d.Trip.Phase!=OfflineTripPhase.ReturnDecided){RemovePortable(dest,OfflineJobLedger.Visitor);d.GuestProfile=null;d.ActiveVisitor=false;d.SetPlayerIdentity(OfflineJobLedger.Visitor,default);var t=d.Trip;t.Phase=OfflineTripPhase.ReturnDecided;d.Trip=t;dest.RestoreProfile(t.ResidentAnchor);SaveChecked(second,dest);}
        var done=h.Trip;done.Phase=OfflineTripPhase.Finished;h.Trip=done;home.RestoreProfile(done.HomeProfile);SaveChecked(first,home);
        done=d.Trip;done.Phase=OfflineTripPhase.Finished;d.Trip=done;SaveChecked(second,dest);
        home.ResidentJobs.Enable(h.TownId,now);home.ResidentJobs.Travel=this;knownFirst=home;knownSecond=dest;return home;
    }
    private static void CheckCapacity(GameState source,int profile,GameState dest)
    {
        int needed=0,free=0;var s=source.Town.Jobs;var d=dest.Town.Jobs;var owner=s.Player(profile);
        for(int i=0;i<s.Rewards.Length;i++)if(PortableReward(s,s.Rewards[i],owner))needed++;
        for(int i=0;i<d.Rewards.Length;i++)if(d.Rewards[i].State==JobRewardState.Empty)free++;
        if(needed>free)throw new InvalidOperationException("Collect destination rewards before travelling");
        if(!s.CanRememberNpcs(d)||!d.CanRememberNpcs(s))throw new InvalidOperationException("Town address book is full");
        int relations=0;for(int i=0;i<OfflineJobLedger.RelationsPerPlayer;i++)if(!s.Relations[profile*OfflineJobLedger.RelationsPerPlayer+i].Npc.IsEmpty)relations++;
        for(int n=0;n<6;n++)if(s.RelationSlot(profile,d.Npc(n),false)<0)relations++;
        if(relations>OfflineJobLedger.RelationsPerPlayer)throw new InvalidOperationException("Villager relationship history is full");
    }
    private static bool PortableReward(OfflineJobLedger ledger,VillagerJobReward r,JobIdentity owner)
    {if(r.State==JobRewardState.Empty||r.IsReply||r.Owner!=owner)return false;if(r.State==JobRewardState.Owed)return true;int j=ledger.FindJob(r.Job);return j>=0&&!ledger.Jobs[j].Shared;}
    private static void CopyPortable(GameState source,int from,GameState dest,int to)
    {
        source.CaptureProfile();var s=source.Town.Jobs;var d=dest.Town.Jobs;var owner=s.Player(from);int fromStart=from*OfflineJobLedger.PersonalPerPlayer,toStart=to*OfflineJobLedger.PersonalPerPlayer;
        var sizing=new JobWireWriter();ProfileCodec.WritePortable(sizing,source.ProfileAt(from));var words=new int[sizing.Count];ProfileCodec.WritePortable(new JobWireWriter(words),source.ProfileAt(from));var p=ProfileCodec.ReadPortable(new JobWireReader(words));
        if(to==OfflineJobLedger.Visitor){Array.Clear(d.Relations,to*OfflineJobLedger.RelationsPerPlayer,OfflineJobLedger.RelationsPerPlayer);p.HousePlaced=false;p.Space=0;p.X=dest.X;p.Z=dest.Z;p.MailCount=0;Array.Clear(p.Mail,0,p.Mail.Length);d.GuestProfile=p;}
        else{
            var original=dest.Profiles[to];p.HousePlaced=original.HousePlaced;p.HouseX=original.HouseX;p.HouseZ=original.HouseZ;p.HouseTier=original.HouseTier;p.GardenTier=original.GardenTier;p.MailCount=original.MailCount;Array.Copy(original.Mail,p.Mail,p.Mail.Length);p.Space=0;p.X=original.X;p.Z=original.Z;dest.Profiles[to]=p;
        }
        d.SetPlayerIdentity(to,owner);
        for(int i=0;i<OfflineJobLedger.PersonalPerPlayer;i++){if(d.Jobs[toStart+i].Occupying)throw new InvalidOperationException("Portable job partition is already occupied");d.Jobs[toStart+i]=s.Jobs[fromStart+i];}
        for(int i=0;i<GameState.MaximumPockets;i++)d.SetBinding(to,i,s.Binding(from,i));
        for(int i=0;i<s.Rewards.Length;i++)if(PortableReward(s,s.Rewards[i],owner)){int slot=d.FreeReward();if(slot<0)throw new InvalidOperationException("Destination reward capacity changed");d.Rewards[slot]=s.Rewards[i];}
        for(int i=0;i<OfflineJobLedger.RelationsPerPlayer;i++){var r=s.Relations[from*OfflineJobLedger.RelationsPerPlayer+i];if(!r.Npc.IsEmpty)d.RememberRelation(to,r.Npc,r.Friendship,r.Met);}
        d.Receipts[to]=s.Receipts[from];
        for(int n=0;n<6;n++){s.RememberNpc(d.Npc(n),d.TownId,dest.Town.Residents[n].Name);d.RememberNpc(s.Npc(n),s.TownId,source.Town.Residents[n].Name);}
        s.ForeignNpcs[from]=d.Npc(0);s.ForeignTowns[from]=d.TownId;
        if(to==OfflineJobLedger.Visitor){d.ForeignNpcs[to]=s.Npc(0);d.ForeignTowns[to]=s.TownId;}
        else{d.ForeignNpcs[to]=s.Npc(0);d.ForeignTowns[to]=s.TownId;}
        // Relationships are keyed; a host roster index is never a visiting friend.
        for(int n=0;n<6;n++){int r=d.RelationSlot(to,d.Npc(n),false);p.Relationships[n*4]=r<0?0:d.Relations[r].Friendship;p.Relationships[n*4+1]=0;p.Relationships[n*4+3]=r<0?0:d.Relations[r].Met;}
        if(to!=OfflineJobLedger.Visitor)dest.RestoreProfile(to);
        d.Revision++;
    }
    private static void RemovePortable(GameState state,int profile)
    {
        var l=state.Town.Jobs;var owner=l.Player(profile);
        for(int i=0;i<l.Rewards.Length;i++)if(PortableReward(l,l.Rewards[i],owner))l.Rewards[i]=default;
        for(int i=0;i<OfflineJobLedger.PersonalPerPlayer;i++)l.Jobs[profile*OfflineJobLedger.PersonalPerPlayer+i]=default;
        for(int i=0;i<GameState.MaximumPockets;i++)l.SetBinding(profile,i,default);l.Revision++;
    }
}
