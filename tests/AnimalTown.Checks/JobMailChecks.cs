using AnimalTown;
using AnimalTown.Jobs;

internal static class JobMailChecks
{
    private sealed class Draws : IJobRandom
    {
        private readonly int[] draws;internal int Count;
        internal Draws(params int[] draws){this.draws=draws;}
        public int NextInt(int maximum){if(Count>=draws.Length)throw new Exception("Unexpected mail RNG draw");int n=draws[Count++];if(n<0||n>=maximum)throw new Exception("Mail RNG bound");return n;}
    }
    private static readonly long start=JobCalendar.At(2026,6,10,12);
    private static void Speak(GameState s,VillagerJobService service,int npc)
    {service.EndConversation();var n=s.Town.Residents[npc];s.Space=n.Space;s.X=n.PosX;s.Z=n.PosZ;if(!service.BeginConversation(npc,out _))throw new Exception("Mail conversation fixture");}
    private static JobOutcome Do(VillagerJobService service,JobCommandKind kind,JobIdentity id=default,int revision=-1)=>service.Execute(service.Command(kind,id,revision));
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(31);s.SelectProfile(1);s.SelectProfile(0);var rewards=new Draws(0);var service=new VillagerJobService(s,new Draws(1,2,6),rewards);service.Enable(12345,start);
        Speak(s,service,0);var offered=Do(service,JobCommandKind.Ask);var accepted=Do(service,JobCommandKind.Accept,offered.Job,s.Town.Jobs.Offer(0,0).Revision);
        check(accepted.Result==JobResult.Ok,"letter favor becomes active only after acceptance");
        service.EndConversation();s.SelectProfile(1);s.Receive(Catalog.WritingPaper);s.Receive(1);int paper=s.Find(Catalog.WritingPaper),gift=s.Find(1);ulong sequence=service.NextCommandSequence;
        var sent=service.SendLetter(s.Town.Jobs.Npc(0),"abcde",paper,gift,sequence);
        check(sent.Result==JobResult.Ok&&s.Find(Catalog.WritingPaper)<0&&s.Find(1)<0&&s.Town.Jobs.Job(s.Town.Jobs.FindJob(accepted.Job)).Claimant.IsEmpty,"sending consumes paper/gift once and queues, without claiming the undelivered letter favor");
        check(service.SendLetter(s.Town.Jobs.Npc(0),"abcde",paper,gift,sequence).Job==sent.Job,"same send command replays its stored result without consuming more items");
        for(int i=0;i<128;i++)s.Mail[i]=19;s.MailCount=128;
        service.AdvanceTo(start+3*86400);
        check(s.Town.Jobs.FindJob(accepted.Job)<0&&s.Town.Residents[0].Friendship==6,"offline catch-up credits the pre-deadline letter before releasing overdue favor occupancy");
        var owed=service.BestInteraction(0);int reward=s.Town.Jobs.FindReward(owed.Job);var frozen=s.Town.Jobs.Reward(reward);
        check(frozen.IsReply&&frozen.Owner==s.Town.Jobs.Player(1)&&frozen.ReplyRank==9&&frozen.Item==Catalog.Pear&&rewards.Count==1,"actual first sender owns the frozen reply and nonnative fruit present");
        Speak(s,service,0);check(Do(service,JobCommandKind.LetterFollowup,owed.Job).Result==JobResult.FullMailbox,"full mailbox preserves a collectible reply after its original favor expired");
        var copy=SaveCodec.Decode(SaveCodec.Encode(s));var resumed=new VillagerJobService(copy);resumed.Enable(copy.Town.Jobs.TownId,start+4*86400);Speak(copy,resumed,0);
        check(Do(resumed,JobCommandKind.LetterFollowup,owed.Job).Result==JobResult.FullMailbox,"owed exact reply survives save/load without another reward draw");
        resumed.DiscardLetter(0,false,resumed.NextCommandSequence);
        check(Do(resumed,JobCommandKind.LetterFollowup,owed.Job).Result==JobResult.Ok&&copy.MailCount==128&&copy.Town.Jobs.FindReward(owed.Job)<0,"explicit follow-up inserts one reply immediately into the newly free mailbox slot");
        int letterSlot=127;var envelope=copy.Town.Jobs.Letter(1,letterSlot);check(envelope.Gift==Catalog.Pear&&envelope.Reply&&resumed.ReadLetter(letterSlot).Length>0,"real reply envelope contains text and its frozen present");
        ulong takeSeq=resumed.NextCommandSequence;var taken=resumed.TakeMailPresent(letterSlot,takeSeq);resumed.TakeMailPresent(letterSlot,takeSeq);
        check(taken.Item==Catalog.Pear&&copy.Pockets.Count(x=>x==Catalog.Pear)==1&&copy.Town.Residents[0].Friendship==6,"mail attachment can be taken once, with no second quest friendship bonus");
        service.EndConversation();s.SelectProfile(0);Speak(s,service,0);check(Do(service,JobCommandKind.Collect,owed.Job).Result==JobResult.AlreadyClaimed,"accepting profile cannot steal another sender's reply");
        LocalMail(check);NormalReplies(check);PostBatch(check);
        Console.WriteLine("Real offline mail: queued delivery, first sender, chronology, full mailbox, durable replies, attachments and local-player delivery passed");
    }
    private static void PostBatch(Action<bool,string> check)
    {
        var state=new GameState(931);var service=new VillagerJobService(state,new Draws(),new Draws());service.Enable(931001,start);string body=new string('a',1000);
        for(int i=0;i<64;i++){state.Receive(Catalog.WritingPaper);check(service.SendLetter(state.Town.Jobs.Npc(0),body,state.Find(Catalog.WritingPaper),-1,service.NextCommandSequence).Result==JobResult.Ok,"maximum post batch queues actual full-length letters");}
        long before=GC.GetAllocatedBytesForCurrentThread();service.AdvanceTo(start+86400);long allocated=GC.GetAllocatedBytesForCurrentThread()-before;
        check(allocated==0,"a full scheduled post batch uses its saved grades without allocating or normalizing text again");
    }
    private static void LocalMail(Action<bool,string> check)
    {
        GiftTypes(check);
        var s=new GameState(32);s.SelectProfile(1);s.SelectProfile(0);var service=new VillagerJobService(s);service.Enable(99,start);
        s.Receive(Catalog.WritingPaper);s.Receive(10);var recipient=s.Town.Jobs.Player(1);var sent=service.SendLetter(recipient,"Cafe\u0301!",s.Find(Catalog.WritingPaper),s.Find(10),service.NextCommandSequence);
        check(sent.Result==JobResult.Ok&&s.Find(10)<0,"player-addressed mail takes a normal present into the outbox");
        service.AdvanceTo(start+86400);s.SelectProfile(1);check(s.MailCount==1&&service.ReadLetter(0)=="Caf\u00e9!","another resident receives the actual normalized body");
        check(service.TakeMailPresent(0,service.NextCommandSequence).Item==10&&s.Find(10)>=0,"present ownership transfers to the addressed player");
        var raw=SaveCodec.Encode(s);check(SaveCodec.Encode(SaveCodec.Decode(raw)).SequenceEqual(raw),"packed UTF-8 mail and envelope bindings round-trip exactly");
    }
    private static void GiftTypes(Action<bool,string> check)
    {
        // Exercise every actual gift type through the real outbox/inbox path.
        // In particular, a second stationery sheet is a valid present, while
        // the sheet being written on must never double as its own attachment.
        for(int item=1;item<Catalog.Items.Length;item++){
            if(!Catalog.Has(item,ItemFlags.Gift)||item==JobContent.Letter)continue;
            var s=new GameState(32){Tutorial=7};s.SelectProfile(1);s.SelectProfile(0);
            var service=new VillagerJobService(s);service.Enable(99,start);
            Array.Clear(s.Pockets);s.Pockets[0]=Catalog.WritingPaper;s.Pockets[1]=item;s.Pockets[2]=Catalog.WritingPaper;
            var sent=service.SendLetter(s.Town.Jobs.Player(1),"A little present for you!",2,1,service.NextCommandSequence);
            check(sent.Result==JobResult.Ok&&s.Pockets[0]==Catalog.WritingPaper&&s.Pockets[1]==0&&s.Pockets[2]==0,"sending consumes precisely the chosen paper and gift for item "+item);
            service.AdvanceTo(start+86400);s.SelectProfile(1);
            check(s.MailCount==1&&s.Town.Jobs.Letter(1,0).Gift==item,"every gift category arrives as its actual item, including stationery "+item);
            check(service.TakeMailPresent(0,service.NextCommandSequence).Item==item&&s.Find(item)>=0,"every mail gift can be taken into pockets "+item);
        }
        var same=new GameState(32){Tutorial=7};same.SelectProfile(1);same.SelectProfile(0);
        var post=new VillagerJobService(same);post.Enable(99,start);
        Array.Clear(same.Pockets);same.Pockets[0]=Catalog.PlainWritingPaper;
        var refused=post.SendLetter(same.Town.Jobs.Player(1),"A little present for you!",0,0,post.NextCommandSequence);
        check(refused.Result==JobResult.WrongItem&&same.Pockets[0]==Catalog.PlainWritingPaper&&same.MailCount==0,"one sheet cannot be both the letter and its present; failure keeps the paper");
    }
    private static void NormalReplies(Action<bool,string> check)
    {
        var s=new GameState(33);var service=new VillagerJobService(s,new Draws(),new Draws(0));service.Enable(100,start);s.Receive(Catalog.WritingPaper);
        service.SendLetter(s.Town.Jobs.Npc(0),"abcde",s.Find(Catalog.WritingPaper),-1,service.NextCommandSequence);service.AdvanceTo(start+86400);
        var saved=SaveCodec.Decode(SaveCodec.Encode(s));var resumed=new VillagerJobService(saved);resumed.Enable(saved.Town.Jobs.TownId,start+3*86400);
        check(saved.MailCount==1&&saved.Town.Jobs.Letter(0,0).Reply&&saved.Town.Jobs.Letter(0,0).Gift==4,"ordinary NPC replies arrive after reload even when there are no other live mail envelopes");
    }
}
