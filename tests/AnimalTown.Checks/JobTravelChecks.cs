using AnimalTown;
using AnimalTown.Jobs;

internal static class JobTravelChecks
{
    private sealed class Disk : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal Action? BeforeWrite,AfterWrite;
        public byte[] Read(string name)=>Files.TryGetValue(name,out var b)?(byte[])b.Clone():throw new IOException("Missing record");
        public void Write(string name,byte[] bytes){BeforeWrite?.Invoke();Files[name]=(byte[])bytes.Clone();AfterWrite?.Invoke();}
        internal Disk Copy(){var d=new Disk();foreach(var p in Files)d.Files[p.Key]=(byte[])p.Value.Clone();return d;}
    }
    private sealed class Draws : IJobRandom
    {
        private readonly int[] values;private int at;
        internal Draws(params int[] values){this.values=values;}
        public int NextInt(int maximum){int n=values[at++];if(n>=maximum)throw new Exception("Travel RNG bound");return n;}
    }
    private static long now=>JobCalendar.At(2026,6,10,12);
    private static void Speak(GameState s,VillagerJobService service,int npc)
    {service.EndConversation();var v=s.Town.Residents[npc];s.Space=v.Space;s.X=v.PosX;s.Z=v.PosZ;if(!service.BeginConversation(npc,out _))throw new Exception("Travel conversation fixture");}
    private static (Disk,Disk) Cards()
    {
        var home=new GameState(61);var dest=new GameState(62);for(int i=1;i<4;i++){home.SelectProfile(i);dest.SelectProfile(i);home.Wallet=100+i;dest.Wallet=200+i;}
        home.SelectProfile(0);dest.SelectProfile(0);home.Tutorial=7;dest.Tutorial=7;for(int p=1;p<4;p++){home.Profiles[p].Tutorial=7;dest.Profiles[p].Tutorial=7;}home.Name="Traveller";home.Wallet=314;home.ResidentJobs.Enable(123456,now);dest.ResidentJobs.Enable(654321,now);var a=new Disk();var b=new Disk();new SaveRepository(a).Save(home);new SaveRepository(b).Save(dest);return(a,b);
    }
    internal static void Run(Action<bool,string> check)
    {
        var (a,b)=Cards();var first=new SaveRepository(a);var second=new SaveRepository(b);var travel=new OfflineTownTravel(first,second);var original=second.Load()!;
        var guest=travel.Depart(0,now);check(guest.Visiting&&guest.Name=="Traveller"&&guest.Wallet==314,"a visitor uses a separate fifth ownership capsule");
        for(int i=0;i<4;i++)check(guest.Profiles[i].Name==original.Profiles[i].Name&&guest.Profiles[i].Wallet==original.Profiles[i].Wallet,"visiting preserves every host resident profile");
        check(first.Load()!.ProfileLocked(0)&&!first.Load()!.ProfileLocked(1),"only the source traveller is locked while away");
        bool blocked=false;try{first.Load()!.SelectProfile(0);}catch(InvalidOperationException){blocked=true;}check(blocked,"the away copy cannot be selected");
        guest.Wallet+=77;guest.Receive(Catalog.Pear);second.Save(guest);var reloaded=second.Load()!;check(reloaded.Visiting&&reloaded.Wallet==391&&reloaded.Find(Catalog.Pear)>=0,"guest state survives native-format split and combined readback");
        var home=travel.Return(now+60);check(!home.Visiting&&home.Wallet==391&&home.Find(Catalog.Pear)>=0&&!home.ProfileLocked(0),"return brings home exactly the active visitor's belongings");
        check(!second.Load()!.Visiting&&second.Load()!.Profiles[0].Wallet==original.Profiles[0].Wallet,"return removes the guest without replacing a host inventory");
        ForeignDelivery(a,b,check);
        NpcMoving(check);
        Interrupted(check);
        Console.WriteLine("Offline travel: dedicated guest, four preserved residents, portable jobs/parcels, return and all interrupted handoff writes passed");
    }
    private static void NpcMoving(Action<bool,string> check)
    {
        var (a,b)=Cards();var first=new SaveRepository(a);var second=new SaveRepository(b);var h=first.Load()!;var d=second.Load()!;var npc=h.Town.Jobs.Npc(1);string name=h.Town.Residents[1].Name;int personality=(int)h.Town.JobState.Resident(1).Personality;
        var move=new OfflineTownTravel(first,second);h=move.ExchangeResidents(1,4,now);
        check(h.Town.Jobs.FindNpc(npc)<0&&h.Town.Jobs.LastMovedNpc==npc,"a real move records the departed NPC identity");
        d=second.Load()!;check(d.Town.Jobs.FindNpc(npc)==4&&d.Town.Residents[4].Name==name&&d.Town.Jobs.Species(4)==1&&(int)d.Town.JobState.Resident(4).Personality==personality,"NPC identity, name, species and personality survive a different roster slot");
        var travel=new OfflineTownTravel(first,second);travel.Inspect(out h,out _);var service=new VillagerJobService(h,new Draws(1,0,2,0),new Draws());service.Enable(h.Town.Jobs.TownId,now);service.Travel=travel;Speak(h,service,0);
        var offer=service.Execute(service.Command(JobCommandKind.Ask));check(offer.Result==JobResult.ExistingOffer&&h.Town.Jobs.Offer(0,0).Target==npc,"moved delivery targets the actual departed neighbour without an invented move-age limit");
        var accept=service.Execute(service.Command(JobCommandKind.Accept,offer.Job,h.Town.Jobs.Offer(0,0).Revision));first.Save(h);
        var guest=travel.Depart(0,now+30);int slot=guest.Town.Jobs.FindJob(accept.Job);var job=guest.Town.Jobs.Job(slot);Speak(guest,guest.ResidentJobs,4);
        check(guest.ResidentJobs.Execute(guest.ResidentJobs.Command(JobCommandKind.Handoff,job.Id,job.Revision)).Result==JobResult.Ok,"the moved NPC receives their exact parcel in their new town");
        // Cut every migration publication before and after its durable write.
        var (baseA,baseB)=Cards();var ca=baseA.Copy();var cb=baseB.Copy();int writes=0;ca.BeforeWrite=cb.BeforeWrite=()=>writes++;new OfflineTownTravel(new SaveRepository(ca),new SaveRepository(cb)).ExchangeResidents(1,4,now);
        for(int after=0;after<2;after++)for(int cut=1;cut<=writes;cut++){
            var x=baseA.Copy();var y=baseB.Copy();int at=0;Action fail=()=>{if(++at==cut)throw new IOException("Power cut");};if(after==0)x.BeforeWrite=y.BeforeWrite=fail;else x.AfterWrite=y.AfterWrite=fail;
            try{new OfflineTownTravel(new SaveRepository(x),new SaveRepository(y)).ExchangeResidents(1,4,now);}catch(IOException){}
            x.BeforeWrite=y.BeforeWrite=x.AfterWrite=y.AfterWrite=null;var xr=new SaveRepository(x);var yr=new SaveRepository(y);var state=xr.Load()!;
            if(state.Town.Jobs.NpcMove.Pending||yr.Load()!.Town.Jobs.NpcMove.Pending)new OfflineTownTravel(xr,yr).RecoverResidentMove(now);
            var xx=xr.Load()!;var yy=yr.Load()!;check((xx.Town.Jobs.FindNpc(npc)>=0?1:0)+(yy.Town.Jobs.FindNpc(npc)>=0?1:0)==1,"recovered NPC moves preserve one roster identity");
        }
    }
    private static void ForeignDelivery(Disk a,Disk b,Action<bool,string> check)
    {
        var first=new SaveRepository(a);var second=new SaveRepository(b);var travel=new OfflineTownTravel(first,second);travel.Inspect(out var home,out _);
        var service=new VillagerJobService(home,new Draws(1,0,1,0),new Draws(0,1,0));service.Enable(home.Town.Jobs.TownId,now+60);service.Travel=travel;Speak(home,service,0);
        var offer=service.Execute(service.Command(JobCommandKind.Ask));check(offer.Result==JobResult.ExistingOffer,"an actual previous visit unlocks foreign delivery");
        var accepted=service.Execute(service.Command(JobCommandKind.Accept,offer.Job,home.Town.Jobs.Offer(0,0).Revision));check(accepted.Result==JobResult.Ok,"foreign offer reserves its exact parcel");first.Save(home);
        var guest=travel.Depart(0,now+120);int slot=guest.Town.Jobs.FindJob(accepted.Job);check(slot>=0&&guest.Town.Jobs.Job(slot).Owner==home.Town.Jobs.Player(0),"personal jobs retain owner and identity through travel");
        var job=guest.Town.Jobs.Job(slot);int npc=guest.Town.Jobs.FindNpc(job.Target);Speak(guest,guest.ResidentJobs,npc);var result=guest.ResidentJobs.Execute(guest.ResidentJobs.Command(JobCommandKind.Handoff,job.Id,job.Revision));
        check(result.Result==JobResult.Ok&&guest.Town.Jobs.FindJob(job.Id)<0,"the real remembered foreign NPC accepts the travelling parcel and pays");second.Save(guest);var returned=travel.Return(now+180);
        check(returned.Town.Jobs.FindJob(job.Id)<0&&!Enumerable.Range(0,returned.Capacity).Any(returned.IsJobItem),"the consumed parcel and completed job do not reappear at home");
    }
    private static void Interrupted(Action<bool,string> check)
    {
        var (baseA,baseB)=Cards();int writes=0;var countA=baseA.Copy();var countB=baseB.Copy();countA.BeforeWrite=countB.BeforeWrite=()=>writes++;
        new OfflineTownTravel(new SaveRepository(countA),new SaveRepository(countB)).Depart(0,now);int departureWrites=writes;
        for(int after=0;after<2;after++)for(int cut=1;cut<=departureWrites;cut++){
            var a=baseA.Copy();var b=baseB.Copy();int at=0;Action fail=()=>{if(++at==cut)throw new IOException("Power cut");};if(after==0)a.BeforeWrite=b.BeforeWrite=fail;else a.AfterWrite=b.AfterWrite=fail;
            try{new OfflineTownTravel(new SaveRepository(a),new SaveRepository(b)).Depart(0,now);}catch(IOException){}
            a.BeforeWrite=b.BeforeWrite=a.AfterWrite=b.AfterWrite=null;var first=new SaveRepository(a);var second=new SaveRepository(b);var h=first.Load()!;var d=second.Load()!;
            check((h.ProfileLocked(0)?0:1)+(d.Visiting?1:0)<=1,"power cut never activates two copies of a traveller");
            if(h.Town.Jobs.Trip.Pending){var guest=new OfflineTownTravel(first,second).Recover(now);check(guest.Visiting&&first.Load()!.ProfileLocked(0),"matching cards replay an interrupted departure");}
        }
        // Start from a committed visit and cut every return write on either card.
        var visitA=baseA.Copy();var visitB=baseB.Copy();new OfflineTownTravel(new SaveRepository(visitA),new SaveRepository(visitB)).Depart(0,now);
        writes=0;countA=visitA.Copy();countB=visitB.Copy();countA.BeforeWrite=countB.BeforeWrite=()=>writes++;new OfflineTownTravel(new SaveRepository(countA),new SaveRepository(countB)).Return(now+60);int returnWrites=writes;
        for(int after=0;after<2;after++)for(int cut=1;cut<=returnWrites;cut++){
            var a=visitA.Copy();var b=visitB.Copy();int at=0;Action fail=()=>{if(++at==cut)throw new IOException("Power cut");};if(after==0)a.BeforeWrite=b.BeforeWrite=fail;else a.AfterWrite=b.AfterWrite=fail;
            try{new OfflineTownTravel(new SaveRepository(a),new SaveRepository(b)).Return(now+60);}catch(IOException){}
            a.BeforeWrite=b.BeforeWrite=a.AfterWrite=b.AfterWrite=null;var first=new SaveRepository(a);var second=new SaveRepository(b);var h=first.Load()!;var d=second.Load()!;
            check((h.ProfileLocked(0)?0:1)+(d.Visiting?1:0)<=1,"return power cut never unlocks home alongside an active visitor");
            if(h.Town.Jobs.Trip.Pending||d.Town.Jobs.Trip.Pending){var recovered=new OfflineTownTravel(first,second).Recover(now+60);check(recovered.Visiting||!recovered.ProfileLocked(0),"return receipts remain recoverable after every write boundary");}
        }
        Console.WriteLine($"Travel fault injection: {departureWrites} departure + {returnWrites} return physical writes, cut before and after each");
    }
}
