using AnimalTown;

internal static class ProfileChecks
{
    private sealed class Storage : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal int Writes;
        public byte[] Read(string name)=>(byte[])Files[name].Clone();
        public void Write(string name,byte[] bytes){Writes++;Files[name]=(byte[])bytes.Clone();}
    }
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(7319);Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);s.Town.GroundCount=0;
        var tiles=s.Town.Tiles;var npc=s.Town.Residents[0];npc.Name="Fern";npc.PosX=120; npc.PosZ=120;
        for(int profile=0;profile<4;profile++){
            s.SelectProfile(profile);s.Name="Villager"+profile;s.Species=profile;s.Wallet=1000+profile;s.Savings=10000+profile;s.Debt=20000+profile;
            s.BackpackTier=profile%3;s.Pockets[0]=5+profile;s.Owned[5+profile]=1;s.MailCount=1;s.Mail[0]=-10-profile;s.Mail[127]=25;
            s.Town.Residents[0].Friendship=30+profile;s.Town.Residents[0].Request=1+profile;s.Town.Residents[0].Met=1;
            check(s.PlaceHouse(10+profile*25,60,false),"every villager can place a separate home in one town");
            s.HouseTier=5;s.GardenTier=4;s.Pockets[1]=10;
            check(s.Place(1,1,1,0,BuildingWorld.PlayerRoom(profile)),"each profile owns distinct interior furnishings");
            s.Pockets[1]=10;check(s.Place(1,s.Town.HouseX+2,s.Town.HouseZ+7,0,0),"each profile owns its garden furnishings");
            if(profile>0)check(!s.PlaceHouse(10,60,false),"new homes cannot overlap another villager's plot");
        }
        check(ReferenceEquals(tiles,s.Town.Tiles)&&s.Town.PlacedCount==8,"switching villagers preserves one shared terrain and furnishing set");
        check(s.Town.Residents[0].PosX==120&&s.Town.Residents[0].Name=="Fern","NPC location and identity remain shared");
        s.Town.Stock[0]=7;s.Town.StockCount=6;s.Wallet=10000;check(s.Buy(0),"shared shop purchase succeeds");
        s.SelectProfile(0);check(s.Town.Stock[0]==0,"another villager sees purchased shared shop stock");
        for(int p=0;p<4;p++){
            s.SelectProfile(p);check(s.Name=="Villager"+p&&s.Species==p&&s.Savings==10000+p&&s.Debt==20000+p,"character identity and money stay independent");
            check(s.Pockets[0]==5+p&&s.Owned[5+p]==1&&s.Town.Residents[0].Friendship==30+p,"inventory, ownership and friendships stay independent");
            check(s.CanWalk(11,62,0)&&!s.CanWalk(12,61,0)&&!s.CanWalk(37,61,0),"all player homes remain world obstacles regardless of active profile");
            int room=BuildingWorld.PlayerRoom((p+1)%4);check(BuildingWorld.Available(s,room),"villagers can visit another profile's home");
            check(!s.CanPlace(10,2,2,0,room,0),"visitors cannot furnish another villager's interior");
        }
        s.SelectProfile(2);s.Space=BuildingWorld.PlayerRoom(2);s.X=5;s.Z=5;
        var combined=SaveCodec.Decode(SaveCodec.Encode(s));var split=SplitSaveCodec.Decode(SplitSaveCodec.Encode(s));
        foreach(var copy in new[]{combined,split}){
            check(copy.ActiveProfile==2&&copy.Space==BuildingWorld.PlayerRoom(2)&&copy.X==5,"active resident and interior survive save/reload");
            check(copy.Town.PlacedCount==8&&copy.Town.Stock[0]==0&&copy.Town.Residents[0].Name=="Fern","shared world survives full round trip");
            for(int p=0;p<4;p++){copy.SelectProfile(p);check(copy.Name=="Villager"+p&&copy.Town.Residents[0].Friendship==30+p&&copy.HousePlaced&&copy.Town.HouseX==10+p*25,"all four personal records and homes survive reload");}
        }
        s.Space=0;s.Town.Day=10;s.Town.Month=100;s.PendingKind=1;s.PendingTier=5;s.PendingDay=11;
        s.SelectProfile(0);s.PendingKind=2;s.PendingTier=4;s.PendingDay=11;s.Rollover(11,101);
        check(s.Mail[0]>0&&s.Jobs==0&&s.PendingKind==0,"active profile receives calendar progression");
        s.SelectProfile(2);check(s.PendingKind==0&&s.Mail[0]>0&&s.Savings==10002+10002/200,"inactive profiles receive rollover exactly once");
        long savings=s.Savings;s.Rollover(11,101);check(s.Savings==savings,"profile switching cannot duplicate shared calendar interest");
        var disk=new Storage();var repo=new SaveRepository(disk);repo.Save(s);s.SelectProfile(1);s.Wallet=4242;repo.Save(s);
        var recovered=repo.Load()!;recovered.SelectProfile(1);check(recovered.Wallet==4242,"saving another profile preserves its progress");recovered.SelectProfile(0);check(recovered.Name=="Villager0"&&recovered.HousePlaced,"saving another profile preserves offline residents");
        check(disk.Files.ContainsKey("AT-Villager2-0")&&disk.Files.ContainsKey("AT-Villager4-1"),"profiles have separate physical card records in both banks");
        var records=SplitSaveCodec.Encode(s);var commit=SplitSaveCodec.EncodeCommit(s,records);records[4][40]^=1;bool rejected=false;try{SplitSaveCodec.DecodeCommitted(commit,records);}catch{rejected=true;}check(rejected,"corruption in an inactive profile invalidates the whole transaction");
        var legacy=new GameState(98){Name="Original",Wallet=1234};var imported=SplitSaveCodec.Decode(SplitSaveCodec.EncodeLegacy(legacy));
        check(imported.ActiveProfile==0&&imported.Name=="Original"&&imported.Profiles.Skip(1).All(p=>!p.Created),"old single-player saves migrate to slot one with three empty slots");
        var combinedLegacy=SaveCodec.Decode(SaveCodec.EncodeLegacy(legacy));check(combinedLegacy.Name=="Original"&&combinedLegacy.Wallet==1234,"v7 import remains supported");
        Console.WriteLine("Four shared-town profiles: independent progress, homes, world ownership, calendar, commits and migration passed");
    }
}
