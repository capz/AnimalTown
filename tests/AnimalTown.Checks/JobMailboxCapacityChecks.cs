using AnimalTown;
using AnimalTown.Jobs;

internal static class JobMailboxCapacityChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var pages=new JobMailSlots(576);pages.Set(0,new JobMailRecord{State=JobMailState.Inbox,Gift=4});pages.Set(575,new JobMailRecord{State=JobMailState.Queued});
        var first=pages.Share();var changed=pages[575];changed.Read=true;pages.Set(575,changed);
        check(!first[575].Read&&pages[575].Read,"a shared high-index mail page copies before the original owner changes it");
        var second=first.Share();changed=first[0];changed.Gift=25;first.Set(0,changed);first.Set(575,default);
        check(second[0].Gift==4&&pages[0].Gift==4&&second[575].State==JobMailState.Queued,"nested mail snapshots isolate writes, gifts and final-page removal in either owner");
        var s=new GameState(82){Tutorial=7};for(int p=1;p<4;p++)s.SelectProfile(p);s.SelectProfile(0);s.Tutorial=7;
        long now=JobCalendar.At(2026,6,10,12);s.ResidentJobs.Enable(0x123abcUL,now);
        string body=string.Concat(Enumerable.Repeat("\U0001f642",1000));
        for(int recipient=0;recipient<4;recipient++)for(int batch=0;batch<2;batch++){
            for(int letter=0;letter<64;letter++){
                s.Receive(Catalog.WritingPaper);var outcome=s.ResidentJobs.SendLetter(s.Town.Jobs.Player(recipient),body,s.Find(Catalog.WritingPaper),-1,s.ResidentJobs.NextCommandSequence);
                check(outcome.Result==JobResult.Ok,"the full mailbox bound accepts every 1,000-scalar Unicode letter");
            }
            now+=86400;s.ResidentJobs.AdvanceTo(now);
        }
        for(int p=0;p<4;p++)check((p==s.ActiveProfile?s.MailCount:s.Profiles[p].MailCount)==128,"all four resident mailboxes retain 128 real letters");
        var bytes=SaveCodec.Encode(s);check(bytes.Length<SaveCodec.MaximumBytes&&bytes.Length>512*2632,"all maximum bodies and their envelopes fit the explicit combined-save budget");
        var loaded=SaveCodec.Decode(bytes);loaded.ResidentJobs.Enable(loaded.Town.Jobs.TownId,now);
        for(int p=0;p<4;p++){loaded.SelectProfile(p);check(loaded.ResidentJobs.ReadLetter(127)==body,"full Unicode bodies survive every resident profile and combined readback");}
        var split=SplitSaveCodec.Decode(SplitSaveCodec.Encode(s));split.ResidentJobs.Enable(split.Town.Jobs.TownId,now);check(split.ResidentJobs.ReadLetter(0)==body,"the economic record preserves maximum text without a temporary word-array copy");
        var memoCopy=SplitSaveCodec.Decode(SplitSaveCodec.Encode(s),null,s.Town.Jobs);memoCopy.ResidentJobs.Enable(memoCopy.Town.Jobs.TownId,now);memoCopy.ResidentJobs.ReadLetter(0);
        check(memoCopy.Town.Jobs.Letter(0,0).Read&&!s.Town.Jobs.Letter(0,0).Read,"reading a letter in a decoded metadata snapshot cannot change its source town");
        s.ResidentJobs.ReadLetter(1);check(!memoCopy.Town.Jobs.Letter(0,1).Read,"source-town changes also copy shared metadata before changing a retained snapshot");
        var smaller=SaveCodec.Decode(bytes);smaller.ResidentJobs.Enable(smaller.Town.Jobs.TownId,now);
        check(smaller.ResidentJobs.DiscardLetter(0,false,smaller.ResidentJobs.NextCommandSequence).Result==JobResult.Ok,"a smaller saved mailbox can be prepared through the real discard command");
        var reduced=SplitSaveCodec.Decode(SplitSaveCodec.Encode(smaller),null,s.Town.Jobs);
        check(reduced.MailCount==127&&reduced.Town.Jobs.Letter(0,127).State==JobMailState.Empty&&s.MailCount==128&&s.Town.Jobs.Letter(0,127).State==JobMailState.Inbox,"decoding fewer envelopes clears unseen shared slots without deleting the source's letters");
        JobMaximumFixture.Fill(s,body);var maximum=SaveCodec.Decode(SaveCodec.Encode(s));check(maximum.Town.Jobs.Job(79).Occupying&&maximum.Town.Jobs.Reward(127).State!=JobRewardState.Empty&&maximum.Town.Jobs.Offers[47].Id.IsValid,"full text mail coexists with every resident personal partition, shared favors, 48 offers, 128 obligations and 64 outgoing letters");
        Console.WriteLine("Maximum occupied economy: "+SaveCodec.Encode(maximum).Length+" combined bytes.");
        Console.WriteLine($"Full mail storage: 512 real letters × 1,000 four-byte scalars; {bytes.Length:N0} combined bytes, Unicode tables {JobLetterGrader.UnicodeTableBytes:N0} bytes");
    }
}
