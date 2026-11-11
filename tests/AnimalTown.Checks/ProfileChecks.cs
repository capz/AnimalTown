using AnimalTown;

internal static class ProfileChecks
{
    private sealed class Storage : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal int Writes;
        internal int FailAt;
        internal bool FailAfter;
        public byte[] Read(string name)=>(byte[])Files[name].Clone();
        public void Write(string name,byte[] bytes){Writes++;if(Writes==FailAt&&!FailAfter)throw new IOException("Interrupted write");Files[name]=(byte[])bytes.Clone();if(Writes==FailAt&&FailAfter)throw new IOException("Interrupted after write");}
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
        DeleteChecks(check);
        OnboardingChecks.Run(check);
        Console.WriteLine("Four shared-town profiles: independent progress, homes, world ownership, calendar, commits and migration passed");
    }
    private static GameState DeletionTown()
    {
        var s=new GameState(7319);Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);s.Town.GroundCount=0;
        for(int i=0;i<4;i++){
            s.SelectProfile(i);s.Name="Player"+i;s.Wallet=1000+i;s.Species=i;s.GardenTier=2;s.Savings=2000+i;
            if(!s.PlaceHouse(10+i*25,60,false))throw new Exception("Deletion fixture plot");
            s.Pockets[0]=10;if(!s.Place(0,1,1,0,BuildingWorld.PlayerRoom(i)))throw new Exception("Deletion fixture room");
            s.Pockets[0]=10;if(!s.Place(0,s.Town.HouseX+2,s.Town.HouseZ+7,0,0))throw new Exception("Deletion fixture garden");
            s.Pockets[0]=5+i;s.Owned[5+i]=1;s.Mail[0]=12;s.MailCount=1;s.PendingKind=1;s.PendingTier=2;s.PendingDay=100;
            s.Town.Residents[0].Friendship=20+i;
        }
        s.SelectProfile(0);s.Town.AddGround(17,8,90,0,0);s.Town.AddGround(2,9,90,2,0);s.Town.SetTerrain(150,150,Terrain.Beach);return s;
    }
    private static void DeleteChecks(Action<bool,string> check)
    {
        for(int target=0;target<4;target++){
            var s=DeletionTown();var tiles=(byte[])s.Town.Tiles.Clone();var elevation=(byte[])s.Town.Elevation.Clone();var before=SplitSaveCodec.Encode(s);
            s.DeleteProfile(target);var after=SplitSaveCodec.Encode(s);
            check(!s.Profiles[target].Created&&!s.HomePlaced(target)&&s.Town.PlacedCount==6,"deletion removes only the selected home and its interior/garden furniture");
            check(s.Town.Placed.Take(s.Town.PlacedCount).All(p=>p.Owner!=(target==0?0:target+6)&&BuildingWorld.RoomProfile(p.Room)!=target),"deleted owner/rooms have no furniture");
            check(s.Town.Tiles.SequenceEqual(tiles)&&s.Town.Elevation.SequenceEqual(elevation)&&s.Town.GroundCount==2,"deletion keeps edited terrain, loose items and plants");
            for(int i=0;i<4;i++)if(i!=target)check(before[3+i].SequenceEqual(after[3+i]),"every other profile record stays byte-exact");
            foreach(var copy in new[]{SaveCodec.Decode(SaveCodec.Encode(s)),SplitSaveCodec.Decode(after)})check(!copy.Profiles[target].Created&&copy.Profiles.Count(p=>p.Created)==3,"deletion survives both current codecs");
            s.SelectProfile(target);check(s.Name=="Newcomer"&&s.Wallet==0&&s.Savings==0&&!s.HousePlaced&&s.MailCount==0&&s.PendingKind==0,"reusing the deleted slot starts fresh");
        }
        var guests=DeletionTown();guests.SelectProfile(1);guests.Space=BuildingWorld.PlayerRoom(0);guests.X=2.5f;guests.Z=2.5f;guests.CaptureProfile();
        var npc=guests.Town.Residents[0];npc.Space=BuildingWorld.PlayerRoom(0);npc.PosX=npc.PosZ=2.5f;npc.JourneyBuilding=0;npc.JourneyPhase=3;
        guests.DeleteProfile(0);check(guests.Space==0&&guests.Profiles[1].Space==0&&guests.CanWalk(guests.X,guests.Z,0),"an offline/active visitor returns safely outside the removed home");
        check(npc.Space==0&&npc.JourneyBuilding==-1&&npc.JourneyPhase==0,"NPC visits and routes to a deleted home are cancelled");
        var disk=new Storage();var repo=new SaveRepository(disk);var original=DeletionTown();repo.Save(original);repo.Save(original);var frozen=SaveCodec.Encode(original);
        var removed=repo.DeleteProfile(original,0);check(SaveCodec.Encode(original).SequenceEqual(frozen),"durable deletion never mutates the displayed snapshot");
        check(removed.Sequence==original.Sequence+2&&!repo.Load()!.Profiles[0].Created,"deletion verifies both save banks");
        int latest=removed.Sequence%2;disk.Files[SplitSaveCodec.FileName(4,latest)][40]^=1;
        check(!repo.Load()!.Profiles[0].Created,"fallback to the other bank cannot resurrect a successfully deleted resident");
        foreach(bool afterWrite in new[]{false,true})for(int fail=1;fail<=16;fail++){
            var fault=new Storage();foreach(var f in new StorageCopy(original).Files)fault.Files[f.Key]=(byte[])f.Value.Clone();
            var store=new SaveRepository(fault);var expected=store.Load()!;byte[] source=SaveCodec.Encode(expected);fault.Writes=0;fault.FailAt=fail;fault.FailAfter=afterWrite;
            bool failed=false;try{store.DeleteProfile(expected,0);}catch(IOException){failed=true;}
            check(failed&&SaveCodec.Encode(expected).SequenceEqual(source),"interrupted deletion preserves its displayed source snapshot");
            var recovered=store.Load()!;check(recovered!=null&&recovered.Town.Tiles.SequenceEqual(expected.Town.Tiles),"every interrupted deletion loads a complete old or new town");
            for(int i=1;i<4;i++)check(recovered!.Profiles[i].Created&&recovered.Profiles[i].Name=="Player"+i&&recovered.Profiles[i].Wallet==1000+i,"interrupted deletion preserves every other villager");
        }
        var emptyDisk=new Storage();var emptyRepo=new SaveRepository(emptyDisk);var last=new GameState(91){Name="Last",Wallet=999};emptyRepo.Save(last);emptyRepo.Save(last);
        var empty=emptyRepo.DeleteProfile(last,0);check(empty.Profiles.All(p=>!p.Created)&&empty.ActiveProfile==0&&empty.Name=="Newcomer"&&empty.Wallet==0,"last deletion retains an empty shared town");
        check(SplitSaveCodec.Decode(SplitSaveCodec.Encode(empty)).Profiles.All(p=>!p.Created)&&SaveCodec.Decode(SaveCodec.Encode(empty)).Profiles.All(p=>!p.Created),"empty roster round-trips without creating a resident");
        foreach(Action encode in new Action[]{()=>SaveCodec.EncodeLegacy(empty),()=>SplitSaveCodec.EncodeLegacy(empty)}){bool refused=false;try{encode();}catch(InvalidOperationException){refused=true;}check(refused,"legacy export cannot recreate the last deleted villager");}
        var sameTown=empty.Town.Tiles;empty.SelectProfile(3);check(ReferenceEquals(sameTown,empty.Town.Tiles)&&empty.ActiveProfile==3&&empty.Profiles.Count(p=>p.Created)==1,"an empty town can start any new resident without regeneration");
        var changedDisk=new StorageCopy(original);var changedRepo=new SaveRepository(changedDisk);var stale=changedRepo.Load()!;var changed=changedRepo.Load()!;changed.Wallet++;changedRepo.Save(changed);int writes=changedDisk.Writes;
        bool rejected=false;try{changedRepo.DeleteProfile(stale,0);}catch(InvalidOperationException){rejected=true;}check(rejected&&changedDisk.Writes==writes,"a stale picker cannot overwrite a newer/different card snapshot");
        Console.WriteLine("Profile deletion: ownership, visitors, last resident, both banks, stale cards and 32 interrupted-write cases passed");
    }
    private sealed class StorageCopy : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();internal int Writes;
        internal StorageCopy(GameState state){var copy=SaveCodec.Decode(SaveCodec.Encode(state));copy.Sequence=0;var repo=new SaveRepository(this);repo.Save(copy);repo.Save(copy);}
        public byte[] Read(string name)=>(byte[])Files[name].Clone();
        public void Write(string name,byte[] data){Writes++;Files[name]=(byte[])data.Clone();}
    }
}
