using AnimalTown;
using AnimalTown.Online;
using Dolphin.Storage;

internal static class OnlineRecoveryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        MemoryCard.Slots.Clear();var card=new MemoryCard();SaveCards.Initialize();
        var home=new GameState(1838){Tutorial=7};
        var repository=new SaveRepository(new CardSaveStorage(card,new byte[6144],new byte[2048],4));
        repository.Save(home);
        check(!home.Town.Jobs.Initialized,"The ordinary saved-town fixture has not started its job ledger");
        int writes=card.Writes;
        check(OnlineTravelCard.Load(home)==null&&card.Writes==writes,
            "An unlinked saved town needs no online identity or recovery writes before its resident picker");

        home.ResidentJobs.Enable(0x0102030405060708,AnimalTown.Jobs.JobCalendar.At(2027,7,15,12));repository.Save(home);
        const string id="00112233-4455-6677-8899-aabbccddeeff";
        OnlineTravelCard.Save(home,null,new OnlineTravelRecord("https://town.example","0102030405060708",id,id,1,0,OnlineTravelPhase.Reserved));
        writes=card.Writes;
        check(OnlineTravelCard.Load(home)!.Record.Phase==OnlineTravelPhase.Reserved&&card.Writes==writes,
            "A linked card still verifies its exact saved home before returning the recovery journal");
        bool refused=false;try{OnlineTravelCard.Load(new GameState(1839));}catch(InvalidOperationException){refused=true;}
        check(refused&&card.Writes==writes,"A recovery journal cannot bypass home-card revision and identity checks");
        foreach(string name in new[]{OnlineTravelStore.Bank0,OnlineTravelStore.Bank1})if(card.Files.TryGetValue(name,out var bytes))bytes[0]^=1;
        refused=false;try{OnlineTravelCard.Load(home);}catch(IOException){refused=true;}
        check(refused&&card.Writes==writes,"Unreadable recovery banks fail closed and preserve the home card");
        Console.WriteLine("Online recovery: ordinary unlinked saves, exact linked homes, changed cards and damaged banks passed.");
    }
}
