using AnimalTown.Jobs;

namespace AnimalTown.Online;

// A review approves the saved roster, never rolls back a completed two-card
// move. Removed owners' new hosted effects have an explicit surviving owner.
public sealed class OnlineResidentDecisions
{
    readonly int approved;
    readonly JobIdentity[] recipients;
    public OnlineResidentDecisions(int approvedChanges,JobIdentity[] recipients)
    {
        if(recipients.Length!=GameState.ProfileCount)throw new ArgumentException("Four recipient identities are required.");
        approved=approvedChanges;this.recipients=(JobIdentity[])recipients.Clone();
    }
    public static int Changes(GameState local,GameState baseline)
    {
        int result=0;
        for(int p=0;p<GameState.ProfileCount;p++)if(local.Town.Jobs.Player(p)!=baseline.Town.Jobs.Player(p))result|=1<<p;
        for(int n=0;n<Town.SavedVillagerCount;n++)if(local.Town.Jobs.Npc(n)!=baseline.Town.Jobs.Npc(n))result|=1<<(GameState.ProfileCount+n);
        return result;
    }
    public void Validate(GameState local,GameState baseline,GameState hosted)
    {
        if(approved!=Changes(local,baseline)||Changes(hosted,baseline)!=0)
            throw new InvalidOperationException("The resident review no longer matches these checkpoints.");
        for(int p=0;p<GameState.ProfileCount;p++)
            if(!baseline.Town.Jobs.Player(p).IsEmpty)Resolve(local,baseline,baseline.Town.Jobs.Player(p));
    }
    public JobIdentity Resolve(GameState local,GameState baseline,JobIdentity owner)
    {
        int existing=local.Town.Jobs.FindPlayer(owner);
        if((uint)existing<GameState.ProfileCount&&local.Profiles[existing].Created)return owner;
        int previous=baseline.Town.Jobs.FindPlayer(owner);
        if((uint)previous>=GameState.ProfileCount||(approved&(1<<previous))==0)
            throw new InvalidOperationException("This online value has no reviewed owner.");
        var recipient=recipients[previous];int target=local.Town.Jobs.FindPlayer(recipient);
        if((uint)target>=GameState.ProfileCount||!local.Profiles[target].Created)
            throw new InvalidOperationException("Choose a surviving resident for the pending online value.");
        return recipient;
    }
}
