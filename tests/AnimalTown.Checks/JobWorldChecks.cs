using AnimalTown;
using AnimalTown.Jobs;

internal static class JobWorldChecks
{
    private sealed class Draws : IJobRandom
    {
        private readonly int[] values;private int at;
        internal Draws(params int[] values){this.values=values;}
        public int NextInt(int maximum){int n=values[at++];if(n>=maximum)throw new Exception("World RNG bound");return n;}
    }
    private static GameState Plain(int seed)
    {var s=new GameState(seed){Tutorial=7};Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);s.Town.GroundCount=0;return s;}
    private static JobIdentity Accept(GameState s,VillagerJobService service)
    {
        s.X=s.Town.Residents[0].PosX;s.Z=s.Town.Residents[0].PosZ;s.Space=0;service.BeginConversation(0,out _);
        var o=service.Execute(service.Command(JobCommandKind.Ask));if(o.Result!=JobResult.ExistingOffer)throw new Exception("World offer not generated");
        var r=service.Execute(service.Command(JobCommandKind.Accept,o.Job,s.Town.Jobs.Offer(0,0).Revision));if(r.Result!=JobResult.Ok)throw new Exception("World offer not accepted");service.EndConversation();return r.Job;
    }
    private static void Push(GameState s,VillagerJobService service,int slot,float dx,float dz)
    {
        var o=s.Town.Jobs.WorldObject(slot);float distance=o.Radius+.32f;s.X=o.X-dx*distance;s.Z=o.Z-dz*distance;s.Space=0;service.TickWorld(s.X-dx*.1f,s.Z-dz*.1f);
    }
    private static void Grow(GameState s,VillagerJobService service,int slot)
    {for(int tick=0;tick<240;tick++){float angle=tick*.17f;Push(s,service,slot,(float)Math.Cos(angle),(float)Math.Sin(angle));}}
    internal static void Run(Action<bool,string> check)
    {
        Ball(check);Snow(check);FlowersAndSupply(check);
        Console.WriteLine("World favors: actual rolling/collision/combine/destruction, saved instance evidence, net flower planting and supply handoffs passed");
    }
    private static void Ball(Action<bool,string> check)
    {
        var s=Plain(71);long now=JobCalendar.At(2026,6,10,12);var service=new VillagerJobService(s,new Draws(1,2,1),new Draws(0,1,0));service.Enable(1111,now);service.TickWorld(s.X,s.Z);var id=Accept(s,service);var ball=s.Town.Jobs.WorldObject(0);s.Town.Residents[0].PosX=ball.X+1.1f;s.Town.Residents[0].PosZ=ball.Z;
        Push(s,service,0,1,0);for(int i=0;i<15;i++)service.TickWorld(s.X,s.Z);
        var job=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));check(job.Status==JobStatus.ReadyToReport&&job.Evidence==ball.Id&&job.Claimant==s.Town.Jobs.Player(0),"physical ball collision records the pushing player's exact evidence");
        service.DestroyWorldObject(0);check(s.Town.Jobs.Job(s.Town.Jobs.FindJob(id)).Status==JobStatus.ReadyToReport,"destroying a ball after contact preserves earned evidence");
        var saved=SaveCodec.Decode(SaveCodec.Encode(s));check(saved.Town.Jobs.Job(saved.Town.Jobs.FindJob(id)).Evidence==ball.Id,"world evidence survives shutdown independently of the destroyed object");
    }
    private static void Snow(Action<bool,string> check)
    {
        var s=Plain(73);long now=JobCalendar.At(2026,12,26,12);var service=new VillagerJobService(s,new Draws(1,2,2),new Draws());service.Enable(2222,now);service.TickWorld(s.X,s.Z);
        int target=-1;for(int i=2;i<6;i++){var o=s.Town.Jobs.WorldObject(i);if(o.X%20>2&&o.X%20<18&&o.Z%20>2&&o.Z%20<18){target=i;break;}}if(target<0)throw new Exception("Snow fixture needs an interior-acre snowball");int moving=target==2?3:2;
        Grow(s,service,target);Grow(s,service,moving);var to=s.Town.Jobs.WorldObject(target);var npc=s.Town.Residents[0];npc.HomeX=(int)to.X/20*20+1;npc.HomeZ=(int)to.Z/20*20+1;var id=Accept(s,service);
        for(int i=0;i<2000;i++){
            var a=s.Town.Jobs.WorldObject(moving);var b=s.Town.Jobs.WorldObject(target);if(a.Kind==JobWorldKind.Snowman||b.Kind==JobWorldKind.Snowman)break;
            float dx=b.X-a.X,dz=b.Z-a.Z,d=(float)Math.Sqrt(dx*dx+dz*dz);if(d<.0001f)break;Push(s,service,moving,dx/d,dz/d);
        }
        var job=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));check(job.Status==JobStatus.ReadyToReport&&job.Evidence.IsValid,"rolling two sufficiently grown physical snowballs builds a new qualifying snowman");
        int snowman=s.Town.Jobs.WorldObject(moving).Kind==JobWorldKind.Snowman?moving:target;var built=s.Town.Jobs.WorldObject(snowman);check(job.Evidence==built.Id&&built.Builder==s.Town.Jobs.Player(0),"snowman instance and builder are the saved physical build result");
        var copy=SaveCodec.Decode(SaveCodec.Encode(s));check(copy.Town.Jobs.WorldObject(snowman).Id==built.Id,"the actual snowman persists with its evidence");
        service.DestroyWorldObject(0);check(s.Town.Jobs.Job(s.Town.Jobs.FindJob(id)).Status==JobStatus.ReadyToReport,"destroying an unrelated object cannot steal the snowman claim");
        service.DestroyWorldObject(snowman);job=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));check(job.Status==JobStatus.Active&&job.Evidence.IsEmpty&&job.Claimant.IsEmpty,"destroying the exact snowman before report clears only that evidence and claimant");
    }
    private static void FlowersAndSupply(Action<bool,string> check)
    {
        long now=JobCalendar.At(2026,6,10,12);var s=Plain(74);var service=new VillagerJobService(s,new Draws(1,2,3),new Draws(0,1,0));service.Enable(3333,now);var id=Accept(s,service);var n=s.Town.Residents[0];int x=n.HomeX/20*20+1,z=n.HomeZ/20*20+1;
        for(int i=0;i<4;i++){s.Receive(2);check(s.Use(s.Find(2),1,x+i,z),"normal planting contributes a real flower to the requesting acre");}
        s.X=n.PosX;s.Z=n.PosZ;service.BeginConversation(0,out _);var j=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));check(service.Execute(service.Command(JobCommandKind.Handoff,id,j.Revision)).Result==JobResult.Ok,"four net flowers qualify through the ordinary planting API");
        foreach(int subtype in new[]{4,5}){
            s=Plain(75+subtype);service=new VillagerJobService(s,new Draws(1,2,subtype),new Draws(0,1,0));service.Enable((ulong)(4400+subtype),now);id=Accept(s,service);n=s.Town.Residents[0];s.X=n.PosX;s.Z=n.PosZ;service.BeginConversation(0,out _);s.Town.Residents[0].Friendship=10;
            check(service.BeginSupplySelection(id),"ordinary fish/insect selector opens in the validated conversation");service.Execute(service.Command(JobCommandKind.SupplyCancel,id));service.Execute(service.Command(JobCommandKind.SupplyCancel,id));check(n.Friendship==9,"cancelling one handoff applies its relationship effect once");
            int item=subtype==4?14:15;s.Receive(item);service.BeginSupplySelection(id);j=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));check(service.Execute(service.Command(JobCommandKind.Handoff,id,j.Revision,s.Find(item))).Result==JobResult.Ok,"an ordinary acquired fish or insect completes its real handoff");
        }
    }
}
