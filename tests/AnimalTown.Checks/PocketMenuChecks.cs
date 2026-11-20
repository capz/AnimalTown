using AnimalTown;

internal static class PocketMenuChecks
{
    internal static void Run(Action<bool,string> check)
    {
        void Menu(int item,int room,params PocketAction[] expected)
        {
            var actual=Enumerable.Range(0,PocketMenu.Count(item,room)).Select(row=>PocketMenu.At(item,room,row));
            check(actual.SequenceEqual(expected),"context actions for "+item+" in room "+room);
            check(PocketMenu.At(item,room,-1)==PocketAction.Quit&&PocketMenu.At(item,room,expected.Length)==PocketAction.Quit,"out-of-range popup rows safely quit");
        }
        int[] tools={Catalog.FishingRod,Catalog.BugNet,Catalog.Shovel,Catalog.Axe};
        check(Enumerable.Range(1,Catalog.Items.Length-1).Where(id=>Catalog.Has(id,ItemFlags.Tool)).SequenceEqual(tools),"only the four implemented GameCube tools are equippable");
        check(Enumerable.Range(0,Catalog.ToolCount).Select(Catalog.ToolAt).SequenceEqual(tools),"shop tool selection contains all four GameCube tools");
        foreach(int tool in tools){
            Menu(tool,0,PocketAction.Equip,PocketAction.Drop,PocketAction.Quit);
            Menu(tool,1,PocketAction.Equip,PocketAction.Quit);
            var s=new GameState(73);s.Pockets[0]=tool;s.Tool=Catalog.Shovel;
            var action=PocketMenu.At(tool,0,0);
            check(PocketMenu.Perform(s,0,action,30,40,0,0)&&s.Tool==tool&&s.Pockets[0]==Catalog.Shovel,"first filtered row equips and preserves the previously held tool");
            s.Pockets[0]=tool;int groundCount=s.Town.GroundCount,wallet=s.Wallet,shirt=s.Shirt;
            foreach(var forbidden in new[]{PocketAction.Place,PocketAction.Plant,PocketAction.Eat,PocketAction.Wear,PocketAction.Bury,PocketAction.OpenMoneyBag,PocketAction.Read})
                check(!PocketMenu.Perform(s,0,forbidden,30,40,0,0),"tool rejects unrelated action "+forbidden);
            check(s.Pockets[0]==tool&&s.Tool==tool&&s.Town.GroundCount==groundCount&&s.Wallet==wallet&&s.Shirt==shirt,"rejected tool actions leave possessions unchanged");
            check(!PocketMenu.Perform(s,0,PocketAction.Drop,30,40,0,1)&&s.Pockets[0]==tool,"indoor menu cannot drop a tool into the outdoor world");
            check(PocketMenu.Perform(s,0,PocketMenu.At(tool,0,1),30,40,0,0)&&s.Pockets[0]==0&&s.Town.Ground[s.Town.GroundCount-1].Item==tool,"second filtered row drops the selected tool");
        }
        Menu(1,0,PocketAction.Drop,PocketAction.Plant,PocketAction.Eat,PocketAction.Bury,PocketAction.Quit);
        Menu(1,1,PocketAction.Eat,PocketAction.Quit);
        Menu(Catalog.WorkShirt,0,PocketAction.Wear,PocketAction.Drop,PocketAction.Quit);
        Menu(Catalog.WorkShirt,1,PocketAction.Wear,PocketAction.Quit);
        Menu(10,0,PocketAction.Drop,PocketAction.Place,PocketAction.Quit);
        Menu(25,0,PocketAction.Drop,PocketAction.Quit);
        Menu(25,1,PocketAction.Place,PocketAction.Quit);
        Menu(19,0,PocketAction.Drop,PocketAction.Read,PocketAction.Quit);
        Menu(Catalog.Acorns1000,0,PocketAction.OpenMoneyBag,PocketAction.Drop,PocketAction.Bury,PocketAction.Quit);
        foreach(int id in new[]{0,-1,Catalog.Items.Length})Menu(id,0,PocketAction.Quit);
        Menu(20,0,PocketAction.Drop,PocketAction.Quit); // Lost umbrella is a quest item.
        Menu(Catalog.LegacyWateringCan,0,PocketAction.Drop,PocketAction.Quit);
        var actions=new GameState(73);actions.Pockets[0]=4;
        check(PocketMenu.Perform(actions,0,PocketMenu.At(Catalog.WorkShirt,0,0),30,40,0,0)&&actions.Shirt==Catalog.WorkShirt,"first clothing row wears the tutorial shirt");
        actions.Pockets[0]=1;actions.Town.SetTerrain(30,40,Terrain.Grass);
        check(PocketMenu.Perform(actions,0,PocketMenu.At(1,0,1),30,40,0,0)&&actions.Pockets[0]==0,"filtered Plant row still plants fruit");
        actions.Pockets[0]=22;actions.Wallet=100;
        check(PocketMenu.Perform(actions,0,PocketMenu.At(22,1,0),0,0,0,1)&&actions.Wallet==1100&&actions.Pockets[0]==0,"filtered indoor money-bag action credits its actual value");
        check(!PocketMenu.Perform(actions,-1,PocketAction.Equip,0,0,0,0)&&!PocketMenu.Perform(actions,actions.Capacity,PocketAction.Equip,0,0,0,0),"invalid pocket slots are rejected");

        int[] entries=Enumerable.Range(0,Catalog.EntryCount).Select(Catalog.EntryAt).ToArray();
        check(entries.SequenceEqual(Enumerable.Range(1,Catalog.Items.Length-1).Where(id=>id!=Catalog.LegacyWateringCan&&id!=22&&id!=23&&id!=24)),"catalog skips currency and the retired watering can without shifting item identities");
        check(Catalog.EntryAt(7)==Catalog.Axe&&Catalog.EntryAt(-1)==0&&Catalog.EntryAt(Catalog.EntryCount)==0,"catalog row lookup stays bounded across the retired ID gap");
        foreach(int spending in new[]{0,25000,100000}){
            var shop=new GameState(73);shop.Town.Spending=spending;shop.Rollover(100,100);
            check(shop.Town.StockCount==(spending==0?6:spending==25000?9:12)&&shop.Town.Stock.Take(shop.Town.StockCount).All(id=>id>=10)&&tools.All(id=>Enumerable.Range(0,TownShop.CabinetCount).Any(i=>TownShop.CabinetItem(i)==id)),"all store tiers have finite decor displays and unlimited GameCube tools in the cabinet");
            shop.Town.Stock[2]=Catalog.LegacyWateringCan;shop.Wallet=5000;shop.Owned[Catalog.LegacyWateringCan]=1;
            check(!shop.Buy(2)&&!shop.Order(Catalog.LegacyWateringCan)&&shop.Wallet==5000&&shop.MailCount==0,"retired watering can cannot be bought or ordered from old data");
            shop.Rollover(100,100);
            check(shop.Town.Stock[2]!=Catalog.LegacyWateringCan,"same-day old shop stock is repaired without waiting for rollover");
            shop.Owned[Catalog.Axe]=1;
            check(shop.Order(Catalog.EntryAt(7))&&shop.Mail[0]==-Catalog.Axe,"catalog selection orders the shown tool after the ID gap");
        }
        var legacy=new GameState(73);legacy.Tool=8;legacy.Pockets[0]=8;legacy.Owned[8]=1;legacy.Mail[0]=-8;legacy.Mail[1]=8;legacy.MailCount=2;
        foreach(var restored in new[]{SaveCodec.Decode(SaveCodec.Encode(legacy)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(legacy)),SplitSaveCodec.Decode(SplitSaveCodec.EncodeLegacy(legacy))}){
            check(restored.Tool==8&&restored.Pockets[0]==8&&restored.Owned[8]==1&&restored.Mail[0]==-8&&restored.Mail[1]==8,"legacy watering-can possessions survive single and split/profile save formats");
            check(!restored.Use(0,4,0,0)&&restored.Tool==8&&restored.Pockets[0]==8,"legacy item cannot be newly equipped");
            restored.Pockets[1]=Catalog.Axe;
            check(PocketMenu.Perform(restored,1,PocketAction.Equip,0,0,0,0)&&restored.Tool==Catalog.Axe&&restored.Pockets[1]==8,"legacy held item is preserved when replacing it with a supported tool");
            check(restored.Sell(0)&&restored.Wallet==125,"legacy watering can remains sellable");
        }
        Clothing(check);
        Money(check);
    }
    private static void Clothing(Action<bool,string> check)
    {
        check(Enumerable.Range(1,Catalog.Items.Length-1).Where(id=>Catalog.Has(id,ItemFlags.Wear)).SequenceEqual(new[]{Catalog.WorkShirt,Catalog.StripedShirt,Catalog.LeafShirt,Catalog.LinenShirt,Catalog.BerryShirt,Catalog.SkyShirt}),"the clothing catalog retains the work shirt and reusable resident reward shirts");
        foreach(int room in new[]{0,1}){
            var s=new GameState(73);s.Tool=Catalog.Shovel;s.BackpackTier=2;Array.Fill(s.Pockets,Catalog.WorkShirt);
            int slot=s.Capacity-1;
            check(PocketMenu.Label(PocketMenu.At(Catalog.WorkShirt,room,0))=="Wear","clothing always opens with Wear selected");
            check(PocketMenu.Perform(s,slot,PocketMenu.At(Catalog.WorkShirt,room,0),30,40,0,room)&&s.Shirt==Catalog.WorkShirt&&s.Pockets[slot]==0&&s.Tool==Catalog.Shovel,"wearing works with full pockets, retains the held tool and leaves no phantom default-shirt item");
            s.Pockets[slot]=Catalog.WorkShirt;
            int copies=s.Pockets.Count(id=>id==Catalog.WorkShirt)+1;
            check(PocketMenu.Perform(s,slot,PocketAction.Wear,30,40,0,room)&&s.Pockets.Count(id=>id==Catalog.WorkShirt)+1==copies&&s.Pockets[slot]==Catalog.WorkShirt,"wearing another copy swaps back the previously worn shirt even with full pockets");
            var before=SaveCodec.Encode(s);
            foreach(var forbidden in new[]{PocketAction.Equip,PocketAction.Place,PocketAction.Plant,PocketAction.Eat,PocketAction.Bury,PocketAction.OpenMoneyBag,PocketAction.Read})
                check(!PocketMenu.Perform(s,slot,forbidden,30,40,0,room),"clothing rejects unrelated action "+forbidden);
            check(before.SequenceEqual(SaveCodec.Encode(s)),"rejected clothing actions preserve all saved state");
            foreach(var restored in new[]{SaveCodec.Decode(before),SplitSaveCodec.Decode(SplitSaveCodec.Encode(s)),SplitSaveCodec.Decode(SplitSaveCodec.EncodeLegacy(s))})
                check(restored.Shirt==Catalog.WorkShirt&&restored.Pockets.SequenceEqual(s.Pockets)&&restored.Tool==Catalog.Shovel,"worn and pocket clothing survive single and split/profile save formats");
            if(room==0)check(PocketMenu.Perform(s,slot,PocketMenu.At(Catalog.WorkShirt,room,1),30,40,0,room)&&s.Pockets[slot]==0&&s.Shirt==Catalog.WorkShirt&&s.Town.Ground[s.Town.GroundCount-1].Item==Catalog.WorkShirt,"Drop removes only the selected pocket shirt, leaving the worn shirt intact");
            else check(!PocketMenu.Perform(s,slot,PocketAction.Drop,30,40,0,room)&&s.Pockets[slot]==Catalog.WorkShirt,"indoor clothing cannot leak into the outdoor drop list");
            s.Pockets[slot]=Catalog.WorkShirt;
            check(s.Sell(slot)&&s.Shirt==Catalog.WorkShirt&&s.Wallet==Catalog.Items[Catalog.WorkShirt].Sell,"selling pocket clothing preserves the worn shirt");
        }
        var profiles=new GameState(73);
        check(PocketMenu.Perform(profiles,0,PocketAction.Wear,0,0,0,0),"first villager can wear their starter shirt");
        profiles.SelectProfile(1);
        check(profiles.Shirt==0&&profiles.Pockets[0]==Catalog.WorkShirt,"new villager retains an independent default outfit and starter shirt");
        profiles.SelectProfile(0);
        check(profiles.Shirt==Catalog.WorkShirt&&profiles.Pockets[0]==0,"returning to the first villager restores their worn shirt");
        var saved=SplitSaveCodec.Decode(SplitSaveCodec.Encode(profiles));saved.SelectProfile(1);
        check(saved.Shirt==0&&saved.Pockets[0]==Catalog.WorkShirt,"saving the town does not copy clothing between villagers");
        long allocated=GC.GetAllocatedBytesForCurrentThread();int rows=0;
        for(int i=0;i<1000;i++){rows+=PocketMenu.Count(Catalog.WorkShirt,0);rows+=(int)PocketMenu.At(Catalog.WorkShirt,0,i%3);}
        check(GC.GetAllocatedBytesForCurrentThread()==allocated&&rows>0,"clothing menu queries allocate no per-frame lists");
    }
    private static void Money(Action<bool,string> check)
    {
        int[] bags={Catalog.Acorns1000,Catalog.Acorns10000,Catalog.LegacyAcorns50000};
        int[] values={1000,10000,50000};
        check(Enumerable.Range(1,Catalog.Items.Length-1).Where(id=>Catalog.Has(id,ItemFlags.Money)).SequenceEqual(bags),"only money items expose wallet actions");
        foreach(int id in new[]{0,-1,Catalog.Items.Length,Catalog.WorkShirt,Catalog.Shovel})check(Catalog.MoneyValue(id)==0,"non-money items have no redeemable value");
        for(int i=0;i<bags.Length;i++)foreach(int room in new[]{0,1}){
            int id=bags[i],value=values[i];
            PocketAction[] expected=room!=0?new[]{PocketAction.OpenMoneyBag,PocketAction.Quit}:id==Catalog.LegacyAcorns50000?
                new[]{PocketAction.OpenMoneyBag,PocketAction.Drop,PocketAction.Quit}:new[]{PocketAction.OpenMoneyBag,PocketAction.Drop,PocketAction.Bury,PocketAction.Quit};
            check(Enumerable.Range(0,PocketMenu.Count(id,room)).Select(row=>PocketMenu.At(id,room,row)).SequenceEqual(expected),"money menus offer only supported actions in the current room");
            check(PocketMenu.Label(PocketMenu.At(id,room,0))=="Put in wallet"&&Catalog.MoneyValue(id)==value,"money opens on its wallet action and retains its exact denomination");
            var s=new GameState(73){BackpackTier=2,Tool=Catalog.Shovel,Shirt=Catalog.WorkShirt,Wallet=GameState.WalletLimit-value};
            Array.Fill(s.Pockets,1);int slot=s.Capacity-1;s.Pockets[0]=id;s.Pockets[slot]=id;
            check(PocketMenu.Perform(s,slot,PocketMenu.At(id,room,0),30,40,0,room)&&s.Wallet==GameState.WalletLimit&&s.Pockets[slot]==0&&s.Pockets[0]==id&&s.Tool==Catalog.Shovel&&s.Shirt==Catalog.WorkShirt,"full-pocket redemption credits exactly one bag, allows an exact wallet fill and preserves equipment");
            check(!PocketMenu.Perform(s,slot,PocketAction.OpenMoneyBag,30,40,0,room)&&s.Wallet==GameState.WalletLimit,"a consumed money bag cannot credit twice");
            s.Pockets[slot]=id;s.Wallet=GameState.WalletLimit-value+1;
            byte[] before=SaveCodec.Encode(s);
            check(!PocketMenu.Perform(s,slot,PocketAction.OpenMoneyBag,30,40,0,room)&&before.SequenceEqual(SaveCodec.Encode(s)),"one-acorn overflow rejects the complete bag without losing money or items");
            foreach(var forbidden in new[]{PocketAction.Equip,PocketAction.Wear,PocketAction.Place,PocketAction.Plant,PocketAction.Eat,PocketAction.Read})
                check(!PocketMenu.Perform(s,slot,forbidden,30,40,0,room),"money rejects unrelated action "+forbidden);
            if(room!=0)check(!PocketMenu.Perform(s,slot,PocketAction.Drop,30,40,0,room)&&!PocketMenu.Perform(s,slot,PocketAction.Bury,30,40,0,room),"indoor money cannot leak to outdoor ground records");
            check(before.SequenceEqual(SaveCodec.Encode(s)),"rejected currency actions are atomic");
            s.Owned[id]=1;s.Town.StockCount=6;s.Town.Stock[0]=id;
            before=SaveCodec.Encode(s);
            check(Catalog.Items[id].Buy==0&&Catalog.Items[id].Sell==0&&!s.Buy(0)&&!s.Order(id)&&!s.Sell(slot)&&before.SequenceEqual(SaveCodec.Encode(s)),"currency cannot be purchased, catalog-ordered or sold even with old owned/stock data");
            foreach(var restored in new[]{SaveCodec.Decode(before),SplitSaveCodec.Decode(SplitSaveCodec.Encode(s)),SplitSaveCodec.Decode(SplitSaveCodec.EncodeLegacy(s))}){
                check(restored.Wallet==s.Wallet&&restored.Pockets.SequenceEqual(s.Pockets),"currency bags and wallet survive single and split/profile save formats");
                restored.Wallet=0;
                check(PocketMenu.Perform(restored,slot,PocketAction.OpenMoneyBag,0,0,0,room)&&restored.Wallet==value,"loaded bags redeem their full original value, including legacy 50000");
            }
            var ground=new GameState(73){Wallet=17};ground.Town.GroundCount=0;ground.Town.SetTerrain(30,40,Terrain.Grass);ground.Pockets[0]=id;
            check(PocketMenu.Perform(ground,0,PocketAction.Drop,30,40,0,0)&&ground.Wallet==17&&ground.Pickup(30,40)&&ground.Find(id)>=0&&ground.Wallet==17,"dropping and picking up a bag preserves its value independently of the wallet");
            ground.Pockets[0]=id;ground.Town.SetTerrain(31,40,Terrain.Grass);
            bool bury=PocketMenu.Perform(ground,0,PocketAction.Bury,31,40,0,0);
            check(id==Catalog.LegacyAcorns50000?!bury&&ground.Pockets[0]==id:bury&&ground.Pockets[0]==0&&ground.Town.Ground[ground.Town.GroundCount-1].Item==id&&ground.Town.Ground[ground.Town.GroundCount-1].Kind==1,"only supported GameCube bags can create new buried money");
        }
        check(Catalog.EntryAt(Catalog.EntryCount-1)==Catalog.CherrySapling&&Catalog.EntryAt(Catalog.EntryCount)==0,"catalog indices remain correct after all currency rows are removed");
        var profiles=new GameState(73){Wallet=17};profiles.Pockets[0]=Catalog.Acorns1000;
        check(PocketMenu.Perform(profiles,0,PocketAction.OpenMoneyBag,0,0,0,0)&&profiles.Wallet==1017,"first villager redeems their own bag");
        profiles.SelectProfile(1);profiles.Wallet=23;profiles.Pockets[0]=Catalog.Acorns10000;profiles.SelectProfile(0);
        var saved=SplitSaveCodec.Decode(SplitSaveCodec.Encode(profiles));
        check(saved.Wallet==1017&&saved.Pockets[0]==0,"first villager's wallet and consumed bag persist");saved.SelectProfile(1);
        check(saved.Wallet==23&&saved.Pockets[0]==Catalog.Acorns10000,"wallets and money bags remain independent between villagers");
        var legacy=new GameState(73);legacy.Town.GroundCount=0;
        check(legacy.Town.AddGround(Catalog.LegacyAcorns50000,30,40,1,0),"construct an older save with a buried 50000 bag");
        foreach(var restored in new[]{SaveCodec.Decode(SaveCodec.Encode(legacy)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(legacy)),SplitSaveCodec.Decode(SplitSaveCodec.EncodeLegacy(legacy))}){
            int index=restored.Town.FindGround(30,40,1);
            check(index>=0&&restored.Town.Ground[index].Item==Catalog.LegacyAcorns50000,"old buried 50000 bags remain readable after new burial is retired");
            int item=restored.Town.Ground[index].Item;
            check(restored.Receive(item)&&restored.Use(restored.Find(item),6,0,0)&&restored.Wallet==50000,"recovered legacy money retains all 50000 acorns");
        }
    }
}
