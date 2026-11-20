using System;
using System.IO;
using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Storage;

internal sealed class OfflineTravelCards : IDisposable
{
    private readonly MemoryCard homeCard,destinationCard;
    private readonly SaveRepository home,destination;
    internal readonly OfflineTownTravel Travel;
    internal readonly int HomeDevice,DestinationDevice;
    internal readonly GameState Home,Destination;
    internal OfflineTravelCards(GameState current,byte[] banner,byte[] icon)
    {
        if(!SaveCards.Enabled)throw new InvalidOperationException("Save your town on a card before travelling.");
        bool currentIsDestination=current.Town.Jobs.Trip.Pending?!current.Town.Jobs.Trip.HomeSide:current.Town.Jobs.NpcMove.Pending&&!current.Town.Jobs.NpcMove.FirstSide;
        HomeDevice=currentIsDestination?(SaveCards.Device==4?5:4):SaveCards.Device;DestinationDevice=HomeDevice==4?5:4;
        if(!SaveCards.Inspect(HomeDevice).CanSave||!SaveCards.Inspect(DestinationDevice).CanSave)throw new InvalidOperationException("Insert two writable town cards, one in each slot.");
        homeCard=MemoryCard.Mount((StorageDevice)HomeDevice,new MemoryCardIdentity("AMTE","01"));
        try{
            destinationCard=MemoryCard.Mount((StorageDevice)DestinationDevice,new MemoryCardIdentity("AMTE","01"));
            home=new SaveRepository(new CardSaveStorage(homeCard,banner,icon,HomeDevice));destination=new SaveRepository(new CardSaveStorage(destinationCard,banner,icon,DestinationDevice));Travel=new OfflineTownTravel(home,destination,current);Travel.Inspect(out Home,out Destination);
            var expected=currentIsDestination?Destination:Home;
            if(expected.Town.Jobs.TownId!=current.Town.Jobs.TownId)throw new InvalidOperationException("The selected town card changed. Return to title and reload.");
        }catch{homeCard.Dispose();destinationCard?.Dispose();throw;}
    }
    internal GameState Execute(int action,int profile,int from,int to,long now)
    {
        if(action==0)return Travel.Depart(profile,now);
        if(action==1)return Travel.Return(now);
        if(action==2)return Travel.Recover(now);
        if(action==3)return Travel.ExchangeResidents(from,to,now,profile==OfflineJobLedger.Visitor);
        return Travel.RecoverResidentMove(now,profile==OfflineJobLedger.Visitor);
    }
    internal void SelectResult(GameState state)
    {int device=state.Town.Jobs.TownId==Home.Town.Jobs.TownId?HomeDevice:DestinationDevice;SaveCards.SelectVerifiedDevice(device);}
    public void Dispose(){destinationCard.Dispose();homeCard.Dispose();}
}
internal sealed class RememberedTownRoutes : IOfflineJobTravel
{
    private readonly OfflineJobLedger ledger;
    internal RememberedTownRoutes(OfflineJobLedger ledger){this.ledger=ledger;}
    public bool Reachable(ulong town,JobIdentity npc)=>town!=ledger.TownId&&ledger.NpcTown(npc)==town;
}
