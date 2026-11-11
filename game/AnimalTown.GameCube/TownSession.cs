using System;
using AnimalTown;
using Dolphin.Storage;
using Dolphin.Timing;

// Read the shared save before constructing any game renderer or town assets.
internal sealed class TownSession
{
    internal readonly GameState State;
    internal readonly bool Loaded,Damaged;
    internal readonly string Status;
    private TownSession(GameState state,bool loaded,bool damaged,string status){State=state;Loaded=loaded;Damaged=damaged;Status=status;}
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
