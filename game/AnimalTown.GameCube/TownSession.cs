using System;
using System.IO;
using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Storage;
using Dolphin.Timing;

// Read the shared save before constructing any game renderer or town assets.
internal sealed partial class TownSession
{
    internal GameState State {get;private set;}
    internal OnboardingDraft? Draft {get;private set;}
    internal bool PreviewStarted {get;set;}
    internal string OnboardingSaveError {get;private set;}="";
    internal bool Loaded {get;private set;}
    internal OnlineDeviceLink? OnlineCredentials {get;set;}
    internal bool OnlineRecoveryRequired {get;private set;}
    internal int OnlineDeletionProfile=-1;
#if ANIMALTOWN_ONLINE_PLAY
    internal OnlinePlayVisit? Online {get;set;}
    internal static TownSession Review(GameState projection)
    {if(!projection.IsOnlineProjection)throw new InvalidOperationException("Expected a public review scene.");return new TownSession(projection,true,false,"Town review");}
    internal void ShowOnlineWorld(GameState projection)
    {if(!projection.IsOnlineProjection)throw new InvalidOperationException("Expected a public online world.");State=projection;}
#endif
    internal readonly bool Damaged;
    internal string Status {get;private set;}
    private int travelAction,travelProfile,travelFrom,travelTo;private long travelAt;
    internal string TravelError {get;private set;}="";
    internal void QueueTravel(int action,int profile,int from,int to,long now)
    {travelAction=action;travelProfile=profile;travelFrom=from;travelTo=to;travelAt=now;}
    private TownSession(GameState state,bool loaded,bool damaged,string status){State=state;Loaded=loaded;Damaged=damaged;Status=status;}
    internal void RefreshVisitors()
    {
        if(State.IsOnlineProjection)return;
        SavedVisitor[] visitors=new SavedVisitor[0];
        if(SaveCards.Enabled&&Loaded&&!Damaged&&Draft==null&&State.Town.Jobs.Initialized){
            int device=SaveCards.Device==4?5:4;
            try{
                using var card=MemoryCard.Mount((StorageDevice)device,new MemoryCardIdentity("AMTE","01"));
                var storage=new CardSaveStorage(card,new byte[0],new byte[0],device,initializeArchive:false);
                bool deleting=false;
                foreach(string name in storage.Names())if(name==TownDataDeletion.Marker){deleting=true;break;}
                // Let the live roster reject duplicate identities before applying its visitor cap.
                if(!deleting)
                    visitors=new SaveRepository(storage).ReadVisitors(State.Town.Jobs.TownId,Town.LocalResidentCount);
            }catch(Exception error){DebugOverlay.WriteLine("ANIMALTOWN VISITOR CARD: "+error.Message);}
        }
        State.SetTownVisitors(visitors);
    }
    internal void AdoptTravel(GameState state)
    {
        if(state.Town.Jobs.TownId!=State.Town.Jobs.TownId){
            state.Town.Traffic.ResetSnowForTownEntry();
            OnlineCredentials=null;
        }
        State=state;Loaded=true;Status="Trip saved / "+BuildInfo.Version;
    }
    internal void SelectProfile(int profile)
    {
        if(State.ProfileLocked(profile))throw new InvalidOperationException("This villager is away.");
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
        if(!SaveCards.Enabled){
            Draft.BeginWithoutSaving();State=Draft.State;Draft.Dispose();Draft=null;
            Status="Playing without saving";DebugOverlay.WriteLine("ANIMALTOWN ONBOARDING TEMPORARY");return true;
        }
        if(Damaged){OnboardingSaveError="Incomplete save preserved. Back up the card before recovery.";return false;}
        OnboardingSaveError="";string phase="mount";
        try{
            using var card=SaveCards.Mount();
            phase="artwork";
            var store=new CardSaveStorage(card,File.ReadAllBytes(AnimalTown.ContentPaths.Asset("savebanner.rgb5a3")),File.ReadAllBytes(AnimalTown.ContentPaths.Asset("saveicon.rgb5a3")),SaveCards.Device);
            phase="directory";
            bool files=false;var names=store.Names();
            for(int i=0;i<names.Length;i++)if(SplitSaveCodec.IsFile(names[i]))files=true;
            if(TownDataDeletion.Pending(names)){OnboardingSaveError="Finish town deletion in title Options first.";return false;}
            phase="save set";
            Draft.Commit(new SaveRepository(store),files,names);State=Draft.State;Draft.Dispose();Draft=null;Loaded=true;
            try{store.UpdateVillagerFiles(State);}
            catch(Exception error){DebugOverlay.WriteLine("VILLAGER CARD MIGRATION: "+error.Message);}
            store.MigrateSettings();
            Status="Saved / "+BuildInfo.Version;return true;
        }catch(Exception error){
            string detail=error.Message??"Native save error (no message)";
            DebugOverlay.WriteLine("ONBOARDING SAVE ERROR: "+phase+" (code "+NativeCalendar.CardError()+", mount "+NativeCalendar.CardMountError()+", recovery "+NativeCalendar.CardRecoveryError()+"): "+detail);
            OnboardingSaveError=error.Message!=null&&(error.Message.StartsWith("The town on this card")||error.Message.StartsWith("This card already"))?
                "Card changed. Return to the title and reload your town.":
                CardSaveError.Describe(phase,NativeCalendar.CardError(),NativeCalendar.CardMountError(),NativeCalendar.CardRecoveryError(),detail,SaveCards.Slot);
            // A successful commit remains successful if closing the card fails.
            // Never leave a review screen holding an already released draft.
            return Draft==null;
        }
    }
    internal bool DeleteProfile(int profile)
    {
        if(!SaveCards.Enabled||!Loaded||Damaged||!State.Profiles[profile].Created)return false;
        try{
            using var card=SaveCards.Mount();
            var storage=new CardSaveStorage(card,File.ReadAllBytes(AnimalTown.ContentPaths.Asset("savebanner.rgb5a3")),File.ReadAllBytes(AnimalTown.ContentPaths.Asset("saveicon.rgb5a3")),SaveCards.Device);
            bool linked=State.HostedCommerceLocked;
            State=new SaveRepository(storage).DeleteProfile(State,profile);State.HostedCommerceLocked=linked;
            try{storage.UpdateVillagerFiles(State);}
            catch(Exception error){DebugOverlay.WriteLine("VILLAGER CARD CLEANUP: "+error.Message);}
            return true;
        }catch(Exception){return false;}
    }
    internal static TownSession Load()
    {
        GameState? state=null;bool damaged=false,pendingOnline=false;string status=SaveCards.Enabled?"New town - no valid save":"Playing without saving";
        if(SaveCards.Enabled)try{
            using var card=SaveCards.Mount();
            var store=new CardSaveStorage(card,new byte[0],new byte[0],SaveCards.Device);var names=store.Names();bool existing=false,pending=false;
            for(int i=0;i<names.Length;i++){if(SplitSaveCodec.IsFile(names[i]))existing=true;if(names[i]==TownDataDeletion.Marker)pending=true;}
            if(pending){damaged=true;status="Finish town deletion in title Options.";}
            else{
                state=new SaveRepository(store).Load();
                if(state!=null)
                {
                    status="Save loaded";
                    try
                    {
                        var recovery=OnlineTravelCard.ReadMounted(card);
                        state.HostedCommerceLocked=recovery!=null;
                        pendingOnline=recovery!=null&&recovery.Record.Phase!=AnimalTown.Online.OnlineTravelPhase.Ready;
                    }
                    catch(Exception){pendingOnline=true;status="Keep this card. Its online recovery record could not be verified.";}
                }
                else if(existing){damaged=true;status="No complete save set. Backup card before recovery.";}
            }
        }catch(Exception error){DebugOverlay.WriteLine("TOWN LOAD ERROR: "+error.Message);damaged=true;status="Could not read "+SaveCards.Slot+". Return to title to recheck.";}
        bool loaded=state!=null;
        if(state==null){long now=Clock.Timestamp;int date=NativeCalendar.Read();state=new GameState(unchecked((int)(now^(now>>32)^date*7919L)));state.Profiles[0].Created=false;}
        return new TownSession(state,loaded,damaged,status){OnlineRecoveryRequired=pendingOnline};
    }
}
