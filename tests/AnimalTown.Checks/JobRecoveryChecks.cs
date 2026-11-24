using AnimalTown;
using AnimalTown.Jobs;

internal static class JobRecoveryChecks
{
    private sealed class Draws(params int[] values):IJobRandom
    {private int at;public int NextInt(int maximum){int value=values[at++];if((uint)value>=maximum)throw new Exception("Recovery fixture draw");return value;}}
    private sealed class Disk:ISaveStorage
    {internal readonly Dictionary<string,byte[]> Files=new();public byte[] Read(string name)=>Files.TryGetValue(name,out var bytes)?(byte[])bytes.Clone():throw new IOException();public void Write(string name,byte[] bytes)=>Files[name]=(byte[])bytes.Clone();}
    private static long Now=>JobCalendar.At(2026,6,10,12);
    private static void Speak(GameState s,VillagerJobService service,int npc)
    {service.EndConversation();var n=s.Town.Residents[npc];s.Space=n.Space;s.X=n.PosX;s.Z=n.PosZ;if(!service.BeginConversation(npc,out _))throw new Exception("Recovery fixture context");}
    private static JobIdentity Accept(GameState s,VillagerJobService service)
    {Speak(s,service,0);var offer=service.Execute(service.Command(JobCommandKind.Ask));return service.Execute(service.Command(JobCommandKind.Accept,offer.Job,s.Town.Jobs.Offer(0,0).Revision)).Job;}
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(913){Tutorial=7};var service=new VillagerJobService(s,new Draws(1,0,0,0,0),new Draws(40,1000));service.Enable(913001,Now);s.Wallet=99900;
        var id=Accept(s,service);var job=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));int target=s.Town.Jobs.FindNpc(job.Target);Speak(s,service,target);
        check(service.Execute(service.Command(JobCommandKind.Handoff,id,job.Revision)).Amount==99,"recovery starts with an earned partial currency obligation");
        var removed=s.Town.Jobs.Npc(target);check(service.ReplaceResident(target,"New neighbour","Hello",target,JobPersonality.Normal).Result==JobResult.Ok,"explicit permanent NPC replacement succeeds");
        check(s.Town.Jobs.PermanentlyRemoved(removed)&&s.Town.Jobs.Npc(target)!=removed,"replacement is a new identity, with proven removal retained");
        check(service.Execute(service.Command(JobCommandKind.RecoverReward,job.Obligation)).Result==JobResult.InvalidContext,"removed reward recovery requires the town hall");
        s.Space=25;check(service.Execute(service.Command(JobCommandKind.RecoverReward,job.Obligation)).Result==JobResult.NotReady,"a full wallet keeps the exact recovery obligation");
        s.Deposit(1000);var collect=service.Command(JobCommandKind.RecoverReward,job.Obligation);int wallet=s.Wallet;
        check(service.Execute(collect).Amount==101&&s.Wallet==wallet+101,"town hall recovery pays only the frozen currency remainder");service.Execute(collect);check(s.Wallet==wallet+101,"recovery command replay cannot pay twice");
        var loaded=SaveCodec.Decode(SaveCodec.Encode(s));check(loaded.Town.Jobs.PermanentlyRemoved(removed)&&loaded.Town.Jobs.Terminal(0).Reason==JobEndReason.Success,"removal proof and terminal history survive reload");
        s=new GameState(914){Tutorial=7};service=new VillagerJobService(s,new Draws(1,0,3,0,0));service.Enable(914001,Now);id=Accept(s,service);job=s.Town.Jobs.Job(s.Town.Jobs.FindJob(id));target=s.Town.Jobs.FindNpc(job.Target);int friendship=s.Town.Residents[0].Friendship;
        service.ReplaceResident(target,"New neighbour","Hello",target,JobPersonality.Normal);
        check(s.Town.Jobs.FindJob(id)<0&&!Enumerable.Range(0,s.Capacity).Any(s.IsJobItem)&&s.Town.Residents[0].Friendship==friendship,"permanently removed targets cancel and remove exact parcels without blame");
        check(s.Town.Jobs.Terminal(0).Reason==JobEndReason.WorldRemoved,"world cancellation retains its visible reason");
        for(int i=0;i<OfflineJobLedger.RelationsPerPlayer;i++)s.Town.Jobs.Relations[i]=new JobRelation{Npc=new JobIdentity(6000,(ulong)i+1)};
        var oldIdentity=s.Town.Jobs.Npc(0);check(service.ReplaceResident(0,"Blocked","Hello",0,JobPersonality.Normal).Result==JobResult.FullJobs&&s.Town.Jobs.Npc(0)==oldIdentity,"full relationship history rejects replacement before any economic mutation");
        for(int i=0;i<OfflineJobLedger.RelationsPerPlayer;i++)s.Town.Jobs.Relations[i]=default;
        var l=s.Town.Jobs;var reward=new VillagerJobReward{Id=l.Allocate(),Job=l.Allocate(),Owner=l.Player(0),Giver=l.Npc(0),State=JobRewardState.Owed,Item=9999,Category=JobRewardCategory.Clothing,ResolvedAt=Now};l.Rewards[0]=reward;uint rng=s.Town.JobState.Rewards.State;
        loaded=SaveCodec.Decode(SaveCodec.Encode(s));var migrated=loaded.Town.Jobs.Reward(0);
        check(migrated.MigratedFromItem==9999&&migrated.Category==JobRewardCategory.Clothing&&JobContent.Matches(migrated.Item,JobRewardCategory.Clothing)&&loaded.Town.JobState.Rewards.State==rng,"retired reward content migrates once within the captured category without RNG");
        var again=SaveCodec.Decode(SaveCodec.Encode(loaded)).Town.Jobs.Reward(0);check(again.Item==migrated.Item&&again.MigratedFromItem==9999,"content fallback remains frozen after subsequent saves");
        service=loaded.ResidentJobs;service.Enable(loaded.Town.Jobs.TownId,Now);service.ReplaceResident(0,"Departed replacement","Hello",0,JobPersonality.Normal);loaded.Space=25;loaded.MailCount=128;
        check(service.Execute(service.Command(JobCommandKind.RecoverReward,migrated.Id)).Result==JobResult.FullMailbox&&loaded.Town.Jobs.Reward(0).Item==migrated.Item,"a full recovery mailbox preserves the frozen item obligation");
        loaded.MailCount=0;Array.Fill(loaded.Pockets,1);check(service.Execute(service.Command(JobCommandKind.RecoverReward,migrated.Id)).Result==JobResult.Ok&&loaded.MailCount==1&&loaded.Town.Jobs.Letter(0,0).Gift==migrated.Item,"permanently unreachable item rewards become an actual recovery letter even with full pockets");
        check(service.TakeMailPresent(0,service.NextCommandSequence).Result==JobResult.FullPockets,"the recovery present remains attached until a pocket is free");loaded.Pockets[0]=0;check(service.TakeMailPresent(0,service.NextCommandSequence).Item==migrated.Item&&loaded.Town.Jobs.Letter(0,0).Gift==0,"a recovered present can be collected exactly once");
        AddressBooks(check,false);AddressBooks(check,true);RevisionIdentity(check);ReplacementCapacity(check);MovingAddressBook(check);
        Console.WriteLine("Permanent NPC removal, owed-reward recovery, terminal reasons, content migration and travel address-book preflight passed");
    }
    private static void RevisionIdentity(Action<bool,string> check)
    {
        var disk=new Disk();var repo=new SaveRepository(disk);var a=new GameState(917){Tutorial=7};a.ResidentJobs.Enable(917001,Now);repo.Save(a);var expected=repo.Load()!;repo.VerifyRevision(expected);
        var other=new Disk();var b=new GameState(917){Tutorial=7};b.ResidentJobs.Enable(917002,Now);new SaveRepository(other).Save(b);disk.Files.Clear();foreach(var p in other.Files)disk.Files[p.Key]=(byte[])p.Value.Clone();
        bool rejected=false;try{repo.VerifyRevision(expected);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"travel revision checks reject a replacement town with the same seed and sequence but another nonce");
    }
    private static void MovingAddressBook(Action<bool,string> check)
    {
        for(int free=0;free<=2;free++){
            var h=new GameState(919){Tutorial=7};var d=new GameState(920){Tutorial=7};h.ResidentJobs.Enable(919001,Now);d.ResidentJobs.Enable(920001,Now);
            // Every destination neighbour is already known. The departing local
            // neighbour still needs a new entry when their town changes.
            for(int i=0;i<32-free;i++)h.Town.Jobs.RememberNpc(i<6?d.Town.Jobs.Npc(i):new JobIdentity(7001,(ulong)i+1),d.Town.Jobs.TownId,"Known neighbour");
            var a=new SaveRepository(new Disk());var b=new SaveRepository(new Disk());a.Save(h);b.Save(d);bool rejected=false;
            try{new OfflineTownTravel(a,b).ExchangeResidents(0,0,Now);}catch(InvalidOperationException){rejected=true;}
            check(rejected==(free==0),"moving preflights the exact arriving and departing address-book entries");
            check(!a.Load()!.Town.Jobs.NpcMove.Pending,"address-book limits never leave a newly started move locked");
        }
    }
    private static void ReplacementCapacity(Action<bool,string> check)
    {
        var s=new GameState(918){Tutorial=7};s.ResidentJobs.Enable(918001,Now);var l=s.Town.Jobs;
        for(int i=6;i<63;i++)l.Relations[i]=new JobRelation{Npc=new JobIdentity(8000,(ulong)i+1)};
        check(s.ResidentJobs.ReplaceResident(0,"Last new neighbour","Hello",0,JobPersonality.Normal).Result==JobResult.Ok,"replacement reserves the arriving identity's final relationship slot");
        check(SaveCodec.Decode(SaveCodec.Encode(s)).Town.Jobs.Npc(0)==l.Npc(0),"a replacement that fills relationship history remains saveable");
        var old=l.Npc(0);check(s.ResidentJobs.ReplaceResident(0,"Overflow","Hello",0,JobPersonality.Normal).Result==JobResult.FullJobs&&l.Npc(0)==old,"a subsequent replacement refuses before mutating the town");
    }
    private static void AddressBooks(Action<bool,string> check,bool alreadyKnown)
    {
        var h=new GameState(915){Tutorial=7};var d=new GameState(916){Tutorial=7};h.ResidentJobs.Enable(915001,Now);d.ResidentJobs.Enable(916001,Now);
        for(int i=0;i<32;i++)h.Town.Jobs.RememberNpc(alreadyKnown&&i<6?d.Town.Jobs.Npc(i):new JobIdentity(7000,(ulong)i+1),d.Town.Jobs.TownId,"Known neighbour");
        var a=new SaveRepository(new Disk());var b=new SaveRepository(new Disk());a.Save(h);b.Save(d);var trip=new OfflineTownTravel(a,b);bool rejected=false;
        try{var guest=trip.Depart(0,Now);check(alreadyKnown&&guest.Visiting,"a full book permits a previously recorded town without six spare entries");var returned=trip.Return(Now+60);check(!returned.ProfileLocked(0),"full previously known books also permit safe return");}catch(InvalidOperationException){rejected=true;}
        check(rejected!=alreadyKnown,"only genuinely new residents require address-book space");if(rejected)check(!a.Load()!.Town.Jobs.Trip.Pending,"address-book rejection occurs before the home card locks");
    }
}
