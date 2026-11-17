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
        // Slot limits, physical stands, guest rules and routes use the same tier.
        for(int tier=0;tier<3;tier++){
            var room=new GameState(206){Space=TownShop.Room,X=7.7f,Z=7.7f};room.Town.Spending=tier==0?24999:tier==1?25000:100000;room.Rollover(100,100);
            int count=3+tier*2;check(TownShop.SaleSlots(room)==count&&room.Town.StockCount==6+tier*3,"shop tiers expose exactly three, five and seven sale stands");
            check(BuildingWorld.FixtureCount(room,TownShop.Room)==count+3&&!BuildingWorld.CanStand(room,TownShop.Room,4,4),"one solid central island and only the active wall stands collide");
            for(int slot=0;slot<TownShop.StoredSaleCount;slot++){
                room.Pockets[0]=10;bool listed=room.Town.Shop.List(room,slot,0,500);
                check(listed==(slot<count)&&room.Pockets[0]==(listed?0:10),"tier sale limit applies atomically to real item listings");
                if(slot<TownShop.SaleCount){ShopLayout.Bounds(12+slot,out float x,out float z,out float w,out float d,out _);check(BuildingWorld.CanStand(room,TownShop.Room,x+w*.5f,z+d*.5f)==(slot>=count),"locked stands have no invisible collision");}
            }
            room.Town.Shop.SaleItems[count]=10;room.Town.Shop.SalePrices[count]=500;room.Town.Shop.SaleOwners[count]=room.ActiveProfile;
            guest.Wallet=99999;Array.Clear(guest.Pockets);int guestMoney=guest.Wallet;
            check(!room.Town.Shop.Buy(room,guest,ShopSource.Consignment,count)&&!room.Town.Shop.BuyNpc(room,0,ShopSource.Consignment,count)&&!room.Town.Shop.Reprice(room,count,1)&&guest.Wallet==guestMoney,"guests and NPCs cannot buy or manage stored overflow listings");
            check(room.Town.Shop.StoredListingCount(room)==1&&room.Town.Shop.StoredListing(room,0)==count,"old excess listing is reclaimable without adding a sale stand");
            Array.Fill(room.Pockets,1,0,room.Capacity);check(!room.Town.Shop.Remove(room,count)&&room.Town.Shop.SaleItems[count]==10,"reclaiming with full pockets preserves stored stock");room.Pockets[0]=0;
            foreach(var copy in new[]{SaveCodec.Decode(SaveCodec.Encode(room)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(room))})check(TownShop.SaleSlots(copy)==count&&copy.Town.Shop.SaleItems[count]==10&&copy.Town.Shop.Encode().Length==154,"tiers and stored overflow survive both save formats without a schema/budget change");
            check(room.Town.Shop.Remove(room,count)&&room.Pockets[0]==10&&room.Town.Shop.StoredListingCount(room)==0,"stored item can be reclaimed once by its owner");
            int active=room.Town.StockCount+count+2;var inspected=new HashSet<int>();
            for(int i=0;i<active;i++)check(inspected.Add(ShopLayout.Inspect(room,i))&&ShopLayout.Active(room,ShopLayout.Inspect(room,i)),"fixture inspection visits each active display exactly once");
            check(ShopLayout.Inspect(room,active)==0,"fixture inspection wraps without visiting locked slots");
            for(int fixture=0;fixture<ShopLayout.FixtureCount;fixture++){
                if(!ShopLayout.Active(room,fixture))continue;
                var v=room.Town.Residents[0];v.Space=TownShop.Room;v.PosX=4;v.PosZ=7;
                ShopLayout.Approach(fixture,out float x,out float z);check(BuildingWorld.CanStand(room,TownShop.Room,x,z)&&ShopLayout.Near(room,x,z)==fixture,"shop interaction point clears collision and selects its own fixture");
                var nav=new InteriorNavigation();for(int tick=0;tick<800&&!nav.Arrived(1);tick++){nav.BeginTick();nav.Tick(room,1,x,z);check(nav.LastExpansions<=128&&BuildingWorld.CanStand(room,TownShop.Room,v.PosX,v.PosZ),"shop routes respect collision and search budget");}
                check(nav.Arrived(1),"NPC reaches shop fixture "+fixture+" in tier "+tier);
            }
        }
        var growth=new GameState(209);growth.Rollover(100,100);growth.Town.Spending=100000;
        growth.Space=TownShop.Room;growth.X=.5f;growth.Z=4.7f;
        check(TownShop.SaleSlots(growth)==3&&BuildingWorld.CanStand(growth,TownShop.Room,growth.X,growth.Z),"spending does not spawn wall colliders during the shopping day");growth.Rollover(101,100);check(TownShop.SaleSlots(growth)==7&&BuildingWorld.CanStand(growth,TownShop.Room,growth.X,growth.Z),"overnight upgrades unlock stands and move any overlapping actor to clear floor");
        growth.Space=TownShop.Room;growth.X=growth.Z=4;growth.Town.Residents[0].Space=TownShop.Room;growth.Town.Residents[0].PosX=growth.Town.Residents[0].PosZ=4;
        foreach(var copy in new[]{SaveCodec.Decode(SaveCodec.Encode(growth)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(growth))})check(BuildingWorld.CanStand(copy,TownShop.Room,copy.X,copy.Z)&&BuildingWorld.CanStand(copy,TownShop.Room,copy.Town.Residents[0].PosX,copy.Town.Residents[0].PosZ),"loading a previous shop layout moves actors out of the new solid island");
        var journey=new GameState(207){Space=TownShop.Room,X=7.7f,Z=7.7f};journey.Rollover(100,100);
        var shopper=journey.Town.Residents[0];shopper.Space=TownShop.Room;shopper.PosX=4;shopper.PosZ=7;shopper.JourneyPhase=2;
        var journeys=new VillagerJourney();bool left=false;for(int tick=0;tick<1600;tick++){journeys.Tick(journey);if(shopper.Space==0){left=true;break;}}
        check(journey.Town.Shop.NpcCount[0]==1&&journey.Town.Stock.Take(journey.Town.StockCount).Count(id=>id==0)==1&&left,"NPC browses, exhausts one real display and walks back outside");
    }
}
