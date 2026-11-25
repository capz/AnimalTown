using AnimalTown;
using Dolphin.Storage;

internal static class CardSelectionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        MemoryCard.Slots.Clear();SaveCards.Initialize();
        check(!SaveCards.Enabled&&SaveCards.DefaultChoice(SaveCards.Inspect(4),SaveCards.Inspect(5))==2,"no cards defaults to play without saving");
        check(!AutoSelect(),"no cards opens the chooser");
        check(SaveCards.Inspect(4).Status=="No memory card","a native no-card mount exception has a clear unavailable-row label");
        var b=new MemoryCard(5);SaveCards.Initialize();
        check(SaveCards.Device==5,"a sole Slot B card is selected before loading preferences");
        check(AutoSelect()&&SaveCards.Device==5,"Start skips the chooser for a single usable Slot B card");
        var a=new MemoryCard();SaveCards.Initialize();
        check(SaveCards.Device==4,"two usable cards default to Slot A");
        check(!AutoSelect(),"the chooser stays available for two empty cards");
        a.Capacity=33;SaveCards.Initialize();
        check(SaveCards.Device==5&&!SaveCards.Inspect(4).CanSave,"a full first card falls through to a usable second card");
        a.Capacity=38;check(!SaveCards.Inspect(4).CanSave,"a blank card needs room for the villager and journal as well as the archive");
        a.Capacity=39;check(SaveCards.Inspect(4).CanSave,"39 free blocks are sufficient for a first town");
        a.DirectoryFree=0;check(!SaveCards.Inspect(4).CanSave,"blocks alone are insufficient when all directory entries are used");a.DirectoryFree=127;
        a.MountError=-6;check(!SaveCards.Inspect(4).CanSave&&a.Writes==0,"damaged cards are never formatted or written by a probe");a.MountError=0;
        a.Capacity=80;
        var banner=new byte[6144];var icon=new byte[2048];
        SaveCards.Select(SaveCards.Inspect(5));
        check(UserOptionsStorage.Save(new UserOptions())&&DevConfigStorage.Save(new DevConfig())&&b.Files.Count==2&&b.Used==2,"settings each create a one-block entry on an empty card");
        check(!AutoSelect(),"settings files do not count as a town save");
        using(var draft=Draft("Aster"))draft.Commit(new SaveRepository(new CardSaveStorage(a,banner,icon,4)),false,new string[0]);
        check(AutoSelect()&&SaveCards.Device==4,"a town archive wins over a settings-only card");
        using(var draft=Draft("Birch"))draft.Commit(new SaveRepository(new CardSaveStorage(b,banner,icon,5)),false,new string[0]);
        check(!AutoSelect(),"two towns open the chooser");
        a.Capacity=a.Used;a.DirectoryFree=a.Files.Count;
        int writesA=a.Writes,writesB=b.Writes,mounts=MemoryCard.Mounts;
        check(SaveCards.Inspect(4).CanSave,"an existing current archive needs no free blocks or directory entries to be opened for reading and deletion");
        check(a.Writes==writesA&&b.Writes==writesB&&MemoryCard.Mounts==mounts,"card discovery is read-only and never mounts the game archive for journal recovery");
        a.GameCode="OTHER";check(!SaveCards.Inspect(4).CanSave,"a same-named archive owned by another game does not count as reserved space");a.GameCode="AMTE";
        check(SaveCards.Select(SaveCards.Inspect(5))&&SaveCards.Device==5,"an explicit Slot B selection is remembered");
        check(SaveCards.DefaultChoice(SaveCards.Inspect(4),SaveCards.Inspect(5))==1,"reopening the chooser retains the player's usable selection");
        check(!SaveCards.Select(new SaveCardInfo(4,"No card"))&&SaveCards.Device==5,"an unusable choice cannot replace the selected slot");
        var session=TownSession.Load();check(session.Loaded&&session.State.Name=="Birch","town loading reads the selected Slot B archive");
        var originalDebug=b.Files[CardSettingsStorage.DebugFile].ToArray();
        var options=new UserOptions{Widescreen=true};
        check(UserOptionsStorage.Save(options)&&UserOptionsStorage.Load().Widescreen&&UserOptionsStorage.Status.Contains("Slot B"),"video options write and reload on Slot B");
        check(b.Files[CardSettingsStorage.DebugFile].SequenceEqual(originalDebug),"video save leaves the dedicated debug file byte-identical");
        var originalVideo=b.Files[CardSettingsStorage.VideoFile].ToArray();
        var config=new DevConfig();config.Values[DevConfig.GroundMipBandsIndex]=1;
        check(DevConfigStorage.Save(config)&&DevConfigStorage.Load().Values[DevConfig.GroundMipBandsIndex]==1,"debug settings write and reload on Slot B");
        check(b.Files[CardSettingsStorage.VideoFile].SequenceEqual(originalVideo),"debug save leaves the dedicated video file byte-identical");
        session.State.Wallet++;new SaveRepository(new CardSaveStorage(b,banner,icon,SaveCards.Device)).Save(session.State);
        check(TownSession.Load().State.Wallet==session.State.Wallet,"subsequent town saves publish to Slot B");
        check(a.Writes==writesA&&new SaveRepository(new CardSaveStorage(a,banner,icon,4)).Load()!.Name=="Aster","Slot B writes leave Slot A town and settings untouched");
        check(TownDataStorage.Delete()&&!TownSession.Load().Loaded&&a.Writes==writesA,"title town deletion uses Slot B without touching Slot A");
        check(UserOptionsStorage.Load().Widescreen&&DevConfigStorage.Load().Values[DevConfig.GroundMipBandsIndex]==1,"selected-slot town deletion retains both dedicated settings files");
        check(AutoSelect()&&SaveCards.Device==4,"one town wins even after Slot B was explicitly selected");
        SaveCards.Select(SaveCards.Inspect(5));
        MemoryCard.Slots.Remove(5);bool removed=false;
        try{using var missing=SaveCards.Mount();}catch(IOException){removed=true;}
        check(removed&&SaveCards.Device==5&&a.Writes==writesA,"removing Slot B never redirects a save to Slot A");
        MemoryCard.Slots[5]=b;SaveCards.WithoutSaving();writesB=b.Writes;mounts=MemoryCard.Mounts;
        check(!UserOptionsStorage.Save(options)&&!DevConfigStorage.Save(config)&&!TownDataStorage.Delete()&&!TownDataStorage.Pending(),"temporary play blocks preference writes and destructive card operations");
        session=TownSession.Load();session.SelectProfile(0);var temporary=session.Draft!;
        temporary.SetName("Clover");PlaceHome(temporary);
        check(session.SaveOnboarding()&&session.Draft==null&&!session.Loaded&&session.State.Name=="Clover"&&session.State.Sequence==0,"onboarding finishes in memory without creating a save generation");
        check(a.Writes==writesA&&b.Writes==writesB&&MemoryCard.Mounts==mounts,"temporary onboarding and settings never mount or write either card");
        check(SaveCards.DefaultChoice(SaveCards.Inspect(4),SaveCards.Inspect(5))==2,"rechecking cards retains an explicit choice to play without saving");
        SaveCards.Initialize();check(SaveCards.Device==4&&!SaveCards.Chosen,"a fresh boot returns to the first usable card");
        check(CardSaveError.Describe("mount",0,-3,0,"","Slot B").Contains("Slot B"),"save failure instructions name the selected slot");
        var small=new MemoryCard();small.CreateRecords("AnimalTown",16,1234,new byte[16384]);small.Capacity=small.Used+33;
        check(!SaveCards.Inspect(4).CanSave,"a smaller legacy archive still needs 34 blocks for safe expansion");
        var empty=new MemoryCard();
        var legacy=new MemoryCard(5);
        legacy.WriteSave("AnimalTown0",SaveCodec.Encode(new GameState(719)),new MemoryCardSaveOptions());
        check(AutoSelect()&&SaveCards.Device==5&&empty.Writes==0,"a legacy town also takes priority over an empty card");
        Console.WriteLine("Card selection: A/B discovery and capacity, retained choice, Slot B town/settings/deletion, removal without fallback and temporary onboarding passed.");
    }
    private static bool AutoSelect()=>SaveCards.TryAutoSelect(SaveCards.Inspect(4),SaveCards.Inspect(5));
    private static OnboardingDraft Draft(string name)
    {
        var source=new GameState(7319);source.Profiles[0].Created=false;
        var draft=new OnboardingDraft(source,0,false);draft.SetName(name);PlaceHome(draft);return draft;
    }
    private static void PlaceHome(OnboardingDraft draft)
    {
        for(int z=1;z<Town.Height-13;z++)for(int x=1;x<Town.Width-15;x++)if(draft.PlaceHome(x,z))return;
        throw new Exception("Card selection fixture needs a home plot");
    }
}
