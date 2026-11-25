using AnimalTown;

internal static class HomePlotChecks
{
    private const int X=60,Z=75;
    private sealed class Storage:ISaveStorage
    {
        private readonly Dictionary<string,byte[]> files=new();
        public byte[] Read(string name)=>(byte[])files[name].Clone();
        public void Write(string name,byte[] data)=>files[name]=(byte[])data.Clone();
    }
    private static GameState FlatTown()
    {
        var s=new GameState(7319);
        Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);
        s.Town.GroundCount=0;s.Town.PlacedCount=0;s.Town.Paths.Clear();
        s.Town.Day=100;s.Town.Month=100;s.Debt=0;s.HouseTier=4;
        return s;
    }
    private static void Ground(GameState s,int x,int z,int kind,int item=3)
    {if(!s.Town.AddGround(item,x,z,kind,s.Town.Day))throw new Exception("Home plot fixture ground");}
    private static bool At(GameState s,int x,int z,int kind)=>s.Town.FindGround(x,z,kind)>=0;
    internal static void Run(Action<bool,string> check)
    {
        CompactPlots(check);ProtectedContent(check);TreeClearing(check);Expansion(check);PendingConstruction(check);DraftTrees(check);Saplings(check);
        Console.WriteLine("Home plots: current-tier footprints, tree-only construction, safe entrances, pending expansion, saves and draft rollback passed");
    }
    private static void CompactPlots(Action<bool,string> check)
    {
        var s=FlatTown();s.Town.SetTerrain(X+7,Z,Terrain.Water);
        check(s.CanPlaceHouse(X,Z,out _)&&s.PlaceHouse(X,Z,false),"starter placement accepts7x5 beside terrain outside its actual footprint");
        check(s.InGarden(X+6,Z+4)&&!s.InGarden(X+7,Z+4),"starter garden owns only its current seven-by-five area");
        var close=FlatTown();check(close.PlaceHouse(X,Z,false),"first compact home");
        close.SelectProfile(1);check(close.PlaceHouse(X+7,Z,false),"another resident can use land outside the first current garden");
        close.SelectProfile(0);check(!close.CanExpandGarden(1,out _),"a later garden expansion respects the adjacent resident's real home");
        foreach(var loaded in new[]{SaveCodec.Decode(SaveCodec.Encode(close)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(close))})
            check(loaded.HomePlaced(0)&&loaded.HomePlaced(1)&&loaded.HomeX(1)==X+7,"compact adjacent homes survive both save formats");

        var edge=FlatTown();int ex=Town.Width-7,ez=Town.Height-7;
        check(edge.PlaceHouse(ex,ez,false)&&edge.CanWalk(edge.X,edge.Z,0),"a starter plot near the town edge keeps a safe exterior landing");
        foreach(var loaded in new[]{SaveCodec.Decode(SaveCodec.Encode(edge)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(edge))})
            check(loaded.Town.HouseX==ex&&loaded.Town.HouseZ==ez,"small edge plots round-trip without legacy maximum-footprint bounds");
        check(!edge.CanExpandGarden(1,out _),"expansion cannot extend beyond the town boundary");

        var relocation=FlatTown();relocation.GardenTier=1;check(relocation.PlaceHouse(X,Z,false),"expanded initial fixture");
        relocation.Pockets[0]=10;check(relocation.Place(0,X+5,Z+2,0,0),"existing garden furniture fixture");
        relocation.Town.SetTerrain(20+8,60,Terrain.Water);relocation.Wallet=99999;
        check(!relocation.PlaceHouse(20,60,true)&&relocation.Wallet==99999&&relocation.Town.Placed[0].X==X+5,"relocation validates the current upgraded garden and fails without moving furniture or charging");
        relocation.Town.SetTerrain(28,60,Terrain.Grass);
        check(relocation.PlaceHouse(20,60,true)&&relocation.GardenTier==1&&relocation.Town.Placed[0].X==25&&relocation.Wallet==49999,"successful relocation carries existing garden contents and charges once");
    }
    private static void ProtectedContent(Action<bool,string> check)
    {
        foreach(var terrain in new[]{Terrain.Water,Terrain.Sea,Terrain.Beach,Terrain.Cliff,Terrain.Building,Terrain.Plaza,Terrain.Camp,Terrain.Bridge,Terrain.CoastWest}){
            var s=FlatTown();Ground(s,X+2,Z+1,3);s.Town.SetTerrain(X+6,Z+4,terrain);byte[] before=SaveCodec.Encode(s);
            check(!s.PlaceHouse(X,Z,false)&&before.SequenceEqual(SaveCodec.Encode(s)),"invalid terrain rejects the entire transaction before removing a tree");
        }
        foreach(int kind in new[]{0,1,2}){
            var s=FlatTown();Ground(s,X+2,Z+1,3);Ground(s,X+3,Z+1,kind,kind==2?2:10);byte[] before=SaveCodec.Encode(s);
            check(!s.PlaceHouse(X,Z,false)&&before.SequenceEqual(SaveCodec.Encode(s)),"house construction never removes a loose item, buried item or planted flower");
        }
        var path=FlatTown();Ground(path,X+2,Z+1,3);path.Town.Paths.Set(X+6,Z+3,1);
        check(!path.PlaceHouse(X,Z,false)&&At(path,X+2,Z+1,3)&&path.Town.Paths.At(X+6,Z+3)==1,"house placement preserves an obstructing path and every tree on failure");
        var slope=FlatTown();slope.Town.Elevation[Z*Town.Width+X]=TerrainElevation.RampEast;
        check(!slope.PlaceHouse(X,Z,false)&&slope.Town.Elevation[Z*Town.Width+X]==TerrainElevation.RampEast,"house placement never levels terrain");
        var exit=FlatTown();exit.Town.SetTerrain(X+3,Z+5,Terrain.Water);
        check(!exit.PlaceHouse(X,Z,false),"a clear7x5 plot cannot strand its resident at the portal landing outside the fence");
        var structure=FlatTown();
        check(!structure.PlaceHouse(structure.Town.Koban.X,structure.Town.Koban.Z,false),"overlay structures remain placement blockers");
    }
    private static void TreeClearing(Action<bool,string> check)
    {
        var s=FlatTown();
        var removed=new[]{(X+2,Z+1),(X,Z+2),(X+1,Z+3),(X+3,Z+4),(X+3,Z+5)};
        foreach(var position in removed)Ground(s,position.Item1,position.Item2,3);
        Ground(s,X+5,Z+2,3);Ground(s,X+8,Z+2,3);
        Ground(s,X+1,Z+1,2,2);Ground(s,X+5,Z+3,0,10);Ground(s,X+1,Z+2,1,16);
        byte[] tiles=(byte[])s.Town.Tiles.Clone(),heights=(byte[])s.Town.Elevation.Clone(),paths=(byte[])s.Town.Paths.Packed.Clone();
        int count=s.Town.GroundCount,shadows=s.Town.ShadowVersion;
        check(s.CanPlaceHouse(X,Z,out _)&&s.Town.GroundCount==count&&s.Town.ShadowVersion==shadows,"preview accepts removable trees without touching the world");
        check(s.PlaceHouse(X,Z,false),"confirmed home placement clears trees obstructing cottage, fence, mailbox and doorway");
        foreach(var position in removed)check(!At(s,position.Item1,position.Item2,3),"only each obstructing construction tree is removed");
        check(At(s,X+5,Z+2,3)&&At(s,X+8,Z+2,3),"decorative interior trees and trees outside the plot survive");
        check(At(s,X+1,Z+1,2)&&At(s,X+5,Z+3,0)&&At(s,X+1,Z+2,1),"flowers, dropped items and buried items outside construction cells remain intact");
        check(tiles.SequenceEqual(s.Town.Tiles)&&heights.SequenceEqual(s.Town.Elevation)&&paths.SequenceEqual(s.Town.Paths.Packed),"tree clearing never changes terrain, elevation or paths");
        check(s.CanWalk(X+3.5f,Z+4.5f,0)&&s.CanWalk(X+3.5f,Z+5.5f,0)&&s.Town.ShadowVersion>shadows,"cleared entrance and exit are walkable and tree rendering is invalidated");
    }
    private static void Expansion(Action<bool,string> check)
    {
        foreach(var terrain in new[]{Terrain.Water,Terrain.Building,Terrain.Cliff,Terrain.Beach,Terrain.Plaza}){
            var s=FlatTown();check(s.PlaceHouse(X,Z,false),"expansion terrain fixture");Ground(s,X+7,Z+1,3);s.Town.SetTerrain(X+8,Z+6,terrain);byte[] before=SaveCodec.Encode(s);
            check(!s.Expand(true,100,out string reason)&&reason.Length>0&&before.SequenceEqual(SaveCodec.Encode(s)),"blocked expansion gives a reason without charging, queuing or clearing trees");
            check(!s.Expand(true,100,out _,true),"free construction still respects terrain and structures");
        }
        var path=FlatTown();path.PlaceHouse(X,Z,false);path.Town.Paths.Set(X+8,Z+2,1);
        check(!path.Expand(true,100,out _)&&path.Town.Paths.At(X+8,Z+2)==1,"garden expansion never erases an existing path in the additional land");
        var s1=FlatTown();check(s1.PlaceHouse(X,Z,false),"garden growth fixture");Ground(s1,X+7,Z+1,3);Ground(s1,X+9,Z+1,3);Ground(s1,X+5,Z+2,3);
        Ground(s1,X+7,Z+2,2,2);Ground(s1,X+7,Z+3,1,16);s1.Town.Paths.Set(X+5,Z+3,1);
        check(s1.Expand(true,100,out _)&&s1.PendingKind==2&&s1.GardenTier==0&&s1.Debt==GameState.GardenPrices[1],"a valid expansion queues exactly one normal garden loan");
        check(At(s1,X+7,Z+1,3),"ordering construction leaves its trees in place until completion");
        s1.Rollover(100,100);check(s1.GardenTier==0,"garden expansion does not complete before its due date");
        s1.Rollover(101,100);
        check(s1.GardenTier==1&&s1.PendingKind==0&&!At(s1,X+7,Z+1,3),"overnight garden expansion acquires the additional land and removes its trees");
        check(At(s1,X+9,Z+1,3)&&At(s1,X+5,Z+2,3)&&At(s1,X+7,Z+2,2)&&At(s1,X+7,Z+3,1),"garden growth retains outside and existing interior trees, flowers and buried belongings");
        check(s1.Town.Paths.At(X+5,Z+3)==1,"an existing path inside the owned garden survives expansion");
    }
    private static void PendingConstruction(Action<bool,string> check)
    {
        var s=FlatTown();s.PlaceHouse(X,Z,false);check(s.Expand(true,100,out _),"pending expansion fixture");
        s.SelectProfile(1);s.Name="Neighbour";s.Wallet=712;
        check(!s.CanPlaceHouse(X+7,Z,out _),"accepted pending garden land cannot be claimed by another home");
        var loaded=SplitSaveCodec.Decode(SplitSaveCodec.Encode(s));
        check(!loaded.CanPlaceHouse(X+7,Z,out _),"pending construction reservations survive reload");
        loaded.Rollover(101,100);
        check(loaded.ActiveProfile==1&&loaded.Name=="Neighbour"&&loaded.Wallet==712&&loaded.Profiles[0].GardenTier==1&&loaded.Profiles[0].PendingKind==0,"an inactive resident's garden completes without replacing the active profile");

        var blocked=FlatTown();blocked.PlaceHouse(X,Z,false);Ground(blocked,X+7,Z+1,3);blocked.Expand(true,100,out _);
        long debt=blocked.Debt;blocked.Town.SetTerrain(X+8,Z+6,Terrain.Building);blocked.Rollover(101,100);
        check(blocked.GardenTier==0&&blocked.PendingKind==2&&blocked.Debt==debt&&At(blocked,X+7,Z+1,3),"a late construction blocker defers the job without removing trees or charging again");
        foreach(var saved in new[]{SaveCodec.Decode(SaveCodec.Encode(blocked)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(blocked))})
            check(saved.PendingKind==2&&saved.GardenTier==0&&saved.Town.At(X+8,Z+6)==(int)Terrain.Building,"a deferred construction job remains saveable with its obstacle intact");
        blocked.Town.SetTerrain(X+8,Z+6,Terrain.Grass);blocked.Rollover(102,100);
        check(blocked.GardenTier==1&&blocked.PendingKind==0&&blocked.Debt==debt&&!At(blocked,X+7,Z+1,3),"construction completes later once the blocker is removed without another loan");
    }
    private static void DraftTrees(Action<bool,string> check)
    {
        var source=FlatTown();Ground(source,X+2,Z+1,3);Ground(source,92,81,3);
        Ground(source,110,100,0,10);var visitor=FlatTown();int custodyIndex=source.Town.GroundCount-1;
        check(TownItemActions.RemoveGround(source,visitor,TownItemActor.Visitor(source,visitor),custodyIndex),"draft rollback fixture puts a receipt-bearing custody item last in the ground table");
        int recovery=source.Town.Ground[custodyIndex].RecoveryId;int[] receipts=source.Town.LostAndFound.Encode();
        var originalGround=source.Town.Ground.Take(source.Town.GroundCount).Select(g=>(g.Item,g.X,g.Z,g.Kind,g.Owner,g.Day,g.Wrapped,g.RecoveryId)).ToArray();
        byte[] before=SaveCodec.Encode(source);
        using(var draft=new OnboardingDraft(source,0,false)){
            check(draft.CanPlaceHome(X,Z,out _)&&At(draft.State,X+2,Z+1,3),"onboarding preview does not clear a construction tree");
            check(draft.PlaceHome(X,Z)&&!At(draft.State,X+2,Z+1,3)&&At(source,X+2,Z+1,3),"choosing a home clears trees only in the private draft");
            draft.ClearHome();check(At(draft.State,X+2,Z+1,3),"reviewing the home restores trees removed for the earlier candidate");
            check(draft.State.Town.LostAndFound.FindRecovery(recovery)==custodyIndex&&receipts.SequenceEqual(draft.State.Town.LostAndFound.Encode())&&originalGround.SequenceEqual(draft.State.Town.Ground.Take(draft.State.Town.GroundCount).Select(g=>(g.Item,g.X,g.Z,g.Kind,g.Owner,g.Day,g.Wrapped,g.RecoveryId))),"review restores the exact ground ordering and custody receipt table after removing an earlier tree");
            check(draft.PlaceHome(90,80)&&At(draft.State,X+2,Z+1,3)&&!At(draft.State,92,81,3),"repositioning removes only trees at the final selected home");
            draft.ClearHome();check(receipts.SequenceEqual(draft.State.Town.LostAndFound.Encode())&&draft.State.Town.LostAndFound.FindRecovery(recovery)==custodyIndex,"repeated repositioning preserves custody receipt indices");
            check(before.SequenceEqual(SaveCodec.Encode(source))&&before.SequenceEqual(SaveCodec.Encode(draft.Restore())),"discard restores all original trees and leaves the source snapshot byte-exact");
        }
        var repo=new SaveRepository(new Storage());
        using(var draft=new OnboardingDraft(source,0,false)){
            check(draft.PlaceHome(X,Z),"tree-clearing save fixture chooses a home");draft.Commit(repo,false,Array.Empty<string>());
            var saved=repo.Load()!;int recovered=saved.Town.LostAndFound.FindRecovery(recovery);
            check(saved.HousePlaced&&!At(saved,X+2,Z+1,3)&&At(saved,92,81,3)&&recovered>=0&&saved.Town.Ground[recovered].Item==10,"final onboarding commit persists only the confirmed tree clearing and retains shifted custody receipts");
            check(before.SequenceEqual(SaveCodec.Encode(source)),"saving confirmed tree clearing leaves the original source graph untouched");
        }
    }
    private static void Saplings(Action<bool,string> check)
    {
        var source=FlatTown();Ground(source,X+2,Z+1,2,3);Ground(source,X+1,Z+1,2,2);Ground(source,X+8,Z+1,2,3);
        using(var draft=new OnboardingDraft(source,0,false)){
            check(draft.PlaceHome(X,Z)&&!At(draft.State,X+2,Z+1,2)&&At(draft.State,X+1,Z+1,2)&&At(draft.State,X+8,Z+1,2),"construction clears obstructing saplings while preserving flowers and trees outside its footprint");
            draft.ClearHome();check(At(draft.State,X+2,Z+1,2)&&At(source,X+2,Z+1,2),"review restores a removed sapling in both its original kind and position");
        }
        source.PlaceHouse(X,Z,false);source.Expand(true,100,out _);source.Rollover(101,100);
        check(source.GardenTier==1&&!At(source,X+8,Z+1,2)&&At(source,X+1,Z+1,2),"garden construction removes newly covered tree saplings without clearing existing flowers");
    }
}
