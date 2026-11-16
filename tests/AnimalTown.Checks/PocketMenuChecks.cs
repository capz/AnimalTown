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
        Menu(4,0,PocketAction.Drop,PocketAction.Wear,PocketAction.Quit);
        Menu(10,0,PocketAction.Drop,PocketAction.Place,PocketAction.Quit);
        Menu(25,0,PocketAction.Drop,PocketAction.Quit);
        Menu(25,1,PocketAction.Place,PocketAction.Quit);
        Menu(19,0,PocketAction.Drop,PocketAction.Read,PocketAction.Quit);
        Menu(22,0,PocketAction.Drop,PocketAction.Bury,PocketAction.OpenMoneyBag,PocketAction.Quit);
        foreach(int id in new[]{0,-1,Catalog.Items.Length})Menu(id,0,PocketAction.Quit);
        Menu(20,0,PocketAction.Drop,PocketAction.Quit); // Lost umbrella is a quest item.
        Menu(Catalog.LegacyWateringCan,0,PocketAction.Drop,PocketAction.Quit);
        var actions=new GameState(73);actions.Pockets[0]=4;
        check(PocketMenu.Perform(actions,0,PocketMenu.At(4,0,1),30,40,0,0)&&actions.Shirt==4,"filtered Wear row still equips the tutorial shirt");
        actions.Pockets[0]=1;actions.Town.SetTerrain(30,40,Terrain.Grass);
        check(PocketMenu.Perform(actions,0,PocketMenu.At(1,0,1),30,40,0,0)&&actions.Pockets[0]==0,"filtered Plant row still plants fruit");
        actions.Pockets[0]=22;actions.Wallet=100;
        check(PocketMenu.Perform(actions,0,PocketMenu.At(22,1,0),0,0,0,1)&&actions.Wallet==1100&&actions.Pockets[0]==0,"filtered indoor money-bag action credits its actual value");
        check(!PocketMenu.Perform(actions,-1,PocketAction.Equip,0,0,0,0)&&!PocketMenu.Perform(actions,actions.Capacity,PocketAction.Equip,0,0,0,0),"invalid pocket slots are rejected");

        int[] entries=Enumerable.Range(0,Catalog.EntryCount).Select(Catalog.EntryAt).ToArray();
        check(entries.SequenceEqual(Enumerable.Range(1,Catalog.Items.Length-1).Where(id=>id!=Catalog.LegacyWateringCan)),"catalog skips only the retired watering can without shifting item identities");
        check(Catalog.EntryAt(7)==Catalog.Axe&&Catalog.EntryAt(-1)==0&&Catalog.EntryAt(Catalog.EntryCount)==0,"catalog row lookup stays bounded across the retired ID gap");
        foreach(int spending in new[]{0,25000,100000}){
            var shop=new GameState(73);shop.Town.Spending=spending;shop.Rollover(100,100);
            check(shop.Town.StockCount==(spending==0?6:spending==25000?9:12)&&shop.Town.Stock.Take(4).SequenceEqual(tools)&&!shop.Town.Stock.Take(shop.Town.StockCount).Contains(Catalog.LegacyWateringCan),"all store tiers stock only GameCube tools");
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
    }
}
