using System;
using System.IO;
using AnimalTown;
using Dolphin.Storage;
using Dolphin.Timing;

// Read the shared save before constructing any game renderer or town assets.
internal sealed class TownSession
{
    internal GameState State {get;private set;}
    internal OnboardingDraft? Draft {get;private set;}
    internal bool PreviewStarted {get;set;}
    internal string OnboardingSaveError {get;private set;}="";
    internal bool Loaded {get;private set;}
    internal readonly bool Damaged;
    internal string Status {get;private set;}
    private TownSession(GameState state,bool loaded,bool damaged,string status){State=state;Loaded=loaded;Damaged=damaged;Status=status;}
    internal void SelectProfile(int profile)
    {
        if(!State.Profiles[profile].Created||!State.HomePlaced(profile)){
            Draft=new OnboardingDraft(State,profile,Loaded);State=Draft.State;
        }else State.SelectProfile(profile);
    }
    internal void DiscardDraft()
    {
        if(Draft==null)return;
        State=Draft.Restore();Draft.Dispose();Draft=null;
    }
    internal bool SaveOnboarding()
    {
        if(Draft==null)return false;
        if(Damaged){OnboardingSaveError="Incomplete save preserved. Back up the card before recovery.";return false;}
        try{
            using var card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("AMTE","01"));
            bool files=false;var entries=card.GetEntries();var names=new string[entries.Length];
            for(int i=0;i<entries.Length;i++){names[i]=entries[i].Name;if(SplitSaveCodec.IsFile(names[i]))files=true;}
            var store=new CardSaveStorage(card,File.ReadAllBytes("dvd:/savebanner.rgb5a3"),File.ReadAllBytes("dvd:/saveicon.rgb5a3"));
            Draft.Commit(new SaveRepository(store),files,names);State=Draft.State;Draft.Dispose();Draft=null;Loaded=true;Status="Saved / "+BuildInfo.Version;return true;
        }catch(Exception error){
            OnboardingSaveError=error.Message!=null&&(error.Message.StartsWith("The town on this card")||error.Message.StartsWith("This card already"))?
                "Card changed. Return to the title and reload your town.":"Couldn't save. Check Slot A and try again.";
            // A successful commit remains successful if closing the card fails.
            // Never leave a review screen holding an already released draft.
            return Draft==null;
        }
    }
    internal bool DeleteProfile(int profile)
    {
        if(!Loaded||Damaged||!State.Profiles[profile].Created)return false;
        try{
            using var card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("AMTE","01"));
            var storage=new CardSaveStorage(card,File.ReadAllBytes("dvd:/savebanner.rgb5a3"),File.ReadAllBytes("dvd:/saveicon.rgb5a3"));
            State=new SaveRepository(storage).DeleteProfile(State,profile);return true;
        }catch(Exception){return false;}
    }
    internal static TownSession Load()
    {
        GameState? state=null;bool damaged=false;string status="New town - no valid save";
        try{
            using var card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("AMTE","01"));
            var entries=card.GetEntries();bool existing=false;for(int i=0;i<entries.Length;i++)if(SplitSaveCodec.IsFile(entries[i].Name))existing=true;
            state=new SaveRepository(new CardSaveStorage(card,new byte[0],new byte[0])).Load();
            if(state!=null)status="Save loaded";else if(existing){damaged=true;status="No complete save set. Backup card before recovery.";}
        }catch(Exception){status="Insert memory card in Slot A to save";}
        bool loaded=state!=null;
        if(state==null){long now=Clock.Timestamp;int date=NativeCalendar.Read();state=new GameState(unchecked((int)(now^(now>>32)^date*7919L)));state.Profiles[0].Created=false;}
        return new TownSession(state,loaded,damaged,status);
    }
}
