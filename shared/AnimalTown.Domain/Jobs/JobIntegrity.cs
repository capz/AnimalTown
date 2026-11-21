namespace AnimalTown.Jobs;

public sealed partial class OfflineJobLedger
{
    private void ValidateEconomicIdentities()
    {
        var ids=new JobIdentity[OwnerCount+6+Jobs.Length*2+Offers.Length+Rewards.Length+Mail.Length+World.Length+Notices.Length+OwnerCount+2];int count=0;
        void Add(JobIdentity id){if(id.IsEmpty)return;if(id.Town==TownId&&id.Serial>=nextSerial)throw new InvalidOperationException("Saved counter would reuse an economic identity");ids[count++]=id;}
        for(int p=0;p<OwnerCount;p++)Add(players[p]);for(int n=0;n<6;n++)Add(npcs[n]);
        for(int i=0;i<Jobs.Length;i++){
            var j=Jobs[i];if(!j.Occupying)continue;Add(j.Id);Add(j.Binding);
            if(!j.Shared&&j.Owner!=Player(i/PersonalPerPlayer)||j.Shared&&j.Requester!=Npc(i-SharedStart))throw new InvalidOperationException("Saved job occupies another villager's partition");
            int reward=FindReward(j.Obligation);if(j.Status!=JobStatus.CompletedRetained&&reward<0)throw new InvalidOperationException("Saved live job has no reserved reward");
            if(reward>=0&&Rewards[reward].Job!=j.Id)throw new InvalidOperationException("Saved reward belongs to another job");
            if(j.Template==JobTemplate.RetrievalBorrowed){for(int a=0;a<j.Borrowers;a++){if(!j.Borrower(a).IsValid||j.Borrower(a)==j.Requester)throw new InvalidOperationException("Invalid saved borrower chain");for(int b=0;b<a;b++)if(j.Borrower(a)==j.Borrower(b))throw new InvalidOperationException("Repeated saved borrower");}}
        }
        for(int i=0;i<Offers.Length;i++)if(!Offers[i].Id.IsEmpty){Add(Offers[i].Id);for(int j=0;j<i;j++)if(!Offers[j].Id.IsEmpty&&Offers[i].Owner==Offers[j].Owner&&Offers[i].Requester==Offers[j].Requester)throw new InvalidOperationException("Duplicate saved offer owner");}
        for(int i=0;i<Rewards.Length;i++)if(Rewards[i].State!=JobRewardState.Empty)Add(Rewards[i].Id);
        for(int i=0;i<Mail.Length;i++)if(Mail[i].State!=JobMailState.Empty)Add(Mail[i].Id);
        for(int i=0;i<World.Length;i++)if(World[i].Kind!=JobWorldKind.Empty)Add(World[i].Id);
        for(int i=0;i<Notices.Length;i++)Add(Notices[i].Id);for(int p=0;p<OwnerCount;p++)Add(OpeningParcels[p]);Add(Trip.Id);Add(NpcMove.Id);
        // In-place heapsort bounds validation to O(n log n), including a full
        // mailbox. It runs only on private readback/load candidates.
        void Sift(int root,int end){while(root*2+1<end){int child=root*2+1;if(child+1<end&&JobIdentity.Compare(ids[child],ids[child+1])<0)child++;if(JobIdentity.Compare(ids[root],ids[child])>=0)return;var swap=ids[root];ids[root]=ids[child];ids[child]=swap;root=child;}}
        for(int i=count/2-1;i>=0;i--)Sift(i,count);for(int end=count-1;end>0;end--){var swap=ids[0];ids[0]=ids[end];ids[end]=swap;Sift(0,end);}
        for(int i=1;i<count;i++)if(ids[i]==ids[i-1])throw new InvalidOperationException("Duplicate saved economic identity");
    }
}
