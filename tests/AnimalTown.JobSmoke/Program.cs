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
    private static int Main(){JobMemory.ReclaimTemporaryText=ReclaimText;NativeJobBlocks.Report(0);Console.WriteLine("Native normal jobs");NormalJobs();NativeJobBlocks.Report(1);Console.WriteLine("Native travel");TravelJobs();NativeJobBlocks.Report(2);Console.WriteLine("Native capacity");Capacity();NativeJobBlocks.Report(3);Console.WriteLine("Native full-card travel");FullCardTravel();NativeJobBlocks.Report(4);MaximumEconomy();NativeJobBlocks.Report(5);return 0;}
    private static void ReclaimText()=>NativeJobBlocks.Collect();
    private static void NormalJobs()
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
    }
    private sealed class CardBlocks : Dolphin.Storage.ICardBlockStorage
    {
        private readonly int card;public int Blocks=>1024;
        internal CardBlocks(int card,bool reset){this.card=card;if(reset)NativeJobBlocks.Io(card,0,Blocks,new byte[0]);}
        public void Read(int block,byte[] bytes){if(NativeJobBlocks.Io(card,1,block,bytes)<0)throw new InvalidOperationException("Native card read");}
        public void Write(int block,byte[] bytes){if(NativeJobBlocks.Io(card,2,block,bytes)<0)throw new InvalidOperationException("Native card write");}
    }
    private sealed class Card : ISaveStorage,ISaveSnapshotStorage
    {
        private readonly TownCardRecords records;
        internal Card(int card,bool reset=false){records=new TownCardRecords(new CardBlocks(card,reset),true);}
        public byte[] Read(string name)=>records.Read(name);
        public void Write(string name,byte[] bytes)=>records.Write(name,bytes);
        public bool Matches(string name,byte[]? expected,int sequence,int length,uint checksum,bool ignoreSequence)=>records.Matches(name,expected,sequence,length,checksum,ignoreSequence);
    }
    private static void TravelJobs()
    {
        long now=JobCalendar.At(2026,6,10,12);var a=new GameState(718){Tutorial=7};var b=new GameState(719){Tutorial=7};a.ResidentJobs.Enable(10001,now);b.ResidentJobs.Enable(10002,now);
        var ar=new SaveRepository(new Card(0,true));var br=new SaveRepository(new Card(1,true));ar.Save(a);br.Save(b);var trip=new OfflineTownTravel(ar,br);var guest=trip.Depart(0,now);Require(guest.Visiting,"native separate visitor capsule");guest.Wallet=321;br.Save(guest);var home=trip.Return(now+60);Require(!home.Visiting&&home.Wallet==321&&!home.ProfileLocked(0),"native two-card departure and return");
        trip.ExchangeResidents(1,4,now+120);Require(br.Load()!.Town.Jobs.Species(4)==1,"native actual NPC migration");
    }
    private static void Capacity()
    {
        var s=new GameState(720){Tutorial=7};for(int p=1;p<4;p++)s.SelectProfile(p);s.SelectProfile(0);long now=JobCalendar.At(2026,6,10,12);s.ResidentJobs.Enable(10003,now);
        var chars=new char[2000];for(int i=0;i<1000;i++){chars[i*2]='\ud83d';chars[i*2+1]='\ude42';}string body=new string(chars);
        for(int recipient=0;recipient<4;recipient++)for(int batch=0;batch<2;batch++){
            for(int n=0;n<64;n++){s.Receive(Catalog.WritingPaper);Require(s.ResidentJobs.SendLetter(s.Town.Jobs.Player(recipient),body,s.Find(Catalog.WritingPaper),-1,s.ResidentJobs.NextCommandSequence).Result==JobResult.Ok,"native full mailbox send");}
            now+=86400;s.ResidentJobs.AdvanceTo(now);
        }
        Console.WriteLine("Native mailbox populated");var bytes=SaveCodec.Encode(s);Console.WriteLine("Native mailbox encoded");var copy=SaveCodec.Decode(bytes);Console.WriteLine("Native mailbox decoded");copy.ResidentJobs.Enable(copy.Town.Jobs.TownId,now);Require(copy.MailCount==128&&copy.ResidentJobs.ReadLetter(127)==body,"native maximum Unicode mailbox roundtrip");
    }
    private static void BuildFullCard(int card)
    {
        var state=new GameState(730+card){Tutorial=7};for(int p=1;p<4;p++)state.SelectProfile(p);state.SelectProfile(0);state.Tutorial=7;
        long now=JobCalendar.At(2026,6,10,12);state.ResidentJobs.Enable((ulong)(20000+card),now);var chars=new char[2000];for(int i=0;i<1000;i++){chars[i*2]='\ud83d';chars[i*2+1]=(char)(0xde00+card);}string body=new string(chars);
        for(int p=0;p<4;p++)for(int batch=0;batch<2;batch++){for(int n=0;n<64;n++){state.Receive(Catalog.WritingPaper);Require(state.ResidentJobs.SendLetter(state.Town.Jobs.Player(p),body,state.Find(Catalog.WritingPaper),-1,state.ResidentJobs.NextCommandSequence).Result==JobResult.Ok,"native maximum-card send");}now+=86400;state.ResidentJobs.AdvanceTo(now);}
        new SaveRepository(new Card(card,true)).Save(state);
    }
    private static void FullCardTravel()
    {
        BuildFullCard(0);BuildFullCard(1);var a=new SaveRepository(new Card(0));var b=new SaveRepository(new Card(1));var live=a.Load()!;long now=live.Town.Jobs.Watermark;
        var trip=new OfflineTownTravel(a,b,live);var guest=trip.Depart(0,now);Require(guest.Visiting,"native maximum-mailbox departure");b.Save(guest);var returned=trip.Return(now+60);Require(returned.MailCount==128&&!returned.Visiting,"native maximum-mailbox return preserves all text");
    }
    private static void MaximumEconomy()
    {
        Console.WriteLine("Native simultaneous maximum economy");BuildFullCard(0);var repo=new SaveRepository(new Card(0));var s=repo.Load()!;string body=s.ResidentJobs.ReadLetter(0);JobMaximumFixture.Fill(s,body);repo.Save(s);var copy=repo.Load(s)!;Require(copy.Town.Jobs.Reward(127).State!=JobRewardState.Empty&&copy.Town.Jobs.Job(79).Occupying&&copy.Town.Jobs.Offers[47].Id.IsValid,"native full mail, personal/shared jobs, offers, obligations and outbox coexist");
    }

}

internal static class NativeJobBlocks
{
    internal static int Report(int stage)=>throw new PlatformNotSupportedException("Native test measurement bridge required");
    internal static int Collect()=>throw new PlatformNotSupportedException("Native test collection bridge required");
    internal static int Io(int card,int op,int block,byte[] bytes)=>throw new PlatformNotSupportedException("Native test card bridge required");
}
