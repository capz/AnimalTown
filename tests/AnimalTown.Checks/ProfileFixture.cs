using AnimalTown;
using System.Text.Json;

internal static class ProfileFixture
{
    private sealed class Storage : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        public byte[] Read(string name)=>Files[name];
        public void Write(string name,byte[] data)=>Files[name]=(byte[])data.Clone();
    }
    internal static void Write(string output,bool money=false,bool shop=false,int shopTier=0,bool chat=false)
    {
        Directory.CreateDirectory(output);var s=new GameState(7319);
        if(shop)s.Town.Spending=shopTier==2?100000:shopTier==1?25000:0;
        s.Rollover(21014,2027*12+7);
        string[] names={"Ari","Bram","Cleo","Dune"};
        for(int slot=0;slot<4;slot++){
            s.SelectProfile(slot);s.Name=names[slot];s.Species=slot;s.Wallet=1001+slot*100;s.Savings=5000+slot*100;s.HouseTier=1;
            bool placed=false;for(int z=20;z<Town.Height-13&&!placed;z++)for(int x=4;x<Town.Width-15&&!placed;x++)placed=s.PlaceHouse(x,z,false);
            if(!placed)throw new Exception("Fixture town has no spare home plot");
            for(int i=s.Town.GroundCount-1;i>=0;i--){var g=s.Town.Ground[i];if(g.X>=s.Town.HouseX&&g.X<s.Town.HouseX+15&&g.Z>=s.Town.HouseZ&&g.Z<s.Town.HouseZ+13)s.Town.RemoveGround(i);}
            s.Pockets[1]=10;if(!s.Place(1,1,1,0,BuildingWorld.PlayerRoom(slot)))throw new Exception("Fixture furnishing failed");
        }
        s.SelectProfile(0);s.Space=0;s.MoveToHouseEntrance();
        if(money){s.Pockets[0]=Catalog.Acorns1000;s.Pockets[1]=Catalog.Acorns10000;s.Pockets[2]=Catalog.LegacyAcorns50000;s.Wallet=GameState.WalletLimit-1000;for(int id=22;id<=24;id++)s.Owned[id]=1;}
        if(shop){
            s.SelectProfile(1);s.Pockets[0]=25;if(!s.Town.Shop.List(s,0,0,1800))throw new Exception("Shop guest-seller fixture failed");
            s.SelectProfile(0);Array.Clear(s.Pockets);s.Wallet=20000;s.Pockets[0]=11;
            if(!s.Town.Shop.List(s,1,0,2500))throw new Exception("Shop seller fixture failed");
            s.Space=0;BuildingWorld.Entrance(s,7,out s.X,out s.Z);s.Facing=(float)Math.PI;
            for(int i=0;i<6;i++)s.Town.Residents[i].Dwell=36000;
            var npc=s.Town.Residents[0];npc.Space=TownShop.Room;npc.PosX=4;npc.PosZ=7;npc.JourneyPhase=2;npc.Dwell=1;s.Town.Shop.NpcTarget[0]=0;
        }
        if(chat){
            s.Space=BuildingWorld.PlayerRoom(0);s.X=3;s.Z=5;s.Facing=(float)Math.PI;
            var npc=s.Town.Residents[0];npc.Name="Rowan";npc.Space=s.Space;npc.PosX=4.5f;npc.PosZ=3.5f;npc.JourneyPhase=3;npc.Dwell=36000;
            if(!BuildingWorld.CanStand(s,s.Space,s.X,s.Z)||!BuildingWorld.CanStand(s,s.Space,npc.PosX,npc.PosZ))throw new Exception("Chat fixture positions must be clear");
        }
        var disk=new Storage();var repository=new SaveRepository(disk);repository.Save(s);repository.Save(s);
        foreach(var file in disk.Files)File.WriteAllBytes(Path.Combine(output,file.Key),file.Value);
        File.WriteAllText(Path.Combine(output,"fixture.json"),JsonSerializer.Serialize(new{seed=s.Town.Seed,sequence=s.Sequence,names,files=disk.Files.Keys},new JsonSerializerOptions{WriteIndented=true}));
        Console.WriteLine("Disposable four-profile fixture: "+Path.GetFullPath(output));
    }
}
