using AnimalTown;
using AnimalTown.Jobs;

internal static class OfflineJobChecks
{
    private sealed class Draws : IJobRandom
    {
        private readonly int[] values;internal int At;
        internal Draws(params int[] values){this.values=values;}
        public int NextInt(int maximum){if(At>=values.Length)throw new Exception("Unexpected job draw at "+At+" with bound "+maximum);int n=values[At++];if(n<0||n>=maximum)throw new Exception("Bad fake job draw "+n+" / "+maximum);return n;}
    }
    private static long noon=>JobCalendar.At(2026,6,10,12);
    private static VillagerJobService Service(GameState s,Draws offers,Draws rewards)
    {s.Tutorial=6;var service=new VillagerJobService(s,offers,rewards);service.Enable(12345,noon);return service;}
    private static void Speak(GameState s,VillagerJobService service,int npc)
    {service.EndConversation();var v=s.Town.Residents[npc];s.Space=v.Space;s.X=v.PosX;s.Z=v.PosZ;if(!service.BeginConversation(npc,out _))throw new Exception("NPC context unavailable in fixture");}
    private static JobOutcome Do(VillagerJobService service,JobCommandKind kind,JobIdentity id=default,int revision=-1,int pocket=-1)=>service.Execute(service.Command(kind,id,revision,pocket));
    private static JobIdentity Accept(GameState s,VillagerJobService service,int npc,Action<bool,string> check)
    {Speak(s,service,npc);var offer=Do(service,JobCommandKind.Ask);check(offer.Result==JobResult.ExistingOffer,"explicit ask stores a complete offer");var snapshot=s.Town.Jobs.Offer(s.ActiveProfile,npc);var accepted=Do(service,JobCommandKind.Accept,offer.Job,snapshot.Revision);check(accepted.Result==JobResult.Ok,"explicit acceptance reserves the job and reward");return accepted.Job;}
    internal static void Run(Action<bool,string> check)
    {
        Deliveries(check);OffersAndBindings(check);Retrieval(check);RetrievalLimit(check);LegacyRetrievalLimit(check);Shared(check);Clocks(check);
        Console.WriteLine("Offline errands: physical contexts, stored offers, bound parcels, delivery/retrieval, shared claims, exact rewards and time checks passed");
    }
    private static void Deliveries(Action<bool,string> check)
    {
        var s=new GameState(17);var rng=new Draws(1,0,0,0,0);var rewards=new Draws(70,0);var service=Service(s,rng,rewards);
        var id=Accept(s,service,0,check);int slot=s.Town.Jobs.FindJob(id);var job=s.Town.Jobs.Job(slot);int target=s.Town.Jobs.FindNpc(job.Target),bound=Array.FindIndex(s.Pockets,x=>x==18&&Array.IndexOf(s.Pockets,x)>=0);
        Speak(s,service,target);check(Do(service,JobCommandKind.Handoff,id,job.Revision,2).Result==JobResult.WrongItem,"ordinary lookalike parcel cannot substitute for the bound instance");
        var command=service.Command(JobCommandKind.Handoff,id,job.Revision);var result=service.Execute(command);
        check(result.Result==JobResult.Ok&&s.Town.Jobs.FindJob(id)<0&&s.Town.Jobs.Outfit(target)==4,"clothing delivery consumes its parcel, equips the selected shirt and closes after payout");
        check(s.Town.Residents[target].Friendship==3&&s.Town.Residents[0].Friendship==0,"delivery recipient gets exactly the ordinary relationship effect");
        int[] pockets=(int[])s.Pockets.Clone();int wallet=s.Wallet;service.Execute(command);
        check(s.Pockets.SequenceEqual(pockets)&&s.Wallet==wallet&&s.Town.Residents[target].Friendship==3,"replayed completion cannot consume or reward twice");
        var changed=new JobCommand(JobCommandKind.Abandon,service.Conversation,command.Sequence,id);
        check(service.Execute(changed).Result==JobResult.StaleCommand,"reusing a command sequence for different content is rejected");
        var full=new GameState(21);var ps=Service(full,new Draws(1,0,3,0,0),new Draws(0,1,0));for(int i=0;i<full.Capacity;i++)full.Pockets[i]=1;full.Pockets[9]=0;
        var pid=Accept(full,ps,0,check);var pj=full.Town.Jobs.Job(full.Town.Jobs.FindJob(pid));Speak(full,ps,full.Town.Jobs.FindNpc(pj.Target));
        check(Do(ps,JobCommandKind.Handoff,pid,pj.Revision).Result==JobResult.Ok&&full.Pockets[9]==10,"full-pocket property delivery uses the just-freed parcel slot for its item reward");
        var cash=new GameState(22);var cs=Service(cash,new Draws(1,0,0,0,0),new Draws(40,1000));cash.Wallet=99900;
        var cid=Accept(cash,cs,0,check);var cj=cash.Town.Jobs.Job(cash.Town.Jobs.FindJob(cid));Speak(cash,cs,cash.Town.Jobs.FindNpc(cj.Target));
        check(Do(cs,JobCommandKind.Handoff,cid,cj.Revision).Amount==99&&cash.Wallet==99999&&cash.Town.Jobs.FindJob(cid)>=0,"partial wallet credit retains a personal reward-owed record");
        cash.Deposit(1000);check(Do(cs,JobCommandKind.Collect,cj.Obligation).Amount==101&&cash.Town.Jobs.FindJob(cid)<0,"later collection pays only the frozen currency remainder");
    }
    private static void OffersAndBindings(Action<bool,string> check)
    {
        var s=new GameState(23);var offers=new Draws(1,0,3,0,0);var service=Service(s,offers,new Draws());Speak(s,service,0);
        var offer=Do(service,JobCommandKind.Ask);var snapshot=s.Town.Jobs.Offer(0,0);int draws=offers.At;
        check(Do(service,JobCommandKind.Ask).Job==offer.Job&&offers.At==draws,"reopening a stored offer cannot reroll its payload");
        for(int i=0;i<s.Capacity;i++)s.Pockets[i]=1;check(Do(service,JobCommandKind.Accept,offer.Job,snapshot.Revision).Result==JobResult.FullPockets&&s.Town.Jobs.Offer(0,0).Id==offer.Job,"capacity failure preserves the same stored offer");
        s.Town.Residents[0].Friendship=20;Do(service,JobCommandKind.Decline,offer.Job,snapshot.Revision);var refused=s.Town.Jobs.Offer(0,0);Do(service,JobCommandKind.Decline,offer.Job,refused.Revision);
        check(s.Town.Residents[0].Friendship==17&&offers.At==draws,"decline effect occurs once per stored offer");
        s.Pockets[9]=0;var accepted=Do(service,JobCommandKind.Accept,offer.Job,refused.Revision);int at=s.Town.Jobs.FindJob(accepted.Job);var job=s.Town.Jobs.Job(at);
        check(s.IsJobItem(9)&&!s.Sell(9)&&!s.Use(9,0,30,40)&&!s.Place(9,30,40,0,0)&&!s.Town.Shop.List(s,0,9,100),"parcel protection reaches normal item and shop mutation APIs");
        s.SwapPockets(9,0);check(s.IsJobItem(0)&&!s.IsJobItem(9),"pocket rearrangement moves the exact binding with the item");
        var saved=SaveCodec.Decode(SaveCodec.Encode(s));var restored=new VillagerJobService(saved);restored.Enable(saved.Town.Jobs.TownId,noon+10);
        check(saved.IsJobItem(0)&&saved.Town.Jobs.Job(saved.Town.Jobs.FindJob(job.Id)).Target==job.Target,"bound jobs and full offers round-trip through the complete save");
        saved.Pockets[0]=0;restored.RecoverBindings();check(saved.Town.Jobs.FindJob(job.Id)<0&&!saved.IsJobItem(0),"missing parcel recovery cancels without replacement minting");
        check(Do(service,JobCommandKind.Abandon,job.Id).Result==JobResult.Ok&&!s.IsJobItem(0)&&s.Town.Residents[0].Friendship==14,"abandonment removes the bound parcel and applies its penalty once");
    }
    private static void Retrieval(Action<bool,string> check)
    {
        var s=new GameState(24);var offers=new Draws(1,1,0,0,0);var service=Service(s,offers,new Draws(0,0));for(int i=0;i<s.Capacity;i++)s.Pockets[i]=1;
        var id=Accept(s,service,0,check);var j=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));check(j.Borrowers==1&&j.Status==JobStatus.Seeking,"retrieval starts without a parcel slot and counts the first borrower");
        Speak(s,service,s.Town.Jobs.FindNpc(j.Target));check(Do(service,JobCommandKind.Handoff,id,j.Revision).Result==JobResult.FullPockets,"final pickup waits for a free pocket");
        int draws=offers.At;j=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));check(j.ChainDecision==2&&j.DueAt==noon+2*86400,"full pickup preserves its selected stop and original stage deadline");
        s.Pockets[9]=0;service.AdvanceTo(noon+100);Do(service,JobCommandKind.Handoff,id,j.Revision);j=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));
        check(offers.At==draws&&j.Status==JobStatus.Returning&&j.Target==j.Requester&&j.DueAt==noon+100+2*86400,"retry mints one bound object and refreshes the deadline only on committed pickup");
        Speak(s,service,0);check(Do(service,JobCommandKind.Handoff,id,j.Revision).Result==JobResult.Ok&&s.Find(Catalog.WritingPaper)>=0,"one-borrower retrieval pays the stationery reward row");
    }
    private static void RetrievalLimit(Action<bool,string> check)
    {
        check(JobRulesV1.MaximumBorrowers==2,"retrieval is capped at two borrowers, with at most one redirect");
        var s=new GameState(24);var offers=new Draws(1,1,0,0,1,0);var service=Service(s,offers,new Draws());
        // Every candidate has a separate home acre, so a continuation really
        // has somewhere to go rather than stopping because no NPC is eligible.
        for(int n=0;n<6;n++)s.Town.Residents[n].HomeX=5+n*20;
        Array.Fill(s.Pockets,1,0,s.Capacity);
        var id=Accept(s,service,0,check);int at=s.Town.Jobs.FindJob(id);var first=s.Town.Jobs.Job(at);
        service.AdvanceTo(noon+60);Speak(s,service,s.Town.Jobs.FindNpc(first.Target));
        check(Do(service,JobCommandKind.Handoff,id,first.Revision).Result==JobResult.ExistingJob,"first borrower can redirect once even with full pockets");
        var second=s.Town.Jobs.Job(at);int draws=offers.At;
        check(second.Borrowers==2&&second.Borrower1==first.Target&&second.Borrower2==second.Target&&second.Target!=first.Target&&second.Target!=second.Requester,"redirect commits exactly two distinct borrowers excluding the owner");
        check(second.DueAt==noon+60+2*86400&&second.OriginalDueAt==first.OriginalDueAt,"the single redirect refreshes only its stage deadline");
        var saved=SaveCodec.Decode(SaveCodec.Encode(s));var noMoreOffers=new Draws();var resumed=new VillagerJobService(saved,noMoreOffers,new Draws(75,1000));resumed.Enable(saved.Town.Jobs.TownId,noon+60);
        var j=saved.Town.Jobs.Job(saved.Town.Jobs.FindJob(id));Speak(saved,resumed,saved.Town.Jobs.FindNpc(j.Target));
        check(Do(resumed,JobCommandKind.Handoff,id,j.Revision).Result==JobResult.FullPockets,"second borrower must offer the item rather than another redirect");
        j=saved.Town.Jobs.Job(saved.Town.Jobs.FindJob(id));
        check(j.Borrowers==2&&j.ChainDecision==2&&j.NextBorrower.IsEmpty&&noMoreOffers.At==0&&offers.At==draws&&j.DueAt==second.DueAt,"cap and full-pocket retries never draw another referral or refresh its deadline");
        saved.Pockets[saved.Capacity-1]=0;
        check(Do(resumed,JobCommandKind.Handoff,id,j.Revision).Result==JobResult.Ok,"making space collects the item from the second borrower");
        j=saved.Town.Jobs.Job(saved.Town.Jobs.FindJob(id));
        check(j.Status==JobStatus.Returning&&j.Borrowers==2&&j.Target==j.Requester&&saved.IsJobItem(saved.Capacity-1),"pickup binds the item and sends it straight back to the original owner");
        Speak(saved,resumed,0);
        check(Do(resumed,JobCommandKind.Handoff,id,j.Revision).Result==JobResult.Ok&&saved.Wallet==500&&saved.Town.Jobs.FindJob(id)<0,"two-borrower retrieval still completes and pays its correct reward tier");
    }
    private static void LegacyRetrievalLimit(Action<bool,string> check)
    {
        foreach(int borrowers in new[]{2,3,4})foreach(bool pendingReferral in new[]{false,true}){
            var s=new GameState(24);var service=Service(s,new Draws(1,1,0,0),new Draws());var id=Accept(s,service,0,check);int at=s.Town.Jobs.FindJob(id);var j=s.Town.Jobs.Job(at);
            // Reproduce the existing four-slot save layout, including a saved
            // continuation decision from a build with the former limit.
            for(int i=0;i<borrowers;i++)j.SetBorrower(i,s.Town.Jobs.Npc(i+1));
            j.Borrowers=borrowers;j.Target=j.Borrower(borrowers-1);j.ChainDecision=pendingReferral?1:0;j.NextBorrower=pendingReferral?s.Town.Jobs.Npc(borrowers+1):default;s.Town.Jobs.Jobs[at]=j;
            Array.Fill(s.Pockets,1,0,s.Capacity);
            foreach(var saved in new[]{SaveCodec.Decode(SaveCodec.Encode(s)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(s))}){
                var noReferral=new Draws();var resumed=Service(saved,noReferral,new Draws(75,1000));
                var before=saved.Town.Jobs.Job(saved.Town.Jobs.FindJob(id));Speak(saved,resumed,saved.Town.Jobs.FindNpc(before.Target));
                check(Do(resumed,JobCommandKind.Handoff,id,before.Revision).Result==JobResult.FullPockets,"old chains stop at their current borrower without an extra redirect");
                var stop=saved.Town.Jobs.Job(saved.Town.Jobs.FindJob(id));
                check(stop.Borrowers==borrowers&&stop.Target==before.Target&&stop.ChainDecision==2&&stop.NextBorrower.IsEmpty&&stop.DueAt==before.DueAt&&noReferral.At==0,"legacy stop preserves history and deadline, discards a pending referral and consumes no RNG");
                saved.Pockets[saved.Capacity-1]=0;
                check(Do(resumed,JobCommandKind.Handoff,id,stop.Revision).Result==JobResult.Ok,"legacy chain collects its exact bound parcel when space is available");
                var returning=saved.Town.Jobs.Job(saved.Town.Jobs.FindJob(id));Speak(saved,resumed,0);
                check(Do(resumed,JobCommandKind.Handoff,id,returning.Revision).Result==JobResult.Ok&&saved.Wallet==new[]{0,0,500,750,1000}[borrowers],"legacy chains keep their earned two-, three- or four-borrower reward tier");
            }
        }
    }
    private static void Shared(Action<bool,string> check)
    {
        var s=new GameState(25);s.SelectProfile(1);s.SelectProfile(0);var service=Service(s,new Draws(1,2,0,0),new Draws(60,1000));
        var id=Accept(s,service,0,check);var j=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));check(j.Template==JobTemplate.FavorFruit&&j.Item==Catalog.Pear&&j.Owner.IsEmpty,"fruit offer freezes one exact nonnative fruit in a town-shared record");
        service.EndConversation();s.SelectProfile(1);s.Receive(j.Item);Speak(s,service,0);
        check(Do(service,JobCommandKind.Handoff,id,j.Revision,s.Find(j.Item)).Result==JobResult.Ok&&s.Wallet==500,"another eligible local profile can supply pre-owned fruit and claim its reward");
        j=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));check(j.Claimant==s.Town.Jobs.Player(1)&&j.RetainedUntil==j.OriginalDueAt+3*86400,"shared retention anchors to the original deadline rather than payment time");
        service.EndConversation();s.SelectProfile(0);Speak(s,service,0);check(Do(service,JobCommandKind.Ask).Result==JobResult.Retained&&s.Wallet==0,"another player cannot clear retention or take the winner's payout");
        service.AdvanceTo(j.RetainedUntil);check(s.Town.Jobs.FindJob(id)<0,"completed favor releases its occupancy at retention end");
    }
    private static void Clocks(Action<bool,string> check)
    {
        foreach(var date in new[]{(2000,2,29),(2026,12,25),(2028,2,29),(2099,12,31),(1900,3,1)}){
            long t=JobCalendar.At(date.Item1,date.Item2,date.Item3,23,59,59);JobCalendar.Date(t,out int y,out int m,out int d);check(y==date.Item1&&m==date.Item2&&d==date.Item3,"dated-second calendar round-trips leap and century boundaries");}
        check(!JobCalendar.Season(JobTemplate.FavorSnowman,JobCalendar.At(2026,12,25,7,59,59),true)&&JobCalendar.Season(JobTemplate.FavorSnowman,JobCalendar.At(2026,12,25,8),true)&&!JobCalendar.Season(JobTemplate.FavorSnowman,JobCalendar.At(2026,12,25,17),true),"snowman offer hours use exact second boundaries");
        var s=new GameState(26);var service=Service(s,new Draws(1,0,3,0,0),new Draws());s.Town.Residents[0].Friendship=30;var id=Accept(s,service,0,check);var j=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));
        service.AdvanceTo(j.DueAt);check(s.Town.Jobs.FindJob(id)>=0,"accepted job remains valid exactly at its deadline");
        service.AdvanceTo(j.DueAt+1);check(s.Town.Jobs.FindJob(id)<0&&s.Town.Residents[0].Friendship==25,"timeout happens one second after due and applies one relationship effect");
        service.AdvanceTo(j.DueAt+2);check(s.Town.Residents[0].Friendship==25,"repeated expiry reconciliation cannot repeat a penalty");
        var r=new GameState(27);var rs=Service(r,new Draws(1,0,3,0,0),new Draws());var rid=Accept(r,rs,0,check);var before=r.Town.Jobs.Job(r.Town.Jobs.FindJob(rid));
        rs.AdvanceTo(noon-3600);var after=r.Town.Jobs.Job(r.Town.Jobs.FindJob(rid));check(after.AcceptedAt==before.AcceptedAt-3600&&after.DueAt==before.DueAt-3600,"backwards adjustment shifts live timing anchors together without rerolling");
    }
}
