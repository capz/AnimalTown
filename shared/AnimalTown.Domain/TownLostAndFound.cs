using AnimalTown.Jobs;
namespace AnimalTown;

internal enum TownRecoverySource { Ground,Placement,Yield }

// Original placement/ground data is linked to the exact custody item by its
// monotonic ID. The town's lazy table follows every compact-array swap.
internal sealed class TownRecoveryRecord
{
    internal readonly int Id,Item,X,Z,Kind,Owner,Day,Rotation,Room,CustodyOwner;
    internal readonly bool Wrapped;
    internal readonly TownRecoverySource Source;
    internal TownRecoveryRecord(int id,TownRecoverySource source,int item,int x,int z,int kind,int owner,int day,int rotation,int room,bool wrapped,int custodyOwner)
    {Id=id;Source=source;Item=item;X=x;Z=z;Kind=kind;Owner=owner;Day=day;Rotation=rotation;Room=room;Wrapped=wrapped;CustodyOwner=custodyOwner;}
    internal bool Matches(GroundItem g)=>g.Kind==4&&g.Item==Item&&g.X==X&&g.Z==Z&&g.Owner==CustodyOwner&&g.Day==Day&&g.Wrapped==Wrapped;
}

// The existing bounded ground store owns the actual possessions. This extension
// only remembers provenance for still-unclaimed visitor removals. It is not an
// online-session rollback journal and cannot undo a completed shop purchase.
public sealed class TownLostAndFound
{
    private const int Magic=0x3144464c,Version=1,HeaderWords=4,RecordWords=13;
    public const int MaximumEncodedWords=HeaderWords+RecordWords*512;
    private readonly Town town;
    private int sequence;
    private TownRecoveryRecord?[]? recoveries;
    internal TownLostAndFound(Town town){this.town=town;}
    public int Count{get{int n=0;for(int i=0;i<town.GroundCount;i++)if(town.Ground[i].Kind==4)n++;for(int i=0;i<town.Jobs.Rewards.Length;i++)if(Deferred(i))n++;return n;}}
    private int RecoveryCount{get{int n=0;for(int i=0;i<town.GroundCount;i++)if(town.Ground[i].RecoveryId!=0)n++;return n;}}
    public bool HasState=>sequence!=0||RecoveryCount!=0;
    public int EncodedWordCount=>HeaderWords+RecordWords*RecoveryCount;
    internal bool CanRecord=>sequence<int.MaxValue;
    internal bool CanAddReceipts(int count)=>count>=0&&count<=town.Ground.Length-town.GroundCount&&count<=int.MaxValue-sequence;
    private bool Deferred(int slot)=>(uint)slot<town.Jobs.Rewards.Length&&town.Jobs.Rewards[slot].State==JobRewardState.Custody&&town.Jobs.Rewards[slot].Item!=0;
    public int IndexAt(int row)
    {if(row<0)return -1;for(int i=0;i<town.GroundCount;i++)if(town.Ground[i].Kind==4&&row--==0)return i;for(int i=0;i<town.Jobs.Rewards.Length;i++)if(Deferred(i)&&row--==0)return town.Ground.Length+i;return -1;}
    public int ItemAt(int index)=>(uint)index<town.GroundCount?(town.Ground[index].Kind==4?town.Ground[index].Item:0):Deferred(index-town.Ground.Length)?town.Jobs.Rewards[index-town.Ground.Length].Item:0;
    public int RecoveryIdAt(int index)=>(uint)index<town.GroundCount&&town.Ground[index].Kind==4?town.Ground[index].RecoveryId:0;
    public bool WrappedAt(int index)=>(uint)index<town.GroundCount&&town.Ground[index].Kind==4&&town.Ground[index].Wrapped;
    internal bool ClaimDeferred(GameState player,int index)
    {
        int slot=index-town.Ground.Length;if(!Deferred(slot))return false;var r=town.Jobs.Rewards[slot];
        if(!player.Receive(r.Item))return false;r.Item=0;town.Jobs.Rewards[slot]=r.Currency==0?default:r;ClearCustodyJob(r.Job);town.Jobs.Revision++;return true;
    }
    internal bool HoldVisitorReward(GameState state,int slot)
    {
        if(!ReferenceEquals(state.Town,town)||!state.Visiting||!TownItemActor.ForActivePlayer(state).CanModify(state,state)||(uint)slot>=town.Jobs.Rewards.Length)return false;
        var r=town.Jobs.Rewards[slot];
        if(r.State!=JobRewardState.Owed||r.Owner!=town.Jobs.Player(OfflineJobLedger.Visitor)||r.Job.Town!=town.Jobs.TownId||!Catalog.Valid(r.Item))return false;
        if(CanAddReceipts(1)&&TownItemActions.ReceiveTownGift(state,r.Item))r.Item=0;
        else{r.State=JobRewardState.Custody;r.IsReply=false;r.AutomaticReply=false;ClearCustodyJob(r.Job);}
        // Its reserved reward slot becomes bounded deferred custody when the
        // ground store is full. It is never freed or exported with the visitor.
        town.Jobs.Rewards[slot]=r;town.Jobs.Revision++;return true;
    }
    private void ClearCustodyJob(JobIdentity id)
    {int job=town.Jobs.FindJob(id);if(job>=0&&(!town.Jobs.Jobs[job].Shared||town.Jobs.Jobs[job].Template==JobTemplate.FavorLetter))town.Jobs.Jobs[job]=default;}
    public int FindRecovery(int recoveryId)
    {if(recoveryId<=0)return -1;for(int i=0;i<town.GroundCount;i++)if(town.Ground[i].Kind==4&&town.Ground[i].RecoveryId==recoveryId)return i;return -1;}
    internal TownRecoveryRecord? RecoveryAt(int index)=>recoveries!=null&&(uint)index<town.GroundCount?recoveries[index]:null;
    internal void ClearRecovery(int index)
    {town.Ground[index].RecoveryId=0;if(recoveries!=null)recoveries[index]=null;}
    internal void RemoveGroundAt(int index,int last)
    {ClearRecovery(index);if(recoveries!=null&&index!=last){recoveries[index]=recoveries[last];recoveries[last]=null;}}
    internal void RestoreGroundAt(int index,int last)
    {if(recoveries!=null){if(index!=last)recoveries[last]=recoveries[index];recoveries[index]=null;}}
    private void Record(int index,TownRecoveryRecord record)
    {recoveries??=new TownRecoveryRecord?[town.Ground.Length];recoveries[index]=record;town.Ground[index].RecoveryId=record.Id;}
    internal void RecordGround(int index)
    {var g=town.Ground[index];Record(index,new TownRecoveryRecord(++sequence,TownRecoverySource.Ground,g.Item,g.X,g.Z,g.Kind,g.Owner,g.Day,0,0,g.Wrapped,g.Owner));}
    internal void RecordPlacement(int index,Placement p)
    {var g=town.Ground[index];Record(index,new TownRecoveryRecord(++sequence,TownRecoverySource.Placement,p.Item,p.X,p.Z,-1,p.Owner,g.Day,p.Rotation,p.Room,false,g.Owner));}
    internal void RecordYield(int index)
    {var g=town.Ground[index];Record(index,new TownRecoveryRecord(++sequence,TownRecoverySource.Yield,g.Item,g.X,g.Z,-1,g.Owner,g.Day,0,0,g.Wrapped,g.Owner));}
    internal bool Add(int item,int x,int z,int owner,int day,bool wrapped,out GroundItem? custody)
    {
        custody=null;
        if(town.GroundCount==town.Ground.Length||!Catalog.Valid(item)||!town.Inside(x,z)||(uint)owner>=GameState.ProfileCount)return false;
        int index=town.GroundCount++;var g=town.Ground[index];g.Item=item;g.X=x;g.Z=z;g.Owner=owner;g.Day=day;g.Kind=4;g.Wrapped=wrapped;ClearRecovery(index);custody=g;return true;
    }
    public int[] Encode()
    {
        var data=new int[EncodedWordCount];int at=0;data[at++]=Magic;data[at++]=Version;data[at++]=sequence;data[at++]=RecoveryCount;
        for(int i=0;i<town.GroundCount;i++){
            var g=town.Ground[i];var r=RecoveryAt(i);if(r==null&&g.RecoveryId==0)continue;
            if(r==null||r.Id!=g.RecoveryId||!r.Matches(g)||r.Id<=0||r.Id>sequence)throw new InvalidOperationException("Custody receipt does not match its item");
            data[at++]=i;data[at++]=r.Id;data[at++]=(int)r.Source;data[at++]=r.Item;data[at++]=r.X;data[at++]=r.Z;data[at++]=r.Kind;data[at++]=r.Owner;data[at++]=r.Day;data[at++]=r.Rotation;data[at++]=r.Room;data[at++]=r.Wrapped?1:0;data[at++]=r.CustodyOwner;
        }
        return data;
    }
    public void Decode(int[] data)
    {
        if(data.Length<HeaderWords||data[0]!=Magic||data[1]!=Version||data[2]<0||data[3]<0||data[3]>town.Ground.Length||data.Length!=HeaderWords+data[3]*RecordWords)throw new InvalidOperationException("Invalid police-custody extension");
        int count=data[3],at=HeaderWords;var indexes=new int[count];var records=new TownRecoveryRecord[count];
        for(int n=0;n<count;n++){
            int index=data[at++],id=data[at++],source=data[at++],item=data[at++],x=data[at++],z=data[at++],kind=data[at++],owner=data[at++],day=data[at++],rotation=data[at++],room=data[at++],wrapped=data[at++],custodyOwner=data[at++];
            if((uint)index>=town.GroundCount||id<=0||id>data[2]||(uint)source>2||!Catalog.Valid(item)||!town.Inside(x,z)||(uint)custodyOwner>=GameState.ProfileCount||(uint)wrapped>1)throw new InvalidOperationException("Invalid custody identity");
            for(int i=0;i<n;i++)if(indexes[i]==index||records[i].Id==id)throw new InvalidOperationException("Duplicate custody identity");
            if(source==(int)TownRecoverySource.Placement){
                if(kind!=-1||!Catalog.Has(item,ItemFlags.Place)||(uint)owner>9||(uint)rotation>3||!BuildingWorld.ValidSpace(room)||wrapped!=0)throw new InvalidOperationException("Invalid recovered placement");
            }else{
                if((uint)owner>=GameState.ProfileCount||custodyOwner!=owner||rotation!=0||room!=0||(source==(int)TownRecoverySource.Ground?(uint)kind>3:kind!=-1))throw new InvalidOperationException("Invalid recovered ground item");
            }
            var record=new TownRecoveryRecord(id,(TownRecoverySource)source,item,x,z,kind,owner,day,rotation,room,wrapped!=0,custodyOwner);
            if(!record.Matches(town.Ground[index]))throw new InvalidOperationException("Recovery refers to changed custody");indexes[n]=index;records[n]=record;
        }
        // Allocate a table only for saves that actually contain receipts.
        TownRecoveryRecord?[]? decoded=count==0?null:new TownRecoveryRecord?[town.Ground.Length];
        for(int i=0;i<town.GroundCount;i++)town.Ground[i].RecoveryId=0;
        for(int i=0;i<count;i++){decoded![indexes[i]]=records[i];town.Ground[indexes[i]].RecoveryId=records[i].Id;}
        recoveries=decoded;sequence=data[2];
    }
}
