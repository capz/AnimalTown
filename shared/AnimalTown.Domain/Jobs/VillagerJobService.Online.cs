namespace AnimalTown.Jobs;

public sealed partial class VillagerJobService
{
    // Hosted rewards enter the home ledger immediately, including owed items
    // and Bells which do not fit. No destination custody or shared visitor slot
    // is involved; the normal card save retains this recovery record.
    public JobIdentity RecordOnlineReward(JobIdentity giver, ulong town, string name, int item, int currency, long now)
    {
        if (!Enabled || state.Visiting || state.IsOnlineProjection || !giver.IsValid || town == 0 ||
            item != 0 && !Catalog.Valid(item) || currency < 0 || item == 0 && currency == 0 || !ledger.CanAllocate(2))
            throw new InvalidOperationException("Invalid hosted reward.");
        int slot = ledger.FreeReward(); if (slot < 0) throw new InvalidOperationException("Home reward records are full.");
        ledger.RememberNpc(giver, town, name);
        var job = ledger.Allocate(); var id = ledger.Allocate();
        ledger.Rewards[slot] = new VillagerJobReward { Id = id, Job = job, Owner = ledger.Player(state.ActiveProfile), Giver = giver,
            State = JobRewardState.Owed, Item = item, Currency = currency, Category = JobContent.CategoryOf(item), ResolvedAt = now, PortableOnline = true };
        ledger.Revision++; return id;
    }
    public JobOutcome CollectOnlineReward(JobIdentity id)
    {
        int at = ledger.FindReward(id);
        if (!Enabled || at < 0 || !ledger.Rewards[at].PortableOnline) return new JobOutcome(JobResult.NotAvailable, id);
        var outcome = Collect(id, -1, true); ledger.Revision++; return outcome;
    }
    public void RecordOnlineFriendship(JobIdentity npc, ulong town, string name, int delta)
    {
        if (!Enabled || !npc.IsValid || town == 0 || delta < -5 || delta > 5) throw new InvalidOperationException("Invalid hosted relationship.");
        ledger.RememberNpc(npc, town, name); Friendship(ledger.Player(state.ActiveProfile), npc, delta); ledger.Revision++;
    }
    public bool DeliverOnlineText(JobIdentity sender, JobIdentity recipient, string body, long at)
    {
        if (!Enabled || !sender.IsValid || !recipient.IsValid || !JobLetterGrader.TryGrade(body, false, out var grade) || !ledger.CanAllocate(1)) return false;
        int profile = ledger.FindPlayer(recipient);
        if ((uint)profile >= GameState.ProfileCount || !state.Profiles[profile].Created) return false;
        ledger.EnsureMail();
        var message = new JobMailRecord { Id = ledger.Allocate(), Owner = recipient, Sender = sender, Recipient = recipient,
            State = JobMailState.Inbox, SentAt = at, DueAt = at };
        ledger.StoreBody(ref message,JobTextBody.Pack(grade.NormalizedBody,grade.ScalarCount));
        if (!InsertIncoming(profile, message)) return false;
        ledger.Revision++; return true;
    }
}
