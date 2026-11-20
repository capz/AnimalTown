namespace AnimalTown.Jobs;

public sealed class NpcJobAvailability
{
    public JobPersonality Personality {get;private set;}
    public int RemainingTicks {get;private set;}
    public int AcreBudgetTicks {get;private set;}
    public int ConversationCount {get;private set;}
    public bool FreshGenerationEnabled {get;private set;}=true;
    public bool CanStartConversation=>ConversationCount<JobRulesV1.Personality(Personality).RefusalCount;
    internal NpcJobAvailability(JobPersonality personality){Personality=personality;}

    public bool StartNoJobCooldown()
    {
        if(!FreshGenerationEnabled)return false;
        FreshGenerationEnabled=false;RestartTimer();return true;
    }
    private void RestartTimer()
    {RemainingTicks=JobRulesV1.Personality(Personality).CooldownTicks;AcreBudgetTicks=JobRulesV1.AcreProgressBudget;}
    public bool ConversationCompleted()
    {
        // The future dialogue adapter must call once per completed session,
        // never once per page, option, refusal-to-start or repeated command.
        var rule=JobRulesV1.Personality(Personality);
        if(!CanStartConversation)return false;
        ConversationCount++;
        if(ConversationCount>=rule.MildAnnoyanceCount)RestartTimer();
        return true;
    }
    internal void AcreEntered(){AcreBudgetTicks=Math.Min(JobRulesV1.AcreProgressBudget,RemainingTicks);}
    internal void Advance(int activeTicks)
    {
        if(RemainingTicks==0)return;
        int spent=Math.Min(activeTicks,Math.Min(RemainingTicks,AcreBudgetTicks));
        RemainingTicks-=spent;AcreBudgetTicks-=spent;
        if(RemainingTicks==0){ConversationCount=0;FreshGenerationEnabled=true;AcreBudgetTicks=0;}
    }
    internal void Restore(int personality,int remaining,int budget,int conversations,int fresh)
    {
        var rule=JobRulesV1.Personality((JobPersonality)personality);
        if(remaining<0||remaining>rule.CooldownTicks||budget<0||budget>Math.Min(remaining,JobRulesV1.AcreProgressBudget)||
            conversations<0||conversations>rule.RefusalCount||(fresh!=0&&fresh!=1)||remaining==0&&(budget!=0||fresh!=1))
            throw new InvalidOperationException("Invalid NPC job availability");
        Personality=(JobPersonality)personality;RemainingTicks=remaining;AcreBudgetTicks=budget;ConversationCount=conversations;FreshGenerationEnabled=fresh==1;
    }
}
