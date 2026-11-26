using AnimalTown;
using AnimalTown.Online;

static class CardAndProjectionChecks
{
    public static void Run(Action<bool,string> check)
    {
        var original = new MemoryFiles(); var store = new OnlineTravelStore(original);
        string account=Guid.NewGuid().ToString(),reservation=Guid.NewGuid().ToString();
        var reserved=new OnlineTravelRecord("https://town.example","1122334455667788",account,reservation,1,2,OnlineTravelPhase.Reserved);
        var active=new OnlineTravelRecord(reserved.Origin,reserved.Town,account,reservation,1,2,OnlineTravelPhase.Active);
        var first=store.Save(null,reserved);
        for(int cut=0;cut<=OnlineTravelStore.RecordBytes;cut++)
        {
            var files=original.Copy(); files.Cut=cut;
            try{new OnlineTravelStore(files).Save(first,active);}catch(IOException){}
            var recovered=new OnlineTravelStore(files).Load();
            check(recovered!=null&&recovered.Record.Reservation==reservation&&
                recovered.Record.Phase is OnlineTravelPhase.Reserved or OnlineTravelPhase.Active,
                "Every interrupted travel-bank write retains one complete reservation");
        }
        var second=store.Save(first,active);bool stale=false;
        try{store.Save(first,reserved);}catch(IOException){stale=true;}
        check(stale&&store.Load()!.Record.Phase==OnlineTravelPhase.Active,"Stale card metadata cannot release an active reservation");
        var damaged=new MemoryFiles();damaged.Data[OnlineTravelStore.Bank0]=new byte[512];
        bool refused=false;try{new OnlineTravelStore(damaged).Load();}catch(IOException){refused=true;}
        check(refused,"Unreadable recovery records fail closed instead of unlocking the home profile");

        var home=new GameState(59052){Wallet=5432,Name="PrivateSelf",Sequence=3};
        home.ResidentJobs.Enable(0x123456789abcdef,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        home.Profiles[1].Created=true;home.Profiles[1].Wallet=7777;home.Profiles[1].MailCount=1;home.Profiles[1].Mail[0]=18;
        var publicBytes=new byte[OnlineTownProjection.MaximumBytes];int length=OnlineTownProjection.Write(home,publicBytes);
        byte[] before=publicBytes[..length];home.Wallet=9876;home.Savings=123456;home.Debt=100;home.Pockets[0]=18;
        home.Profiles[1].Wallet=9999;home.Profiles[1].Pockets[0]=19;home.Profiles[1].Mail[0]=20;
        length=OnlineTownProjection.Write(home,publicBytes);
        check(before.SequenceEqual(publicBytes[..length]),"Public snapshots are invariant under every other player's private economy/mail edits");
        var view=OnlineTownProjection.Read(publicBytes,length);
        check(view.IsOnlineProjection&&view.Town.Jobs.TownId==home.Town.Jobs.TownId&&view.Town.Tiles.SequenceEqual(home.Town.Tiles)&&
            view.Profiles.All(p=>p.Pockets.All(i=>i==0)&&p.Mail.All(i=>i==0))&&view.Wallet==0,
            "Public projection restores the shared world without private records");
        bool wrongWorld=false;try{new SaveRepository(new MemoryFiles()).Save(view);}catch(InvalidOperationException){wrongWorld=true;}
        check(wrongWorld,"A destination projection cannot be saved over a home card");

        for(int i=0;i<home.Capacity;i++)home.Pockets[i]=10;
        home.ResidentJobs.Enable(home.Town.Jobs.TownId,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var giver=new AnimalTown.Jobs.JobIdentity(0x12223242526272,1);
        var reward=home.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Maple",11,200,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var full=home.ResidentJobs.CollectOnlineReward(reward);
        check(full.Result==AnimalTown.Jobs.JobResult.FullPockets,"Full pockets retain an earned hosted reward in the home ledger");
        var reload=SplitSaveCodec.Decode(SplitSaveCodec.Encode(home));reload.ResidentJobs.Enable(reload.Town.Jobs.TownId,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        reload.Pockets[0]=0;int wallet=reload.Wallet;
        var paid=reload.ResidentJobs.Execute(reload.ResidentJobs.Command(AnimalTown.Jobs.JobCommandKind.RecoverReward,reward));
        check(paid.Result==AnimalTown.Jobs.JobResult.Ok&&reload.Pockets[0]==11&&reload.Wallet==wallet+200,
            "Hosted item and Bells remain recoverable after ordinary home-card reload");
        var repeated=reload.ResidentJobs.Execute(reload.ResidentJobs.Command(AnimalTown.Jobs.JobCommandKind.RecoverReward,reward));
        check(repeated.Result==AnimalTown.Jobs.JobResult.NotAvailable&&reload.Wallet==wallet+200,"A portable reward cannot be collected again after its receipt is retired");
        foreach(AnimalTown.Jobs.JobTemplate template in Enum.GetValues<AnimalTown.Jobs.JobTemplate>())
            check(OnlineJobPolicy.Supported(template)==(template is AnimalTown.Jobs.JobTemplate.DeliveryClothing or AnimalTown.Jobs.JobTemplate.DeliveryProperty or AnimalTown.Jobs.JobTemplate.RetrievalBorrowed or AnimalTown.Jobs.JobTemplate.FavorFruit or AnimalTown.Jobs.JobTemplate.FavorLetter),
                "The PoC job allowlist excludes tools, new gathering and cross-town delivery");
        var onlineHome=new GameState(9006);onlineHome.ResidentJobs.Enable(0x11223344556677,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var offlineCopy=SplitSaveCodec.Decode(SplitSaveCodec.Encode(onlineHome));offlineCopy.ResidentJobs.Enable(offlineCopy.Town.Jobs.TownId,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        onlineHome.ResidentJobs.EnableOnlineAdapter(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        ulong issuer=0x9988776655443322;
        var issued=onlineHome.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Maple",11,0,0,new(issuer,1),new(issuer,2));
        check(issued==new AnimalTown.Jobs.JobIdentity(issuer,2),"Hosted receipts retain their separately persisted issuer identity");
        check(onlineHome.ResidentJobs.DeliverOnlineText(giver,onlineHome.Town.Jobs.Player(0),"Thank you for visiting our town! I hope we can meet again soon.",0,new(issuer,3)),"Hosted mail accepts a separate server-issued message identity");
        var afterOnline=onlineHome.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Maple",12,0,0);
        var fromCard=offlineCopy.ResidentJobs.RecordOnlineReward(giver,giver.Town,"Maple",12,0,0);
        check(afterOnline==fromCard,"Unattended hosted rewards and mail cannot consume the offline card's local identity counter");

        var savedFiles=new MemoryFiles();new SaveRepository(savedFiles).Save(home);
        var baseline=new SaveRepository(savedFiles).Load()!;
        var returned=SplitSaveCodec.Decode(SplitSaveCodec.Encode(baseline));returned.Wallet-=100;returned.Sequence=baseline.Sequence+1;
        byte[][] records=SplitSaveCodec.Encode(returned);byte[] commit=SplitSaveCodec.EncodeCommit(returned,records);
        for(int failure=0;failure<40;failure++)
        {
            var files=savedFiles.Copy();files.FailWrite=failure;
            try{new SaveRepository(files).PublishOnlineCheckpoint(baseline,commit,records);}catch(IOException){}
            files.FailWrite=-1;
            var repository=new SaveRepository(files);var recovered=repository.Load()!;
            check(recovered.Wallet==baseline.Wallet||recovered.Wallet==returned.Wallet,"Interrupted return keeps the old or new complete home economy");
            var published=repository.PublishOnlineCheckpoint(baseline,commit,records);
            check(published.Wallet==returned.Wallet&&published.Sequence==returned.Sequence,"Returning after card publication failure settles exactly once");
            published=repository.PublishOnlineCheckpoint(baseline,commit,records);
            check(published.Wallet==returned.Wallet&&published.Sequence==returned.Sequence,"A lost acknowledgment cannot debit or advance the card again");
        }
    }
    sealed class MemoryFiles:ISaveStorage
    {
        public readonly Dictionary<string,byte[]> Data=[];public int Cut=-1,FailWrite=-1;
        public MemoryFiles Copy(){var copy=new MemoryFiles();foreach(var pair in Data)copy.Data.Add(pair.Key,(byte[])pair.Value.Clone());return copy;}
        public byte[] Read(string name)=>Data.TryGetValue(name,out var bytes)?(byte[])bytes.Clone():throw new FileNotFoundException();
        public void Write(string name,byte[] bytes)
        {
            if(FailWrite==0)throw new IOException("Injected card removal");if(FailWrite>0)FailWrite--;
            Data[name]=(byte[])bytes.Clone();if(Cut>=0){Data[name]=bytes[..Cut];throw new IOException("Injected interrupted write");}
        }
    }
}
