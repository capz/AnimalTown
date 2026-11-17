using AnimalTown;

internal static class ShopChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(204){Wallet=99999};s.Rollover(100,100);Array.Clear(s.Pockets);
        var shop=s.Town.Shop;
        check(s.BuildingTypes[7]==1&&BuildingWorld.Size(s,TownShop.Room)==8,"shop has its own reusable building definition");
        for(int i=0;i<TownShop.CabinetCount;i++){
            int id=TownShop.CabinetItem(i);
            check(Catalog.Has(id,ItemFlags.Tool)||Catalog.Has(id,ItemFlags.Plant)||id==Catalog.WorkShirt,"cabinet contains only plants, current tools and starter clothing");
            check(id!=Catalog.LegacyWateringCan&&!Catalog.Has(id,ItemFlags.Money)&&!Catalog.Has(id,ItemFlags.Place),"cabinet excludes retired tools, money and decor");
            for(int n=0;n<3;n++){Array.Clear(s.Pockets);check(s.BuyCabinet(i)&&s.Pockets[0]==id,"essentials cabinet can sell the same item repeatedly");}
        }
        Array.Clear(s.Pockets);s.Wallet=99999;int item=s.Town.Stock[0],price=Catalog.Items[item].Buy;
        check(item>=10&&s.Buy(0)&&s.Town.Stock[0]==0&&s.Wallet==99999-price&&!s.Buy(0),"physical display sells exactly once");
        var guest=new GameState(205){Wallet=99999};Array.Clear(guest.Pockets);int guestStock=s.Town.Stock[1];
        check(shop.Buy(s,guest,ShopSource.Display,1)&&guest.Pockets[0]==guestStock&&s.Town.Stock[1]==0&&s.Wallet==99999-price,"guest uses own pockets and money against host stock");
        check(!shop.Buy(s,guest,ShopSource.Display,1),"host and guest cannot both buy the same finite display");
        Array.Fill(guest.Pockets,1);int revision=shop.Revision;item=s.Town.Stock[2];int wallet=guest.Wallet;
        check(!shop.Buy(s,guest,ShopSource.Display,2)&&guest.Wallet==wallet&&shop.Revision==revision&&s.Town.Stock[2]==item,"full guest pockets do not debit or exhaust stock");
        Array.Clear(guest.Pockets);guest.Wallet=0;
        check(!shop.Buy(s,guest,ShopSource.Display,2)&&s.Town.Stock[2]==item,"poor customer cannot deplete a display");
        s.Pockets[0]=10;check(shop.List(s,0,0,99999)&&s.Pockets[0]==0,"listing escrows the real item");
        s.SelectProfile(1);s.Wallet=99999;Array.Clear(s.Pockets);
        check(!shop.Reprice(s,0,1)&&!shop.Remove(s,0),"other local villagers cannot manage a seller's listing");
        long savings=s.Profiles[0].Savings;int captured=shop.Revision;
        check(shop.Buy(s,s,ShopSource.Consignment,0,captured)&&s.Pockets[0]==10&&s.Wallet==0&&s.Profiles[0].Savings==savings+99999,"consignment credits the exact inactive seller and gives item to buyer");
        check(!shop.Buy(s,s,ShopSource.Consignment,0,captured),"stale double purchase cannot duplicate consignment");
        s.Pockets[1]=11;check(shop.List(s,0,1,1200),"second seller can reuse a sold stand");
        guest.Wallet=99999;check(!shop.Buy(s,guest,ShopSource.Consignment,0,captured),"stale display selection cannot buy a replacement listing");
        s.SelectProfile(0);s.Profiles[1].Created=false;
        check(shop.Buy(s,guest,ShopSource.Consignment,0)&&shop.Proceeds[1]==1200,"missing character card retains seller earnings in town escrow");
        var absent=SplitSaveCodec.Decode(SplitSaveCodec.Encode(s));check(absent.Town.Shop.Proceeds[1]==1200&&!absent.Profiles[1].Created,"unclaimed earnings survive absent-character save");
        s.Profiles[1].Created=true;s.SelectProfile(1);check(s.Savings==1200&&shop.Proceeds[1]==0,"returning seller automatically receives escrow into savings once");
        s.Pockets[1]=22;s.Pockets[2]=18;check(!shop.List(s,0,1,10)&&!shop.List(s,0,2,10),"money bags and quest parcels cannot be listed");
        s.Pockets[3]=10;check(!shop.List(s,0,3,0)&&!shop.List(s,0,3,100000)&&shop.List(s,0,3,500),"asking prices enforce 1 to 99999");
        int npcMoney=shop.NpcWallet[0];check(shop.BuyNpc(s,0,ShopSource.Consignment,0)&&shop.NpcCount[0]==1&&shop.NpcItems[0]==10&&shop.NpcWallet[0]==npcMoney-500&&s.Savings==1700,"NPC purchases spend persistent money and credit the real seller");
        check(!shop.BuyNpc(s,-1,ShopSource.Display,3)&&!shop.Buy(s,s,(ShopSource)99,0)&&!s.BuyCabinet(-1),"invalid buyer, source and slot are rejected");
        foreach(var copy in new[]{SaveCodec.Decode(SaveCodec.Encode(s)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(s))})
            check(copy.Town.Shop.Encode().SequenceEqual(shop.Encode())&&copy.Town.Stock.SequenceEqual(s.Town.Stock)&&copy.Savings==s.Savings,"shop stock, NPC possessions and proceeds round trip in both save formats");
        bool rejected=false;try{SaveCodec.EncodeLegacy(s);}catch(InvalidOperationException){rejected=true;}check(rejected,"legacy export cannot silently lose purchased NPC items");
        s.Rollover(101,100);check(s.Town.Stock.Take(s.Town.StockCount).All(id=>id>=10)&&shop.NpcWallet[0]==Math.Min(20000,npcMoney+500),"next day replenishes daily stock and bounded NPC shopping budget");
        // Every stand must be reachable on the actual half-metre NPC navigation grid.
        for(int fixture=0;fixture<ShopLayout.FixtureCount;fixture++){
            var room=new GameState(206){Space=TownShop.Room,X=7.7f,Z=7.7f};var v=room.Town.Residents[0];v.Space=TownShop.Room;v.PosX=4;v.PosZ=7;
            ShopLayout.Approach(fixture,out float x,out float z);check(BuildingWorld.CanStand(room,TownShop.Room,x,z),"shop interaction point clears fixture collision");
            var nav=new InteriorNavigation();for(int tick=0;tick<800&&!nav.Arrived(1);tick++){nav.BeginTick();nav.Tick(room,1,x,z);check(nav.LastExpansions<=128&&BuildingWorld.CanStand(room,TownShop.Room,v.PosX,v.PosZ),"shop routes respect collision and search budget");}
            check(nav.Arrived(1),"NPC reaches shop fixture "+fixture);
        }
        var journey=new GameState(207){Space=TownShop.Room,X=7.7f,Z=7.7f};journey.Rollover(100,100);
        var shopper=journey.Town.Residents[0];shopper.Space=TownShop.Room;shopper.PosX=4;shopper.PosZ=7;shopper.JourneyPhase=2;
        var journeys=new VillagerJourney();bool left=false;for(int tick=0;tick<1600;tick++){journeys.Tick(journey);if(shopper.Space==0){left=true;break;}}
        check(journey.Town.Shop.NpcCount[0]==1&&journey.Town.Stock.Take(journey.Town.StockCount).Count(id=>id==0)==1&&left,"NPC browses, exhausts one real display and walks back outside");
    }
}
