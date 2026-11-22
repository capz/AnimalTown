namespace AnimalTown.Jobs;

public enum JobPersonality { Normal,Peppy,Lazy,Jock,Cranky,Snooty }
public enum JobFamily { Delivery,Retrieval,Favor }
public enum JobTemplate
{
    DeliveryClothing,DeliveryForeign,DeliveryMoved,DeliveryProperty,RetrievalBorrowed,
    FavorFruit,FavorBall,FavorSnowman,FavorFlowers,FavorFish,FavorInsect,FavorLetter
}
public enum JobSeason { AllYear,Snowman,Flowers,Insects }
public enum JobRewardCategory { Furniture,Stationery,Clothing,Flooring,Wallpaper,Currency,WornClothing }

public readonly struct JobPersonalityRule
{
    public readonly int CooldownTicks,MildAnnoyanceCount,RefusalCount;
    internal JobPersonalityRule(int ticks,int mild,int refusal)
    {CooldownTicks=ticks;MildAnnoyanceCount=mild;RefusalCount=refusal;}
}

public readonly struct JobDefinition
{
    public readonly JobTemplate Template;
    public readonly JobFamily Family;
    public readonly int DeadlineDays;
    public readonly bool ParcelOnAcceptance,RequiresTravel;
    public readonly JobSeason Season;
    internal JobDefinition(JobTemplate template,JobFamily family,int days,bool parcel=false,bool travel=false,JobSeason season=JobSeason.AllYear)
    {Template=template;Family=family;DeadlineDays=days;ParcelOnAcceptance=parcel;RequiresTravel=travel;Season=season;}
}

// The versioned configuration asset is typed immutable data, compiled into the
// console binary. No runtime JSON, reflection, file I/O or mutable public tables.
// Existing host profile, resident, pocket and friendship limits take precedence.
public static class JobRulesV1
{
    public const int Version=1,TemplateCount=12;
    public const int DefaultProfiles=4,DefaultResidents=15,DefaultInventorySlots=15;
    public const int DeliveryRecordsPerPlayer=15,RetrievalRecordsPerPlayer=5;
    public const int FavorsPerResident=1,FavorsPerSubtype=1,OffersPerPlayerResident=1,ObligationSlots=128;
    public const int FreshGateDenominator=4,NoJobGateOutcome=0,FamilyCount=3,DeliverySubtypeCount=4,FavorSubtypeCount=7;
    public const bool RetryInvalidCandidate=false,GateOnFriendship=false;
    public const int ActiveTicksPerSecond=60,AcreProgressBudget=1000;
    public const bool AdvanceDuringDialogue=false,AdvanceDuringMenus=false,AdvanceOffline=false;
    public const int MaximumBorrowers=2,ContinueDenominator=2,ContinueOutcome=1,RetrievalStageDays=2;
    // Retain the original save slots and reward rows for chains already in
    // progress. New referrals must use MaximumBorrowers instead.
    internal const int MaximumRecordedBorrowers=4;
    public const int RetentionDays=3,FlowersToAdd=4,MaximumMatureFlowers=20,MinimumEmptyFlowerCells=4,LetterMaximumScalars=1000;
    public const int SecondsPerDay=86400,HouseFurnitureDenominator=10,DefaultMoneyPower=0,DefaultWalletLimit=99999;
    public const int DefaultMinimumFriendship=0,DefaultMaximumFriendship=127,SuccessFriendship=3,RefusalFriendship=-3,AbandonFriendship=-3,SupplyCancelFriendship=-1;
    public const int ClothingTimeoutFriendship=-5,PropertyTimeoutFriendship=-5,TravelTimeoutFriendship=-1,RetrievalSeekingTimeoutFriendship=0,RetrievalReturningTimeoutFriendship=-2;
    public const int WorldInvalidationFriendship=0,FavorExpiryFriendship=0;
    public const int JitterMinimum=9000,JitterOutcomes=2001,MoneyRateCap=700;
    public const uint OfferSeedSalt=0x9e3779b9,RewardSeedSalt=0x85ebca6b,HouseSeedSalt=0xc2b2ae35;
    public const bool RareClothingUsesOrdinaryPool=true,RareFurnitureUsesOrdinaryPool=true,RareCoveringsUseOrdinaryPool=true;

    private static readonly JobPersonalityRule[] personalities={
        new(4000,12,15),new(3000,10,13),new(4000,12,15),
        new(4000,10,13),new(5000,9,12),new(5000,9,12)
    };
    private static readonly JobDefinition[] definitions={
        new(JobTemplate.DeliveryClothing,JobFamily.Delivery,2,true),
        new(JobTemplate.DeliveryForeign,JobFamily.Delivery,2,true,true),
        new(JobTemplate.DeliveryMoved,JobFamily.Delivery,2,true,true),
        new(JobTemplate.DeliveryProperty,JobFamily.Delivery,2,true),
        new(JobTemplate.RetrievalBorrowed,JobFamily.Retrieval,2),
        new(JobTemplate.FavorFruit,JobFamily.Favor,1),
        new(JobTemplate.FavorBall,JobFamily.Favor,1),
        new(JobTemplate.FavorSnowman,JobFamily.Favor,1,season:JobSeason.Snowman),
        new(JobTemplate.FavorFlowers,JobFamily.Favor,3,season:JobSeason.Flowers),
        new(JobTemplate.FavorFish,JobFamily.Favor,3),
        new(JobTemplate.FavorInsect,JobFamily.Favor,3,season:JobSeason.Insects),
        new(JobTemplate.FavorLetter,JobFamily.Favor,2)
    };
    // Category order follows JobRewardCategory. Retrieval has its own four rows;
    // letters resolve through the rank table, never an ordinary reward roll.
    private static readonly int[][] rewardWeights={
        new[]{40,0,0,0,0,30,30},new[]{40,0,0,10,10,40,0},new[]{20,0,0,20,20,40,0},new[]{40,0,40,10,10,0,0},
        new[]{0,0,0,0,0,0,0},new[]{0,0,0,30,30,40,0},new[]{40,0,0,30,30,0,0},new[]{60,0,0,20,20,0,0},
        new[]{60,0,0,20,20,0,0},new[]{80,0,0,10,10,0,0},new[]{80,0,0,10,10,0,0},new[]{0,0,0,0,0,0,0}
    };
    private static readonly int[][] retrievalWeights={
        new[]{0,75,25,0,0,0,0},new[]{25,25,25,0,0,25,0},new[]{50,0,25,0,0,25,0},new[]{65,0,0,5,5,25,0}
    };
    private static readonly int[] moneyBases={200,1000,1000,0,0,500,0,0,0,0,0,0};
    private static readonly int[] retrievalMoneyBases={0,500,750,1000};

    public static JobPersonalityRule Personality(JobPersonality personality)
    {if((uint)personality>=personalities.Length)throw new ArgumentOutOfRangeException(nameof(personality));return personalities[(int)personality];}
    public static JobDefinition Definition(JobTemplate template)
    {if((uint)template>=definitions.Length)throw new ArgumentOutOfRangeException(nameof(template));return definitions[(int)template];}
    public static int RewardWeight(JobTemplate template,JobRewardCategory category,int borrowers=0)
    {
        Definition(template);if((uint)category>=(uint)rewardWeights[0].Length)throw new ArgumentOutOfRangeException(nameof(category));
        if(template!=JobTemplate.RetrievalBorrowed)return rewardWeights[(int)template][(int)category];
        if(borrowers<1||borrowers>MaximumRecordedBorrowers)throw new ArgumentOutOfRangeException(nameof(borrowers));
        return retrievalWeights[borrowers-1][(int)category];
    }
    public static int MoneyBase(JobTemplate template,int borrowers=0)
    {
        Definition(template);if(template!=JobTemplate.RetrievalBorrowed)return moneyBases[(int)template];
        if(borrowers<1||borrowers>MaximumRecordedBorrowers)throw new ArgumentOutOfRangeException(nameof(borrowers));return retrievalMoneyBases[borrowers-1];
    }
}
