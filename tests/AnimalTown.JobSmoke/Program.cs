using AnimalTown;
using AnimalTown.Jobs;

internal sealed class Draws : IJobRandom
{
    private readonly int[] draws;
    private int at;
    internal Draws(int[] draws){this.draws=draws;}
    public int NextInt(int maximum)
    {if(at>=draws.Length)throw new InvalidOperationException("Unexpected native job RNG draw");int n=draws[at++];if(n<0||n>=maximum)throw new InvalidOperationException("Invalid native job RNG draw");return n;}
}
internal static class Program
{
    private static void Require(bool value,string message){if(!value)throw new InvalidOperationException(message);}
    private static int Main()
    {
        JobContent.Validate();var rng=new JobRandom(653637408);
        Require(rng.NextInt(int.MaxValue)==1012239698,"PPC-compatible unsigned rejection sampling");
        Require(JobSelection.RollMoney(new Draws(new[]{2000}),1000,600)==8360,"64-bit money arithmetic");
        Require(JobLetterGrader.TryGrade("Cafe\u0301!",false,out var grade)&&grade.NormalizedBody=="Caf\u00e9!"&&grade.Rank==3,"native Unicode normalization and grading");
        Require(JobLetterGrader.TryNormalize("\u1100\u1161\u11a8",out string hangul,out int scalars)&&hangul=="\uac01"&&scalars==1,"native algorithmic Hangul normalization");
        var s=new GameState(713);
        var service=new VillagerJobService(s,new Draws(new[]{1,0,3,0,0}),new Draws(new[]{0,1,0,0}));
        long now=JobCalendar.At(2026,6,10,12);service.Enable(0xf0123456789abcdeUL,now);
        s.Space=s.Town.Residents[0].Space;s.X=s.Town.Residents[0].PosX;s.Z=s.Town.Residents[0].PosZ;
        Require(service.BeginConversation(0,out _),"physical conversation starts");
        var offer=service.Execute(service.Command(JobCommandKind.Ask));Require(offer.Result==JobResult.ExistingOffer,"native offer generation");
        var accepted=service.Execute(service.Command(JobCommandKind.Accept,offer.Job,s.Town.Jobs.Offer(0,0).Revision));Require(accepted.Result==JobResult.Ok,"native atomic acceptance");
        int at=s.Town.Jobs.FindJob(accepted.Job);var job=s.Town.Jobs.Job(at);
        Require(job.Binding.IsValid&&s.IsJobItem(3),"native parcel identity");
        var raw=SaveCodec.Encode(s);var restored=SaveCodec.Decode(raw);
        Require(restored.Town.Jobs.TownId==s.Town.Jobs.TownId&&restored.IsJobItem(3),"native private save round-trip");
        var split=SplitSaveCodec.Encode(s);var splitCopy=SplitSaveCodec.Decode(split);
        Require(splitCopy.Town.Jobs.Job(splitCopy.Town.Jobs.FindJob(job.Id)).Target==job.Target,"native economic record extension");
        int target=s.Town.Jobs.FindNpc(job.Target);service.EndConversation();s.Space=s.Town.Residents[target].Space;s.X=s.Town.Residents[target].PosX;s.Z=s.Town.Residents[target].PosZ;
        Require(service.BeginConversation(target,out _),"native target conversation");
        var command=service.Command(JobCommandKind.Handoff,job.Id,job.Revision);var result=service.Execute(command);
        Require(result.Result==JobResult.Ok&&s.Find(10)>=0&&s.Town.Jobs.FindJob(job.Id)<0,"native exact handoff and reward");
        int count=0;for(int i=0;i<s.Capacity;i++)if(s.Pockets[i]==10)count++;
        service.Execute(command);int after=0;for(int i=0;i<s.Capacity;i++)if(s.Pockets[i]==10)after++;
        Require(count==1&&after==1&&s.Town.Residents[target].Friendship==3,"native replay protection");
        s.Receive(Catalog.WritingPaper);var mail=service.SendLetter(s.Town.Jobs.Npc(target),"Cafe\u0301!",s.Find(Catalog.WritingPaper),-1,service.NextCommandSequence);
        Require(mail.Result==JobResult.Ok,"native packed UTF-8 mail creation");
        var queuedCopy=SaveCodec.Decode(SaveCodec.Encode(s));Require(queuedCopy.Town.Jobs.TownId==s.Town.Jobs.TownId,"native queued mail save");
        service.AdvanceTo(now+3*86400);Require(s.MailCount==1&&service.ReadLetter(0).Length>0,"native actual mail delivery and reply");
        var taken=service.TakeMailPresent(0,service.NextCommandSequence);Require(taken.Item==4,"native reply attachment ownership");
        return 0;
    }
}
