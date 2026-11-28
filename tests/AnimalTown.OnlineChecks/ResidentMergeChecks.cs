using AnimalTown;
using AnimalTown.Jobs;
using AnimalTown.Online;

static class ResidentMergeChecks
{
    static GameState Copy(GameState state)=>SplitSaveCodec.Decode(SplitSaveCodec.Encode(state));
    public static void Run(Action<bool,string> check)
    {
        const long now=1800000000;const ulong issuer=0x9876543212345678;
        var baseline=new GameState(10091){Name="Fern",Savings=5000,Wallet=1000};
        baseline.ResidentJobs.Enable(0x1234567891234567,now);
        baseline.SelectProfile(1);baseline.Name="Mira";baseline.Savings=100;
        baseline.ResidentJobs.Enable(baseline.Town.Jobs.TownId,now);baseline.SelectProfile(0);baseline.CaptureProfile();
        var fern=baseline.Town.Jobs.Player(0);var mira=baseline.Town.Jobs.Player(1);var giver=baseline.Town.Jobs.Npc(0);
        var offline=Copy(baseline);
        offline.SelectProfile(1);offline.Savings+=5000;offline.DeleteProfile(0);offline.CaptureProfile();
        var hosted=Copy(baseline);hosted.Savings+=1240;hosted.ResidentJobs.EnableOnlineAdapter(now);
        var reward=hosted.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Rowan",11,0,now,new(issuer,1),new(issuer,2));
        check(hosted.ResidentJobs.DeliverOnlineText(giver,fern,"A new letter delivered while you were away.",now,new(issuer,3)),"Resident fixture has a new hosted letter");
        bool refused=false;
        try{OnlineTownMerge.Apply(Copy(offline),baseline,hosted,now);}catch(InvalidOperationException){refused=true;}
        check(refused,"Removed profiles require explicit review before applying hosted value");
        var recipients=new JobIdentity[GameState.ProfileCount];recipients[0]=mira;
        var decisions=new OnlineResidentDecisions(1,recipients);
        var merged=Copy(offline);OnlineTownMerge.Apply(merged,baseline,hosted,now,decisions);
        check(!merged.Profiles[0].Created&&merged.Town.Jobs.Player(0).IsEmpty&&merged.Savings==6340,
            "Review transfers only new online proceeds without duplicating a deleted profile's already transferred savings");
        check(merged.Town.Jobs.Reward(merged.Town.Jobs.FindReward(reward)).Owner==mira&&merged.MailCount==1&&merged.Town.Jobs.Letter(1,0).Recipient==mira,
            "A deleted profile's new hosted reward and letter retain their receipt IDs with the chosen surviving owner");
        var reloaded=Copy(merged);reloaded.ResidentJobs.EnableOnlineAdapter(now);
        check(reloaded.ResidentJobs.CollectOnlineReward(reward).Result==JobResult.Ok&&reloaded.Find(11)>=0&&
            reloaded.ResidentJobs.CollectOnlineReward(reward).Result==JobResult.NotAvailable,
            "Transferred earned value survives save reload and pays only once");
        var replaced=Copy(offline);replaced.SelectProfile(0);replaced.Name="Reed";replaced.Savings=25;
        replaced.ResidentJobs.Enable(replaced.Town.Jobs.TownId,now);var reed=replaced.Town.Jobs.Player(0);replaced.CaptureProfile();
        OnlineTownMerge.Apply(replaced,baseline,hosted,now,decisions);
        check(reed!=fern&&replaced.Profiles[0].Savings==25&&replaced.Profiles[1].Savings==6340&&
            replaced.Town.Jobs.Reward(replaced.Town.Jobs.FindReward(reward)).Owner==mira,
            "Reusing the removed profile's slot cannot capture its old identity's pending online value");
        var stale=new JobIdentity[4];stale[0]=fern;refused=false;
        try{OnlineTownMerge.Apply(Copy(offline),baseline,hosted,now,new OnlineResidentDecisions(1,stale));}catch(InvalidOperationException){refused=true;}
        check(refused,"A removed recipient identity cannot be selected even if its old slot exists");
        var added=Copy(baseline);added.SelectProfile(2);added.Name="Reed";added.Wallet=432;
        added.ResidentJobs.Enable(added.Town.Jobs.TownId,now);added.CaptureProfile();
        OnlineTownMerge.Apply(added,baseline,Copy(baseline),now,new OnlineResidentDecisions(4,new JobIdentity[4]));
        check(added.Name=="Reed"&&added.Wallet==432&&added.Town.Jobs.Player(2).IsValid,"Reviewed additions retain their saved identity and offline inventory");
        var full=Copy(offline);full.ResidentJobs.EnableOnlineAdapter(now);JobIdentity first=default;
        for(int i=0;i<OfflineJobLedger.RewardCapacity;i++)
        {var id=full.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Rowan",12,0,now);if(i==0)first=id;}
        byte[] retained=SaveCodec.Encode(full);refused=false;
        try{OnlineTownMerge.Apply(Copy(full),baseline,hosted,now,decisions);}catch(InvalidOperationException){refused=true;}
        check(refused&&SaveCodec.Encode(full).SequenceEqual(retained)&&full.Town.Jobs.FindReward(reward)<0,
            "A full reward ledger leaves the source card and unmerged hosted gift intact for another review attempt");
        check(full.ResidentJobs.CollectOnlineReward(first).Result==JobResult.Ok,"Making capacity collects an existing earned obligation");
        var retried=Copy(full);OnlineTownMerge.Apply(retried,baseline,hosted,now,decisions);
        check(retried.Savings==6340&&retried.Town.Jobs.FindReward(reward)>=0&&retried.Town.Jobs.FindReward(first)<0,
            "Retry after making room applies the new hosted proceeds and gift once without reviving a collected reward");
        var empty=Copy(baseline);empty.DeleteProfile(0);empty.DeleteProfile(1);empty.SelectProfile(0);empty.Name="Next";empty.Savings=25;
        empty.ResidentJobs.Enable(empty.Town.Jobs.TownId,now);var newborn=empty.Town.Jobs.Player(0);empty.CaptureProfile();
        var reborn=new JobIdentity[4];reborn[0]=reborn[1]=newborn;
        OnlineTownMerge.Apply(empty,baseline,Copy(baseline),now,new OnlineResidentDecisions(3,reborn));
        check(newborn!=fern&&newborn!=mira&&empty.Name=="Next"&&empty.Savings==25&&!empty.Profiles[1].Created,
            "Recreating a resident after the last deletion keeps a fresh identity and does not resurrect discarded baseline wealth");
    }
}
