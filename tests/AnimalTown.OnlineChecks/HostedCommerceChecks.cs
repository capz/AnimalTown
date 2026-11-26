using AnimalTown;
using AnimalTown.Online;

static partial class HostedVisitChecks
{
    static async Task<(Client Winner,OnlineCommand Receipt)> Commerce(Client host,Client guest,Client other,Action<bool,string> check)
    {
        await guest.WalkToPoint(32.5f,28.5f);
        check((await guest.Command(new(){Kind=OnlineCommandKind.EnterRoom,A=7})).Status==200,"Guest walks to the real shop portal before shopping");
        await other.WalkToPoint(32.5f,28.5f);
        check((await other.Command(new(){Kind=OnlineCommandKind.EnterRoom,A=7})).Status==200,"The second guest independently enters the shared shop");
        async Task<Reply> Shop(Client actor,OnlineCommand operation)
        {
            Reply result=default;
            for(int attempt=0;attempt<4;attempt++)
            {
                await actor.Snapshot();operation.Revision=actor.State.World!.Town.Shop.Revision;
                result=await actor.Command(operation);
                if(result.Status!=409||result.Error!="shop-changed")return result;
            }
            return result;
        }
        await guest.Snapshot();int startWallet=guest.State.Wallet,startFlowers=guest.State.Pockets.Count(p=>p==2);
        var cabinet=new OnlineCommand{Kind=OnlineCommandKind.Buy,A=(int)ShopSource.Cabinet,B=1};
        var purchased=await Shop(guest,cabinet);var repeated=await guest.Command(cabinet);
        await guest.Snapshot();
        check(purchased.Status==200&&repeated.Status==200&&guest.State.Wallet==startWallet-Catalog.Items[2].Buy&&guest.State.Pockets.Count(p=>p==2)==startFlowers+1,
            "Real WSS cabinet purchase consumes Bells and grants one item across receipt retries");
        var forbidden=await Shop(guest,new(){Kind=OnlineCommandKind.List,A=(int)ShopSource.Consignment,B=0,C=1,D=99});
        check(forbidden.Status==403,"Guest cannot stock the town's resident-owned consignment displays");

        var list=new OnlineCommand{Kind=OnlineCommandKind.List,A=(int)ShopSource.Consignment,B=0,C=1,D=99};
        check((await Shop(host,list)).Status==200,"Owner listing escrows an actual selected pocket over WSS");
        check((await host.Command(list)).Status==200,"Listing receipt retry does not escrow a second item");
        var price=new OnlineCommand{Kind=OnlineCommandKind.Reprice,A=(int)ShopSource.Consignment,B=0,D=123};
        check((await Shop(host,price)).Status==200&&(await host.Command(price)).Status==200,"Repricing is authoritative and retryable");
        var cancel=new OnlineCommand{Kind=OnlineCommandKind.CancelListing,A=(int)ShopSource.Consignment,B=0};
        check((await Shop(host,cancel)).Status==200&&(await host.Command(cancel)).Status==200,"Cancellation restores the escrowed resident item once");
        await host.Snapshot();check(host.State.Pockets.Count(p=>p==2)==1,"Repeated listing cancellation preserves the exact original item count");

        await Shop(host,new(){Kind=OnlineCommandKind.List,A=(int)ShopSource.Consignment,B=0,C=1,D=123});
        await guest.Snapshot();await other.Snapshot();await host.Snapshot();
        int revision=guest.State.World!.Town.Shop.Revision,firstWallet=guest.State.Wallet,secondWallet=other.State.Wallet;
        long savings=host.State.Savings;
        var one=new OnlineCommand{Kind=OnlineCommandKind.Buy,A=(int)ShopSource.Consignment,B=0,Revision=revision};
        var two=new OnlineCommand{Kind=OnlineCommandKind.Buy,A=(int)ShopSource.Consignment,B=0,Revision=revision};
        var outcomes=await Task.WhenAll(guest.Command(one),other.Command(two));
        check(outcomes.Count(r=>r.Status==200)==1&&outcomes.Count(r=>r.Status==409)==1,"Two live humans racing one listing produce exactly one purchase");
        Client winner=outcomes[0].Status==200?guest:other;
        check((await winner.Command(outcomes[0].Status==200?one:two)).Status==200,"The winning purchase's receipt survives a lost-response retry");
        await guest.Snapshot();await other.Snapshot();await host.Snapshot();
        check(firstWallet-guest.State.Wallet+secondWallet-other.State.Wallet==123&&host.State.Savings==savings+123,
            "A raced purchase debits and credits exactly one price to the correct private economies");
        check(host.State.World!.Town.Shop.SaleItems[0]==0,"The sold item is absent from the shared display");
        check((await other.Command(new(){Kind=OnlineCommandKind.EnterRoom,A=7})).Status==200,"Guest exits the shop using its real portal");
        await other.WalkToPoint(17.5f,25.5f);await other.Snapshot();
        var occupied=await other.Command(new(){Kind=OnlineCommandKind.Place,Revision=other.State.InventoryRevision,B=3,C=other.Position.X/65536,D=other.Position.Z/65536});
        check(occupied.Status==409&&occupied.Error=="placement-occupied-by-player","Furniture cannot be placed underneath a live human");
        var placed=await other.Command(new(){Kind=OnlineCommandKind.Place,Revision=other.State.InventoryRevision,B=3,C=17,D=24});
        check(placed.Status==200,"A clear reachable furnishing footprint remains usable after shopping");
        var entry=new OnlinePacketReader(placed.Extra,0,placed.Extra.Length);var placement=entry.Id();int version=entry.Int();
        var returnLease=await host.Command(new(){Kind=OnlineCommandKind.ReviewBegin});
        check(returnLease.Status==200,"Owner starts review of a real pending placement after commerce: "+returnLease.Error);
        var lease=new OnlinePacketReader(returnLease.Extra,0,returnLease.Extra.Length).Id();
        while(host.State.NeedsSnapshot)await host.Receive();
        check((await host.Command(new(){Kind=OnlineCommandKind.ReviewDecision,Entity=placement,Revision=version,A=1,Text=OnlineIdentifiers.Format(lease)})).Status==200,"Koban review commits after permanent commerce");
        check((await host.Command(new(){Kind=OnlineCommandKind.ReviewEnd,Entity=lease})).Status==200,
            "Owner review can interrupt and resume the town after permanent commerce");
        await winner.Snapshot();
        check(winner.State.Wallet==(ReferenceEquals(winner,guest)?firstWallet:secondWallet)-123,"Review does not undo the confirmed guest purchase");
        return(winner,outcomes[0].Status==200?one:two);
    }
}
