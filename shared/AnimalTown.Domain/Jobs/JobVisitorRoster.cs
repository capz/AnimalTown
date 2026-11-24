namespace AnimalTown.Jobs;

internal sealed class VisitorRosterReader
{
    private readonly byte[]? bytes;
    private readonly ISaveWordReader? source;
    private readonly int length;
    internal int Position {get;private set;}
    internal VisitorRosterReader(byte[] bytes){this.bytes=bytes;length=(bytes.Length-4)/4;}
    internal VisitorRosterReader(ISaveWordReader source,int byteLength){this.source=source;length=(byteLength-4)/4;}
    internal void Skip(int words)
    {if(words<0||words>length-Position)throw new InvalidOperationException("Truncated roster record");Position+=words;}
    internal int Int()
    {
        int offset=Position*4;
        Skip(1);
        if(source!=null)return source.ReadWord(offset);
        int word=0;for(int i=0;i<4;i++)word|=bytes![offset+i]<<(8*i);return word;
    }
    internal int Range(int min,int max){int value=Int();if(value<min||value>max)throw new InvalidOperationException("Invalid roster record value");return value;}
    internal ulong ULong(){uint low=unchecked((uint)Int()),high=unchecked((uint)Int());return low|((ulong)high<<32);}
    internal JobIdentity Key(bool empty=true)
    {var identity=new JobIdentity(ULong(),ULong());if(!identity.IsValid&&!(empty&&identity.IsEmpty))throw new InvalidOperationException("Invalid saved identity");return identity;}
}

public sealed partial class OfflineJobLedger
{
    // The projection scans wire headers and sparse-record counts only. In
    // particular, it skips each packed letter body without materializing it.
    internal static SavedVisitor[] ReadVisitorRoster(VisitorRosterReader reader,string[] names,ulong localTownId,int limit)
    {
        if(reader.Int()!=Magic)throw new InvalidOperationException("Missing saved job identity");
        int schema=reader.Range(7,Schema);
        if(schema>=12)reader.Range(0,1);
        ulong townId=reader.ULong(),nextSerial=reader.ULong();
        if(townId==0||nextSerial<7)throw new InvalidOperationException("Invalid saved town identity");
        if(townId==localTownId)return new SavedVisitor[0];
        reader.Range(0,int.MaxValue);
        const int identityWords=4;
        reader.Skip(OwnerCount*identityWords);
        var identities=new JobIdentity[Town.SavedVillagerCount];
        var outfits=new int[Town.SavedVillagerCount];
        for(int slot=0;slot<identities.Length;slot++){
            var identity=reader.Key(false);
            if(identity.Town==townId&&identity.Serial>=nextSerial)throw new InvalidOperationException("Invalid saved resident identity");
            for(int other=0;other<slot;other++)if(identity==identities[other])throw new InvalidOperationException("Duplicate saved resident identity");
            identities[slot]=identity;
            outfits[slot]=reader.Range(0,Catalog.Items.Length-1);
        }

        // Fixed economic header, then the sparse jobs, offers, rewards,
        // parcel bindings and relationships written by WriteRecords.
        reader.Skip(27+OwnerCount*17);
        reader.Skip(reader.Range(0,JobCapacity)*79);
        reader.Skip(reader.Range(0,OfferCapacity)*23);
        reader.Skip(reader.Range(0,RewardCapacity)*(schema>=11?30:28));
        reader.Skip(reader.Range(0,OwnerCount*GameState.MaximumPockets)*5);
        reader.Skip(reader.Range(0,OwnerCount*RelationsPerPlayer)*7);
        int letters=reader.Range(0,InboxCount+OutboxCapacity);
        for(int i=0;i<letters;i++){reader.Skip(30);reader.Skip(reader.Range(0,1001));}

        reader.Skip(2+GameState.ProfileCount*2);
        for(int i=0;i<WorldCapacity;i++)if(reader.Range(0,3)!=0)reader.Skip(14);
        for(int i=0;i<NoticeCapacity;i++)if(!reader.Key().IsEmpty){reader.Skip(6);SkipRosterText(reader,1000);}

        reader.Skip(18); // Trip receipt preceding its optional portable profile.
        if(reader.Range(0,1)!=0){
            // Portable profile header, identity text, inventory, mail and relationships.
            reader.Skip(17);int mail=reader.Range(0,MailboxCapacity);reader.Skip(15);
            SkipRosterText(reader,16);SkipRosterText(reader,32);
            reader.Skip(GameState.MaximumPockets+64+mail+Town.SavedVillagerCount*4);
        }
        var species=new int[Town.SavedVillagerCount];
        for(int slot=0;slot<species.Length;slot++)species[slot]=reader.Range(0,ModelResidencyDemand.SpeciesCount-1);
        int known=reader.Range(0,32);
        for(int i=0;i<known;i++){reader.Skip(6);SkipRosterText(reader,24);}

        reader.Skip(8);
        int firstSlot=reader.Range(0,Town.SavedVillagerCount-1),secondSlot=reader.Range(0,Town.SavedVillagerCount-1);
        int phase=reader.Range(0,4);
        bool firstSide=reader.Range(0,1)==1;
        int moving=phase>0&&phase<4?(firstSide?firstSlot:secondSlot):-1;
        int count=0;
        for(int slot=0;slot<Town.LocalResidentCount&&count<limit;slot++)if(slot!=moving)count++;
        var visitors=new SavedVisitor[count];
        for(int slot=0,index=0;index<count;slot++){
            if(slot==moving)continue;
            visitors[index++]=new SavedVisitor(townId,slot,identities[slot],names[slot],VillagerNames.DefaultPhrase(slot),species[slot],outfits[slot]);
        }
        return visitors;
    }
    private static void SkipRosterText(VisitorRosterReader reader,int maximumScalars)
        =>reader.Skip((reader.Range(0,maximumScalars*4)+3)/4);
}
