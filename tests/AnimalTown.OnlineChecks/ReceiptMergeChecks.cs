using AnimalTown;
using AnimalTown.Jobs;
using AnimalTown.Online;

static class ReceiptMergeChecks
{
    static GameState Copy(GameState state)=>SplitSaveCodec.Decode(SplitSaveCodec.Encode(state));
    public static void Run(Action<bool,string> check)
    {
        const long now=1800000000;const ulong issuer=0x9988112233445566;
        var baseline=new GameState(9017){Name="Home",Wallet=1000};baseline.ResidentJobs.Enable(0x11229988776655,now);
        var giver=new JobIdentity(0x77665544332211,1);
        var offline=Copy(baseline);offline.ResidentJobs.EnableOnlineAdapter(now);offline.Wallet=700;offline.Pockets[0]=10;
        var local=offline.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Maple",12,0,now);
        var hosted=Copy(baseline);hosted.ResidentJobs.EnableOnlineAdapter(now);
        var reward=hosted.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Maple",11,45,now,new(issuer,1),new(issuer,2));
        check(hosted.ResidentJobs.DeliverOnlineText(giver,hosted.Town.Jobs.Player(0),"Thank you for visiting. I hope you enjoyed our town!",now,new(issuer,3)),"Fixture records an independently issued hosted letter");
        hosted.ResidentJobs.RecordOnlineFriendship(giver,giver.Town,"Maple",3);
        var merged=Copy(offline);merged.ResidentJobs.MergeHostedReceipts(baseline,hosted,now);
        check(merged.Wallet==700&&merged.Pockets.SequenceEqual(offline.Pockets),"Receipt merge preserves ordinary offline inventory and spending");
        check(merged.Town.Jobs.FindReward(local)>=0&&merged.Town.Jobs.FindReward(reward)>=0&&merged.MailCount==1,
            "Independent offline records, permanent online earnings and delivered text survive together");
        var nextOffline=Copy(offline);nextOffline.ResidentJobs.EnableOnlineAdapter(now);
        var nextMerged=Copy(merged);nextMerged.ResidentJobs.EnableOnlineAdapter(now);
        check(nextOffline.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Maple",13,0,now)==
            nextMerged.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Maple",13,0,now),"Merging foreign receipts cannot consume or reuse a local card identity");
        var reloaded=Copy(merged);reloaded.ResidentJobs.Enable(reloaded.Town.Jobs.TownId,now);
        var paid=reloaded.ResidentJobs.Execute(reloaded.ResidentJobs.Command(JobCommandKind.RecoverReward,reward));
        check(paid.Result==JobResult.Ok&&reloaded.Wallet==745&&reloaded.Find(11)>=0,"Merged earnings remain payable once after a home-card reload");
        var full=Copy(offline);full.MailCount=128;Array.Fill(full.Mail,4);
        full.ResidentJobs.MergeHostedReceipts(baseline,hosted,now);
        check(full.MailCount==128,"A full local inbox does not overwrite a letter during reconciliation");
        full.MailCount=127;full.Mail[127]=0;full.ResidentJobs.Enable(full.Town.Jobs.TownId,now);
        check(full.MailCount==128&&full.Town.Jobs.Letter(full.ActiveProfile,127).Id==new JobIdentity(issuer,3),
            "A merged letter waits for mailbox space and keeps its stable identity");
        var other=new GameState(9018);other.ResidentJobs.Enable(0x5566778899,now);bool rejected=false;
        try{Copy(offline).ResidentJobs.MergeHostedReceipts(baseline,other,now);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"Receipt reconciliation refuses a different home lineage");
        var sales=Copy(baseline);sales.Savings=100;sales.CaptureProfile();
        var spent=Copy(sales);spent.Savings=70;spent.HostedCommerceLocked=true;
        var sold=Copy(sales);sold.Savings=130;sold.Town.Stock[0]=0;sold.Town.Shop.Revision++;
        OnlineTownMerge.Apply(spent,sales,sold,now);
        check(spent.Savings==100&&spent.Town.Stock[0]==0,"Three-way sync adds permanent sales to independently spent offline savings");
        var locked=Copy(baseline);locked.HostedCommerceLocked=true;locked.Wallet=99999;locked.Pockets[0]=10;
        check(!locked.Town.Shop.List(locked,0,0,100)&&!locked.BuyCabinet(0)&&!locked.Town.Shop.BuyNpc(locked,0,ShopSource.Cabinet,0),
            "Offline copies cannot spend, cancel or duplicate a hosted shop's escrow");
        var ground=Copy(baseline);ground.Town.AddGround(10,13,24,4,5,true);
        var offlineGround=Copy(baseline);offlineGround.Town.AddGround(11,14,24,4,6);
        OnlineTownMerge.Apply(offlineGround,baseline,ground,now);
        check(offlineGround.Town.Ground.Take(offlineGround.Town.GroundCount).Any(g=>g.Kind==4&&g.Item==10&&g.Wrapped)&&
            offlineGround.Town.Ground.Take(offlineGround.Town.GroundCount).Any(g=>g.Kind==4&&g.Item==11),"Independent offline and reviewed hosted custody both survive sync");
        var garden=Copy(baseline);check(garden.PlaceHouse(12,22,false,true),"Conflict fixture has a home garden");
        garden.Pockets[3]=10;check(garden.Place(3,13,24,0,0),"Conflict fixture places offline furniture");
        int groundCount=garden.Town.GroundCount;
        OnlineTownMerge.KeepPlacement(garden,new Placement{Item=12,X=13,Z=24,Owner=0});
        check(garden.Town.PlacedCount==1&&garden.Town.Placed[0].Item==12&&garden.Town.GroundCount==groundCount+1&&
            garden.Town.Ground.Take(garden.Town.GroundCount).Any(g=>g.Kind==4&&g.Item==10),"Keeping a conflicting guest placement puts the displaced offline item in the koban");
    }
}
