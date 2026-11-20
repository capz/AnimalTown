namespace AnimalTown.Jobs;

public interface IJobRandom { int NextInt(int exclusiveMaximum); }

// State zero is valid. Rejection sampling avoids modulo bias and consumes a
// precisely specified number of draws on every architecture, including PPC.
public sealed class JobRandom : IJobRandom
{
    public uint State {get;private set;}
    public JobRandom(uint state){State=state;}
    internal void Restore(uint state){State=state;}
    public int NextInt(int exclusiveMaximum)
    {
        if(exclusiveMaximum<=0)throw new ArgumentOutOfRangeException(nameof(exclusiveMaximum));
        ulong bound=(uint)exclusiveMaximum,limit=(0x100000000UL/bound)*bound;
        uint value;
        do{State=unchecked(1664525u*State+1013904223u);value=State;}while((ulong)value>=limit);
        return (int)((ulong)value%bound);
    }
}

// Pure selection/math primitives, not an offer or payout API. The transaction
// layer must freeze selections before they can affect inventory or the wallet.
public static class JobSelection
{
    public static bool RollTemplate(IJobRandom random,out JobTemplate template)
    {
        template=default;
        if(random.NextInt(JobRulesV1.FreshGateDenominator)==JobRulesV1.NoJobGateOutcome)return false;
        int family=random.NextInt(JobRulesV1.FamilyCount);
        if(family==(int)JobFamily.Delivery)template=(JobTemplate)random.NextInt(JobRulesV1.DeliverySubtypeCount);
        else if(family==(int)JobFamily.Retrieval)template=JobTemplate.RetrievalBorrowed;
        else template=JobTemplate.FavorFruit+random.NextInt(JobRulesV1.FavorSubtypeCount);
        return true;
    }
    public static JobRewardCategory RollReward(IJobRandom random,JobTemplate template,int borrowers=0)
    {
        // Reject letters/invalid borrower counts before consuming RNG.
        int total=0;for(int i=0;i<7;i++)total+=JobRulesV1.RewardWeight(template,(JobRewardCategory)i,borrowers);
        if(total!=100)throw new InvalidOperationException("Template has no ordinary reward table");
        int draw=random.NextInt(total);
        for(int i=0;i<7;i++){draw-=JobRulesV1.RewardWeight(template,(JobRewardCategory)i,borrowers);if(draw<0)return (JobRewardCategory)i;}
        throw new InvalidOperationException("Invalid reward configuration");
    }
    public static int MoneyRate(int moneyPower)
    {
        long m=moneyPower,rate;
        if(m<=0)return 0;
        if(m<100)rate=3*m/10;
        else if(m<200)rate=(7*m-400)/10;
        else if(m<300)rate=(27*m-4400)/10;
        else if(m<400)rate=(13*m-200)/10;
        else if(m<500)rate=m+100;
        else if(m<600)rate=(6*m+3000)/10;
        else rate=(12*m+6600000)/10000;
        return (int)Math.Min(JobRulesV1.MoneyRateCap,rate);
    }
    public static int RollMoney(IJobRandom random,int baseAmount,int moneyPower=JobRulesV1.DefaultMoneyPower)
    {
        if(baseAmount<0)throw new ArgumentOutOfRangeException(nameof(baseAmount));
        // Validate the output bound before drawing so a rejected operation
        // cannot advance the persisted reward stream.
        int rate=MoneyRate(moneyPower);
        if((long)baseAmount*(JobRulesV1.JitterMinimum+JobRulesV1.JitterOutcomes-1)*(100+rate)/1000000>int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(baseAmount));
        int jitter=JobRulesV1.JitterMinimum+random.NextInt(JobRulesV1.JitterOutcomes);
        return (int)((long)baseAmount*jitter*(100+rate)/1000000);
    }
}
