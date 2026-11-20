namespace AnimalTown.Jobs;

// Live economic records are separate from the 60 Hz availability clock. They
// share the existing small economy save record, never the terrain record.
public sealed partial class OfflineJobLedger
{
    public const int OwnerCount=GameState.ProfileCount+1,Visitor=GameState.ProfileCount;
    public const int MaximumWords=1024*1024;
    private const int Magic=0x32424f4a,Schema=7;
    public ulong TownId {get;private set;}
    private ulong nextSerial=1;
    public bool Initialized=>TownId!=0;
    public int Revision {get;internal set;}
    private readonly JobIdentity[] players=new JobIdentity[OwnerCount],npcs=new JobIdentity[TownJobState.ResidentCount];
    private readonly int[] outfits=new int[TownJobState.ResidentCount];
    public JobIdentity Player(int profile){if((uint)profile>=players.Length)throw new ArgumentOutOfRangeException(nameof(profile));return players[profile];}
    public JobIdentity Npc(int resident){if((uint)resident>=npcs.Length)throw new ArgumentOutOfRangeException(nameof(resident));return npcs[resident];}
    public int Outfit(int resident){if((uint)resident>=outfits.Length)throw new ArgumentOutOfRangeException(nameof(resident));return outfits[resident];}
    public int FindNpc(JobIdentity id){for(int i=0;i<npcs.Length;i++)if(npcs[i]==id&&!id.IsEmpty)return i;return -1;}
    public int FindPlayer(JobIdentity id){for(int i=0;i<players.Length;i++)if(players[i]==id&&!id.IsEmpty)return i;return -1;}
    public void Initialize(ulong townNonce)
    {
        if(townNonce==0)throw new ArgumentOutOfRangeException(nameof(townNonce));
        if(Initialized){if(TownId!=townNonce)throw new InvalidOperationException("Town identity is already established");return;}
        TownId=townNonce;EnsureRecords();for(int i=0;i<npcs.Length;i++)npcs[i]=Allocate();Revision++;
    }
    internal JobIdentity Allocate()
    {if(!Initialized||nextSerial==0||nextSerial==ulong.MaxValue)throw new InvalidOperationException("Job identity space unavailable");return new JobIdentity(TownId,nextSerial++);}
    internal bool CanAllocate(int count)=>Initialized&&count>0&&nextSerial>0&&nextSerial<=ulong.MaxValue-(uint)count;
    public JobIdentity EnsurePlayer(int profile)
    {
        if((uint)profile>=players.Length)throw new ArgumentOutOfRangeException(nameof(profile));
        if(players[profile].IsEmpty){players[profile]=Allocate();Revision++;}return players[profile];
    }
    internal void ForgetPlayer(int profile){players[profile]=default;Revision++;}
    internal void SetOutfit(int resident,int definition){outfits[resident]=definition;}
    internal int WordCount{get{var sizing=new JobWireWriter();Write(sizing);if(sizing.Count>MaximumWords)throw new InvalidOperationException("Job ledger exceeds its bound");return sizing.Count;}}
    internal void WriteTo(byte[] bytes,int offset)=>Write(new JobWireWriter(bytes,offset));
    internal int[] Encode()
    {
        var sizing=new JobWireWriter();Write(sizing);
        if(sizing.Count>MaximumWords)throw new InvalidOperationException("Job ledger exceeds its bounded format");
        var words=new int[sizing.Count];Write(new JobWireWriter(words));return words;
    }
    private void Write(JobWireWriter w)
    {
        w.Int(Magic);w.Int(Schema);w.ULong(TownId);w.ULong(nextSerial);w.Int(Revision);
        for(int i=0;i<players.Length;i++)w.Key(players[i]);
        for(int i=0;i<npcs.Length;i++){w.Key(npcs[i]);w.Int(outfits[i]);}
        WriteRecords(w);
        WriteMail(w);WriteWorld(w);WriteTravel(w);WriteNpcMove(w);WriteOpening(w);
    }
    internal void Decode(int[] words)=>Decode(new JobWireReader(words));
    internal void Decode(byte[] bytes,int offset,int words)=>Decode(new JobWireReader(bytes,offset,words));
    private void Decode(JobWireReader r)
    {
        if(r.Int()!=Magic)throw new InvalidOperationException("Unsupported offline job ledger");int version=r.Range(Schema,Schema);
        TownId=r.ULong();nextSerial=r.ULong();Revision=r.Range(0,int.MaxValue);
        if(TownId==0||nextSerial<7)throw new InvalidOperationException("Invalid saved town identity");
        for(int i=0;i<players.Length;i++)players[i]=r.Key();
        for(int i=0;i<npcs.Length;i++){npcs[i]=r.Key(false);outfits[i]=r.Range(0,Catalog.Items.Length-1);}
        EnsureRecords();if(version>=2)ReadRecords(r);if(version>=3)ReadMail(r);if(version>=4)ReadWorld(r);if(version>=5)ReadTravel(r);if(version>=6)ReadNpcMove(r);if(version>=7)ReadOpening(r);r.End();ValidateIdentities();ValidateEconomicIdentities();
    }
    private void ValidateIdentities()
    {
        for(int i=0;i<players.Length+npcs.Length;i++){
            var a=i<players.Length?players[i]:npcs[i-players.Length];if(a.IsEmpty)continue;
            if(a.Town==TownId&&a.Serial>=nextSerial)throw new InvalidOperationException("Saved identity counter would reuse a live identity");
            for(int j=0;j<i;j++){var b=j<players.Length?players[j]:npcs[j-players.Length];if(a==b)throw new InvalidOperationException("Duplicate saved villager identity");}
        }
    }
}
