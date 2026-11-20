using AnimalTown;
using AnimalTown.Jobs;

internal static class JobMailboxCapacityChecks
{
    internal static void Run(Action<bool,string> check)
    {
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
        var bytes=SaveCodec.Encode(s);check(bytes.Length<SaveCodec.MaximumBytes&&bytes.Length>2000000,"worst-case packed Unicode mail fits the explicit combined-save budget");
        var loaded=SaveCodec.Decode(bytes);loaded.ResidentJobs.Enable(loaded.Town.Jobs.TownId,now);
        for(int p=0;p<4;p++){loaded.SelectProfile(p);check(loaded.ResidentJobs.ReadLetter(127)==body,"full Unicode bodies survive every resident profile and combined readback");}
        var split=SplitSaveCodec.Decode(SplitSaveCodec.Encode(s));split.ResidentJobs.Enable(split.Town.Jobs.TownId,now);check(split.ResidentJobs.ReadLetter(0)==body,"the economic record preserves maximum text without a temporary word-array copy");
        Console.WriteLine($"Full mail storage: 512 real letters × 1,000 four-byte scalars; {bytes.Length:N0} combined bytes, Unicode tables {JobLetterGrader.UnicodeTableBytes:N0} bytes");
    }
}
