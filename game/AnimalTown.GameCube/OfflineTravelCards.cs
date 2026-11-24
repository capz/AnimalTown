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
    private static void Devices(GameState current,out int homeDevice,out int destinationDevice)
    {
        if(!SaveCards.Enabled)throw new InvalidOperationException("Save your town on a card before travelling.");
        bool currentIsDestination=current.Town.Jobs.Trip.Pending?!current.Town.Jobs.Trip.HomeSide:current.Town.Jobs.NpcMove.Pending&&!current.Town.Jobs.NpcMove.FirstSide;
        homeDevice=currentIsDestination?(SaveCards.Device==4?5:4):SaveCards.Device;destinationDevice=homeDevice==4?5:4;
        if(!SaveCards.Inspect(homeDevice).CanSave||!SaveCards.Inspect(destinationDevice).CanSave)throw new InvalidOperationException("Insert two writable town cards, one in each slot.");
    }
    internal static OfflineTravelPreview Preview(GameState current,byte[] banner,byte[] icon)
    {
        Devices(current,out int first,out int second);
        using var a=MemoryCard.Mount((StorageDevice)first,new MemoryCardIdentity("AMTE","01"));
        using var b=MemoryCard.Mount((StorageDevice)second,new MemoryCardIdentity("AMTE","01"));
        var h=new SaveRepository(new CardSaveStorage(a,banner,icon,first,current)).ReadTravelPreview();
        var d=new SaveRepository(new CardSaveStorage(b,banner,icon,second,current)).ReadTravelPreview();
        if(h.TownId==d.TownId)throw new InvalidOperationException("These saves are copies of the same town");
        if(current.Town.Jobs.TownId!=(SaveCards.Device==first?h.TownId:d.TownId))throw new InvalidOperationException("The selected town card changed. Return to title and reload.");
        var trip=current.Town.Jobs.Trip;if(trip.Pending&&(trip.Home!=h.TownId||trip.Destination!=d.TownId))throw new InvalidOperationException("These are not the two cards for this trip");
        return new OfflineTravelPreview(first,second,h,d);
    }
    internal OfflineTravelCards(GameState current,byte[] banner,byte[] icon)
    {
        Devices(current,out HomeDevice,out DestinationDevice);
        bool currentIsDestination=SaveCards.Device==DestinationDevice;
        homeCard=MemoryCard.Mount((StorageDevice)HomeDevice,new MemoryCardIdentity("AMTE","01"));
        try{
            destinationCard=MemoryCard.Mount((StorageDevice)DestinationDevice,new MemoryCardIdentity("AMTE","01"));
            var homeStore=new CardSaveStorage(homeCard,banner,icon,HomeDevice,current,preserveVillagerLayout:true);
            var destinationStore=new CardSaveStorage(destinationCard,banner,icon,DestinationDevice,current,preserveVillagerLayout:true);
            home=new SaveRepository(homeStore);destination=new SaveRepository(destinationStore);Travel=new OfflineTownTravel(home,destination,current);Travel.Inspect(out Home,out Destination);
            // Conversion validates each legacy town again. Reuse that town's
            // immutable bodies rather than decoding the other mailbox twice.
            homeStore.ReuseTextFrom(Home);destinationStore.ReuseTextFrom(Destination);
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
internal readonly struct OfflineTravelPreview
{
    internal readonly int HomeDevice,DestinationDevice;
    internal readonly SavedTownPreview Home,Destination;
    internal OfflineTravelPreview(int homeDevice,int destinationDevice,SavedTownPreview home,SavedTownPreview destination){HomeDevice=homeDevice;DestinationDevice=destinationDevice;Home=home;Destination=destination;}
}
internal sealed class RememberedTownRoutes : IOfflineJobTravel
{
    private readonly OfflineJobLedger ledger;
    internal RememberedTownRoutes(OfflineJobLedger ledger){this.ledger=ledger;}
    public bool Reachable(ulong town,JobIdentity npc)=>town!=ledger.TownId&&ledger.NpcTown(npc)==town;
}
