namespace AnimalTown.Jobs;

// Bounded foundation only: no job/item ownership is inferred from roster or
// profile indices. Durable identities and transactions precede playable jobs.
public sealed class TownJobState
{
    public const int ResidentCount=6,EncodedWordCount=40;
    private const int Magic=0x31424f4a;
    private readonly uint seed;
    private readonly NpcJobAvailability[] residents=new NpcJobAvailability[ResidentCount];
    private readonly int[] lastOutdoorAcres=new int[GameState.ProfileCount];
    public readonly JobRandom Offers,Rewards,HouseCache;
    public TownJobState(int seed)
    {
        this.seed=unchecked((uint)seed);
        Offers=new JobRandom(this.seed^JobRulesV1.OfferSeedSalt);Rewards=new JobRandom(this.seed^JobRulesV1.RewardSeedSalt);HouseCache=new JobRandom(this.seed^JobRulesV1.HouseSeedSalt);
        for(int i=0;i<residents.Length;i++)residents[i]=new NpcJobAvailability((JobPersonality)i);
        for(int i=0;i<lastOutdoorAcres.Length;i++)lastOutdoorAcres[i]=-1;
    }
    public NpcJobAvailability Resident(int index)
    {if((uint)index>=ResidentCount)throw new ArgumentOutOfRangeException(nameof(index));return residents[index];}
    public int LastOutdoorAcre(int profile)
    {if((uint)profile>=lastOutdoorAcres.Length)throw new ArgumentOutOfRangeException(nameof(profile));return lastOutdoorAcres[profile];}

    // Caller splits batches at acre crossings, conversation completions and
    // pause boundaries. A physical acre entry refills, never adds to, budget.
    public void Advance(int activeTicks)
    {
        if(activeTicks<0)throw new ArgumentOutOfRangeException(nameof(activeTicks));
        for(int i=0;i<residents.Length;i++)residents[i].Advance(activeTicks);
    }
    public bool ObserveOutdoorAcre(int profile,int acre)
    {
        if((uint)profile>=lastOutdoorAcres.Length)throw new ArgumentOutOfRangeException(nameof(profile));
        if((uint)acre>=Town.ScreenColumns*Town.ScreenRows)throw new ArgumentOutOfRangeException(nameof(acre));
        int previous=lastOutdoorAcres[profile];lastOutdoorAcres[profile]=acre;
        if(previous<0||previous==acre)return false;
        for(int i=0;i<residents.Length;i++)residents[i].AcreEntered();return true;
    }
    public void ForgetProfileLocation(int profile)
    {if((uint)profile>=lastOutdoorAcres.Length)throw new ArgumentOutOfRangeException(nameof(profile));lastOutdoorAcres[profile]=-1;}
    public bool IsPristine
    {
        get{
            if(Offers.State!=(seed^JobRulesV1.OfferSeedSalt)||Rewards.State!=(seed^JobRulesV1.RewardSeedSalt)||HouseCache.State!=(seed^JobRulesV1.HouseSeedSalt))return false;
            for(int i=0;i<lastOutdoorAcres.Length;i++)if(lastOutdoorAcres[i]!=-1)return false;
            for(int i=0;i<residents.Length;i++){var n=residents[i];if((int)n.Personality!=i||n.RemainingTicks!=0||n.AcreBudgetTicks!=0||n.ConversationCount!=0||!n.FreshGenerationEnabled)return false;}
            return true;
        }
    }
    internal int[] Encode()
    {
        var data=new int[EncodedWordCount];int p=0;
        data[p++]=Magic;data[p++]=JobRulesV1.Version;data[p++]=ResidentCount;
        data[p++]=unchecked((int)Offers.State);data[p++]=unchecked((int)Rewards.State);data[p++]=unchecked((int)HouseCache.State);
        for(int i=0;i<lastOutdoorAcres.Length;i++)data[p++]=lastOutdoorAcres[i];
        for(int i=0;i<residents.Length;i++){var n=residents[i];data[p++]=(int)n.Personality;data[p++]=n.RemainingTicks;data[p++]=n.AcreBudgetTicks;data[p++]=n.ConversationCount;data[p++]=n.FreshGenerationEnabled?1:0;}
        return data;
    }
    internal void Restore(int[] data)
    {
        if(data.Length!=EncodedWordCount||data[0]!=Magic||data[1]!=JobRulesV1.Version||data[2]!=ResidentCount)
            throw new InvalidOperationException("Unsupported job state schema or rules version");
        // Called only on a private decoding candidate, never on the live town.
        int p=3;Offers.Restore(unchecked((uint)data[p++]));Rewards.Restore(unchecked((uint)data[p++]));HouseCache.Restore(unchecked((uint)data[p++]));
        for(int i=0;i<lastOutdoorAcres.Length;i++){int acre=data[p++];if(acre< -1||acre>=Town.ScreenColumns*Town.ScreenRows)throw new InvalidOperationException("Invalid saved job acre");lastOutdoorAcres[i]=acre;}
        for(int i=0;i<residents.Length;i++){residents[i].Restore(data[p],data[p+1],data[p+2],data[p+3],data[p+4]);p+=5;}
    }
}
