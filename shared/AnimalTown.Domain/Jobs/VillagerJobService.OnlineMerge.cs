using AnimalTown.Online;

namespace AnimalTown.Jobs;

public sealed partial class VillagerJobService
{
    // Mutate a private offline-sync candidate only. The caller discards it on
    // refusal; publication/acknowledgment is the separate card transaction.
    public void MergeHostedReceipts(GameState baseline,GameState hosted,long now,OnlineResidentDecisions? residents=null)
    {
        if(state.IsOnlineProjection||baseline.IsOnlineProjection||hosted.IsOnlineProjection||
            !ledger.Initialized||ledger.TownId!=baseline.Town.Jobs.TownId||ledger.TownId!=hosted.Town.Jobs.TownId||state.Visiting)
            throw new InvalidOperationException("These checkpoints do not share one home lineage.");
        EnableOnlineAdapter(now);var before=baseline.Town.Jobs;var remote=hosted.Town.Jobs;
        for(int i=0;i<remote.KnownNpcs.Length;i++)
        {
            var id=remote.KnownNpcs[i];if(id.IsEmpty)continue;
            bool known=false;for(int j=0;j<ledger.KnownNpcs.Length;j++)if(ledger.KnownNpcs[j]==id){known=true;break;}
            if(!known)ledger.RememberNpc(id,remote.KnownNpcTowns[i],remote.KnownNpcNames[i]);
        }
        for(int i=0;i<remote.Rewards.Length;i++)
        {
            var reward=remote.Rewards[i];
            if(reward.State==JobRewardState.Empty||!reward.PortableOnline||before.FindReward(reward.Id)>=0)continue;
            if(residents!=null)reward.Owner=residents.Resolve(state,baseline,reward.Owner);
            int profile=ledger.FindPlayer(reward.Owner);
            if((uint)profile>=GameState.ProfileCount||!state.Profiles[profile].Created)throw new InvalidOperationException("A returning reward belongs to a removed resident.");
            int existing=ledger.FindReward(reward.Id);
            if(existing>=0)
            {
                var same=ledger.Rewards[existing];
                if(same.Owner!=reward.Owner||same.Job!=reward.Job||same.Item!=reward.Item||same.Currency!=reward.Currency)
                    throw new InvalidOperationException("A hosted receipt has conflicting contents.");
                continue;
            }
            if(reward.Id.Town==ledger.TownId||reward.Job.Town==ledger.TownId)
                throw new InvalidOperationException("A hosted receipt reused the offline identity namespace.");
            int slot=ledger.FreeReward();if(slot<0)throw new InvalidOperationException("Make room in the home reward ledger before synchronizing.");
            ledger.Rewards[slot]=reward;
        }
        for(int i=0;i<remote.Mail.Length;i++)
        {
            var message=remote.Mail[i];
            if(message.State!=JobMailState.Inbox||HasMail(before,message.Id)||HasMail(ledger,message.Id))continue;
            if(residents!=null){message.Recipient=residents.Resolve(state,baseline,message.Recipient);message.Owner=message.Recipient;}
            int profile=ledger.FindPlayer(message.Recipient);
            if((uint)profile>=GameState.ProfileCount||!state.Profiles[profile].Created)throw new InvalidOperationException("A delivered letter belongs to a removed resident.");
            if(message.Id.Town==ledger.TownId)throw new InvalidOperationException("A hosted letter reused the offline identity namespace.");
            ledger.EnsureMail();
            if(message.BodyWords>0)
            {
                var body=new int[message.BodyWords];Array.Copy(message.Body!,message.BodyOffset,body,0,body.Length);ledger.StoreBody(ref message,body);
            }
            if(!InsertIncoming(profile,message))
            {
                int free=-1;for(int slot=OfflineJobLedger.InboxCount;slot<ledger.Mail.Length;slot++)if(ledger.Mail[slot].State==JobMailState.Empty){free=slot;break;}
                if(free<0)throw new InvalidOperationException("Make room for delivered letters before synchronizing.");
                // The ordinary card scheduler retries self-delivery after the
                // player makes inbox space. Keep the same ID and text body.
                message.State=JobMailState.Queued;message.Owner=message.Recipient;message.DueAt=now;ledger.Mail.Set(free,message);
            }
        }
        for(int profile=0;profile<GameState.ProfileCount;profile++)
        {
            var owner=remote.Player(profile);if(owner.IsEmpty)continue;
            for(int i=profile*OfflineJobLedger.RelationsPerPlayer;i<(profile+1)*OfflineJobLedger.RelationsPerPlayer;i++)
            {
                var relation=remote.Relations[i];if(relation.Npc.IsEmpty)continue;
                int old=before.RelationSlot(profile,relation.Npc,false);
                int friendship=old<0?0:before.Relations[old].Friendship,met=old<0?0:before.Relations[old].Met;
                if(relation.Friendship==friendship&&relation.Met==met)continue;
                int localProfile=ledger.FindPlayer(residents==null?owner:residents.Resolve(state,baseline,owner));
                if((uint)localProfile>=GameState.ProfileCount||!state.Profiles[localProfile].Created)throw new InvalidOperationException("A hosted relationship belongs to a removed resident.");
                int slot=ledger.RelationSlot(localProfile,relation.Npc,true);
                if(slot<0)throw new InvalidOperationException("The home relationship ledger is full.");
                int value=Math.Clamp(ledger.Relations[slot].Friendship+relation.Friendship-friendship,0,255);
                int seen=ledger.Relations[slot].Met|relation.Met;
                ledger.RememberRelation(localProfile,relation.Npc,value,seen);
                int npc=ledger.FindNpc(relation.Npc);
                if(npc>=0)
                {
                    var personal=state.ProfileAt(localProfile);personal.Relationships[npc*4]=value;personal.Relationships[npc*4+3]=seen;
                    if(localProfile==state.ActiveProfile){state.Town.Residents[npc].Friendship=value;state.Town.Residents[npc].Met=seen;}
                }
            }
        }
        ledger.Revision++;
    }
    static bool HasMail(OfflineJobLedger source,JobIdentity id)
    {for(int i=0;i<source.Mail.Length;i++)if(source.Mail[i].State!=JobMailState.Empty&&source.Mail[i].Id==id)return true;return false;}
}
