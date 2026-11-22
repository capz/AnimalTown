using AnimalTown;

internal static class BuildingKobanChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(2204){HousePlaced=true};var t=s.Town;
        check(BuildingWorld.Count==13&&BuildingWorld.Actors==7&&BuildingWorld.Space(KobanSite.Building)==26&&BuildingWorld.Building(26)==12,"koban adds a stable building/space without increasing the actor pool");
        check(s.BuildingTypes[KobanSite.Building]==2&&BuildingWorld.Role(12)==BuildingRole.Koban&&BuildingWorld.Size(s,26)==6,"new towns select the original six-metre koban kit");
        check(new BuildingPrompt().Enter(s,12)=="Enter Koban"&&BuildingWorld.OwnerName(s,12)=="","the public koban has its own entry prompt");
        check(!s.CanWalk(t.Koban.X+1.5f,t.Koban.Z+1.5f,0)&&t.At(t.Koban.X,t.Koban.Z)==(int)Terrain.Building,"koban footprint blocks movement and plot placement");
        BuildingWorld.Entrance(s,12,out s.X,out s.Z);s.Facing=(float)Math.PI;
        var doors=new PortalSystem();check(doors.Nearby(s,0,s.Facing)==12&&doors.Request(s,0,12),"the koban uses the normal discoverable exterior doorway");
        for(int i=0;i<80;i++)doors.Tick(s);
        check(s.Space==26&&BuildingWorld.CanStand(s,26,s.X,s.Z)&&!KobanSite.InReach(s),"entry commits to a clear interior landing without opening the counter remotely");
        check(BuildingWorld.FixtureCount(s,26)==4&&!BuildingWorld.CanStand(s,26,1,1.8f)&&!BuildingWorld.CanStand(s,26,4.5f,.7f),"counter and lost-property shelves share fixture collision");
        s.X=1.45f;s.Z=2.95f;check(KobanSite.InReach(s)&&BuildingWorld.CanStand(s,26,s.X,s.Z),"counter approach is walkable and opens lost property");
        s.X=4.65f;s.Z=1.65f;check(KobanSite.InReach(s)&&BuildingWorld.CanStand(s,26,s.X,s.Z),"shelf approach is walkable and opens the same custody store");
        foreach(var loaded in new[]{SaveCodec.Decode(SaveCodec.Encode(s)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(s))})
            check(loaded.Space==26&&loaded.BuildingTypes[12]==2&&loaded.Town.Koban.X==t.Koban.X&&loaded.Town.Koban.Z==t.Koban.Z,"both current save formats retain an occupied koban and its fixed plot");
        BuildingWorld.InsideDoor(s,26,out s.X,out s.Z);check(doors.Request(s,0,12),"the koban door also exits");
        for(int i=0;i<80;i++)doors.Tick(s);
        check(s.Space==0&&s.CanWalk(s.X,s.Z,0),"the exterior exit remains walkable");
        var fields=BuildingSave.Encode(s);var previous=new int[BuildingSave.ProfileFieldCount];
        Array.Copy(fields,previous,2+BuildingSave.ProfileBuildingCount);Array.Copy(fields,2+BuildingWorld.Count,previous,2+BuildingSave.ProfileBuildingCount,42);
        var older=new GameState(2204);BuildingSave.Decode(older,previous);
        check(older.BuildingTypes[12]==2&&older.BuildingTypes.Take(12).SequenceEqual(s.BuildingTypes.Take(12)),"old twelve-building records retain their types and gain the koban default");
        LegacyPlot(check);
        LegacyExitLanding(check);
        VisitingMigration(check);
        Console.WriteLine("Koban identity, portals, fixture collision, legacy plots and save compatibility checks passed");
    }
    private static void LegacyPlot(Action<bool,string> check)
    {
        var old=new GameState(2205);var t=old.Town;t.GroundCount=1;
        var item=t.Ground[0];item.Item=10;item.X=KobanSite.PreferredX;item.Z=KobanSite.PreferredZ;item.Kind=0;item.Owner=0;
        t.Paths.Set(KobanSite.PreferredX+1,KobanSite.PreferredZ,1);
        byte[] tiles=(byte[])t.Tiles.Clone(),paths=(byte[])t.Paths.Packed.Clone();
        foreach(var loaded in new[]{SaveCodec.Decode(SaveCodec.EncodeLegacy(old)),SplitSaveCodec.Decode(SplitSaveCodec.EncodeLegacy(old))}){
            var migrated=loaded.Town;
            check(migrated.Koban.Enabled&&!migrated.Koban.Contains(item.X,item.Z)&&migrated.GroundCount==1&&migrated.Ground[0].Item==10,"legacy migration finds a clear plot without taking an existing item");
            check(migrated.Tiles.SequenceEqual(tiles),"adding the koban never rewrites legacy terrain");
            var reloaded=SaveCodec.Decode(SaveCodec.Encode(loaded));
            check(reloaded.Town.Koban.X==migrated.Koban.X&&reloaded.Town.Koban.Z==migrated.Koban.Z,"the migrated service plot is persisted instead of following future item changes");
            bool rejected=false;try{SaveCodec.EncodeLegacy(loaded);}catch(InvalidOperationException){rejected=true;}
            check(rejected,"legacy export cannot silently discard a relocated koban plot");
        }
        check(t.Tiles.SequenceEqual(tiles)&&t.Paths.Packed.SequenceEqual(paths)&&item.Kind==0,"migration leaves the live source snapshot and its belongings untouched");
        old.Town.Ground[0].Kind=4;
        check(old.Town.FindGround(item.X,item.Z)==-1,"custody coordinates never appear as a world obstruction");
    }
    private static void LegacyExitLanding(Action<bool,string> check)
    {
        var old=new GameState(2206);old.Town.GroundCount=0;
        int treeX=KobanSite.PreferredX+1,treeZ=KobanSite.PreferredZ+4;
        check(old.Town.AddGround(Catalog.CherrySapling,treeX,treeZ,3,12),"legacy fixture obstructs the final exit cell beyond a clear doorway approach");
        foreach(var loaded in new[]{SaveCodec.Decode(SaveCodec.EncodeLegacy(old)),SplitSaveCodec.Decode(SplitSaveCodec.EncodeLegacy(old))}){
            var t=loaded.Town;
            check(t.Koban.Enabled&&(t.Koban.X!=KobanSite.PreferredX||t.Koban.Z!=KobanSite.PreferredZ)&&t.FindGround(treeX,treeZ,3)>=0,"migration avoids an obstructed exit landing without removing the old tree");
            BuildingWorld.Entrance(loaded,KobanSite.Building,out loaded.X,out loaded.Z);
            var doors=new PortalSystem();check(doors.Request(loaded,0,KobanSite.Building),"the migrated service has an accessible entrance");
            for(int i=0;i<80;i++)doors.Tick(loaded);
            check(loaded.Space==KobanSite.Room,"the migrated doorway admits the player");
            BuildingWorld.InsideDoor(loaded,KobanSite.Room,out loaded.X,out loaded.Z);
            check(doors.Request(loaded,0,KobanSite.Building),"the player can request an exit from the migrated service");
            for(int i=0;i<PortalSystem.MaximumWait+PortalSystem.Duration;i++)doors.Tick(loaded);
            check(loaded.Space==0&&loaded.CanWalk(loaded.X,loaded.Z,0),"the migrated service completes its exit instead of stranding the player inside");
        }
    }
    private static void VisitingMigration(Action<bool,string> check)
    {
        var state=new GameState(2207){Name="Local",Wallet=333};state.Town.GroundCount=0;
        state.CaptureProfile();state.Town.Koban.Restore(-1,-1);
        var guest=new VillagerProfile{Created=true,Name="Visitor",Wallet=777,Space=0,X=KobanSite.PreferredX+1.5f,Z=KobanSite.PreferredZ+1.5f};
        state.Town.Jobs.GuestProfile=guest;state.Town.Jobs.ActiveVisitor=true;
        // Saved visitors are activated after service migration by both decoders.
        // At this point the active body still belongs to the local save anchor.
        state.Town.Koban.Migrate(state);
        check(state.ActiveProfile==0&&state.Town.Koban.Enabled&&!state.Town.Koban.Contains((int)guest.X,(int)guest.Z),"migration reserves the saved guest's position while the local anchor is active");
        state.ActivateSavedVisitor();
        check(state.Visiting&&state.Name=="Visitor"&&state.Wallet==777&&state.X==guest.X&&state.Z==guest.Z&&state.CanWalk(state.X,state.Z,0),"activating a migrated visitor preserves identity and a walkable position");
        check(state.Profiles[0].Name=="Local"&&state.Profiles[0].Wallet==333,"guest-safe service migration leaves the resident save anchor unchanged");
    }
}
