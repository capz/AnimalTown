using AnimalTown;

internal static class OnboardingChecks
{
    private sealed class Storage : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal int Writes,FailAt;
        internal bool FailAfter;
        public byte[] Read(string name)=>(byte[])Files[name].Clone();
        public void Write(string name,byte[] bytes)
        {
            Writes++;
            if(Writes==FailAt&&!FailAfter)throw new IOException("Interrupted write");
            Files[name]=(byte[])bytes.Clone();
            if(Writes==FailAt&&FailAfter)throw new IOException("Interrupted after write");
        }
        internal Storage Copy()
        {var copy=new Storage();foreach(var file in Files)copy.Files[file.Key]=(byte[])file.Value.Clone();return copy;}
    }
    private static bool Refuses(Action action)
    {try{action();return false;}catch(InvalidOperationException){return true;}}
    private static bool Disposed(Action action)
    {try{action();return false;}catch(ObjectDisposedException){return true;}}
    private static GameState Town()
    {
        var s=new GameState(7319);Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);s.Town.GroundCount=0;
        s.Name="Fern";s.Species=4;s.Wallet=733;s.Savings=12100;s.Pockets[0]=11;s.Owned[11]=1;s.MailCount=1;s.Mail[0]=-18;
        s.Town.Residents[0].Friendship=37;s.Town.Residents[0].Request=1;s.Town.Residents[0].Met=1;
        if(!s.PlaceHouse(10,60,false))throw new Exception("Onboarding fixture home");
        s.GardenTier=1;s.Pockets[1]=10;if(!s.Place(1,12,65,0,0))throw new Exception("Onboarding fixture furniture");
        s.SelectProfile(1);s.Name="Resume";s.Species=3;s.Wallet=922;s.Pockets[0]=12;s.Owned[12]=1;s.Town.Residents[0].Friendship=19;
        s.SelectProfile(0);s.Town.AddGround(2,150,140,2,0);s.Town.SetTerrain(150,150,Terrain.Beach);
        s.Town.Elevation[150*AnimalTown.Town.Width+148]=TerrainElevation.Block;return s;
    }
    private static OnboardingDraft Ready(GameState source,bool savedTown=true,int profile=2)
    {
        var draft=new OnboardingDraft(source,profile,savedTown);
        if(!draft.SetName("Alex")||!draft.PlaceHome(60,80))throw new Exception("Onboarding fixture draft");
        draft.SetSpecies(5);return draft;
    }
    internal static void Run(Action<bool,string> check)
    {
        DraftChecks(check);PlotChecks(check);CommitChecks(check);FailureChecks(check);EmptyTownChecks(check);
        Console.WriteLine("Onboarding: private drafts, zero-allocation plot previews, discard, card guards, preserved profiles and 16 interrupted writes passed");
    }
    private static void DraftChecks(Action<bool,string> check)
    {
        var source=Town();byte[] baseline=SaveCodec.Encode(source);var draft=new OnboardingDraft(source,2,true);var s=draft.State;
        check(draft.IsNewProfile&&draft.Profile==2&&s.ActiveProfile==2&&s.Profiles[2].Created,"onboarding creates only a private selected villager");
        check(SaveCodec.Encode(source).SequenceEqual(baseline),"opening onboarding leaves the displayed town snapshot unchanged");
        check(ReferenceEquals(s.Town.Tiles,source.Town.Tiles)&&ReferenceEquals(s.Town.Elevation,source.Town.Elevation),"onboarding reuses saved terrain without regeneration or duplicate arrays");
        check(!ReferenceEquals(s.Town.Residents[0],source.Town.Residents[0])&&!ReferenceEquals(s.Profiles[0],source.Profiles[0])&&!ReferenceEquals(s.Town.Ground[0],source.Town.Ground[0])&&!ReferenceEquals(s.Town.Placed[0],source.Town.Placed[0]),"onboarding isolates mutable NPC, profile, plant and furniture records");
        foreach(string name in new[]{"","   ","12345678901234567","Alex\n","Alex_","Élan"})check(!draft.SetName(name),"onboarding rejects blank, oversized or unsupported keyboard names");
        check(draft.SetName(" Alex ")&&s.Name=="Alex"&&OnboardingDraft.ValidName("abcdefghijklmnop"),"onboarding accepts and trims supported names up to sixteen characters");
        draft.SetSpecies(5);bool badSpecies=false;try{draft.SetSpecies(6);}catch(ArgumentOutOfRangeException){badSpecies=true;}check(badSpecies&&s.Species==5,"appearance remains within the six supported species");
        check(draft.PlaceHome(60,80)&&s.HousePlaced&&s.Town.HouseX==60,"home selection changes only the draft");
        draft.ClearHome();check(!s.HousePlaced&&draft.PlaceHome(90,80),"review can remove and reposition an uncommitted home");
        var restored=draft.Restore();check(SaveCodec.Encode(restored).SequenceEqual(baseline)&&ReferenceEquals(restored.Town.Tiles,source.Town.Tiles),"discard restores the pre-selection snapshot without rerolling or allocating terrain");
        draft.Dispose();check(Disposed(()=>{_ = draft.State;})&&Disposed(()=>draft.Restore()),"discard releases access to the draft and encoded baseline");
        using var resume=new OnboardingDraft(source,1,true);
        check(!resume.IsNewProfile&&resume.State.Name=="Resume"&&resume.State.Species==3&&resume.State.Wallet==922&&resume.State.Pockets[0]==12&&resume.State.Town.Residents[0].Friendship==19,"an unfinished resident resumes existing identity, possessions and relationships");
        check(Refuses(()=>new OnboardingDraft(source,0,true)),"a completed resident never enters new-villager onboarding");
    }
    private static void PlotChecks(Action<bool,string> check)
    {
        var source=Town();using var draft=new OnboardingDraft(source,2,true);var s=draft.State;string reason;
        check(!draft.CanPlaceHome(0,80,out reason)&&reason.Length>0&&!draft.CanPlaceHome(146,80,out _),"home preview reserves the entire serializable fifteen-by-thirteen plot");
        check(!draft.CanPlaceHome(20,65,out reason)&&reason.Contains("another home"),"home preview explains overlap with another resident's reserved garden");
        int cell=80*AnimalTown.Town.Width+60;
        foreach(var kind in new[]{Terrain.Water,Terrain.Sea,Terrain.CoastWest}){
            s.Town.Tiles[cell]=(byte)kind;check(!draft.CanPlaceHome(60,80,out reason)&&reason.Contains("water"),"home preview rejects rivers, sea and mixed shore footprints");
        }
        s.Town.Tiles[cell]=(byte)Terrain.Grass;s.Town.Elevation[cell]=TerrainElevation.RampEast;
        check(!draft.CanPlaceHome(60,80,out reason)&&reason.Contains("level"),"home preview explains raised or sloped ground");
        s.Town.Elevation[cell]=0;s.Town.Tiles[cell]=(byte)Terrain.Plaza;
        check(!draft.CanPlaceHome(60,80,out reason)&&reason.Contains("grass"),"home preview preserves town paths and service surfaces");s.Town.Tiles[cell]=(byte)Terrain.Grass;
        int count=s.Town.GroundCount;s.Town.AddGround(2,62,81,2,0);
        check(!draft.CanPlaceHome(60,80,out reason)&&reason.Contains("plants"),"a cottage cannot cover an existing plant or loose item");s.Town.GroundCount=count;
        for(int row=4;row<13;row++)s.Town.AddGround(3,63,80+row,3,0);
        check(!draft.CanPlaceHome(60,80,out reason)&&reason.Contains("doorway"),"a fully tree-blocked entrance cannot trap a new resident");
        s.Town.GroundCount--;check(draft.CanPlaceHome(60,80,out _)&&draft.PlaceHome(60,80)&&s.CanWalk(s.X,s.Z,0),"a partially blocked garden keeps its trees and finds a clear arrival");
        draft.ClearHome();byte[] before=SaveCodec.Encode(s);int terrainVersion=s.Town.TerrainVersion,shadowVersion=s.Town.ShadowVersion;
        draft.CanPlaceHome(90,80,out _);long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)draft.CanPlaceHome(90,80,out _);
        long allocated=GC.GetAllocatedBytesForCurrentThread()-start;
        check(allocated==0,"per-frame plot validation allocates no managed objects");
        check(before.SequenceEqual(SaveCodec.Encode(s))&&s.Town.TerrainVersion==terrainVersion&&s.Town.ShadowVersion==shadowVersion,"plot previews do not mutate the draft, terrain or renderer revisions");
    }
    private static void CommitChecks(Action<bool,string> check)
    {
        var disk=new Storage();var repo=new SaveRepository(disk);var source=Town();repo.Save(source);repo.Save(source);byte[] baseline=SaveCodec.Encode(source);
        using var draft=Ready(source);int writes=disk.Writes;
        check(repo.Load()!.Profiles[2].Created==false&&disk.Writes==writes,"name, appearance and home choices remain unsaved until Save and begin");
        draft.Commit(repo,true);var saved=repo.Load()!;var after=SplitSaveCodec.Encode(saved);var s=draft.State;
        check(saved.ActiveProfile==2&&saved.Name=="Alex"&&saved.Species==5&&saved.HousePlaced&&saved.Town.HouseX==60&&saved.Town.HouseZ==80,"explicit commit verifies and reloads the selected resident and home");
        check(disk.Writes==writes+8&&s.Sequence==source.Sequence+1,"onboarding publishes one complete verified save transaction");
        check(SaveCodec.Encode(source).SequenceEqual(baseline),"committing a draft never mutates the original source graph");
        for(int profile=0;profile<4;profile++)if(profile!=2){source.Sequence=saved.Sequence;var sameSequence=SplitSaveCodec.Encode(source);check(sameSequence[3+profile].SequenceEqual(after[3+profile]),"joining a town preserves every other profile record byte for byte");}
        check(saved.Town.Tiles.SequenceEqual(source.Town.Tiles)&&saved.Town.Elevation.SequenceEqual(source.Town.Elevation)&&saved.Town.GroundCount==source.Town.GroundCount&&saved.Town.PlacedCount==source.Town.PlacedCount,"onboarding preserves edited land, plants and existing furnishings");
        writes=disk.Writes;draft.Commit(repo,true);check(writes==disk.Writes,"repeating a successful Save and begin does not duplicate the transaction");
        draft.Dispose();check(s.Name=="Alex"&&s.HousePlaced,"the live gameplay state survives releasing all draft-owned references");
        var staleDisk=new Storage();var staleRepo=new SaveRepository(staleDisk);var original=Town();staleRepo.Save(original);using var stale=Ready(original);
        var changed=staleRepo.Load()!;changed.Wallet++;staleRepo.Save(changed);writes=staleDisk.Writes;
        check(Refuses(()=>stale.Commit(staleRepo,true))&&staleDisk.Writes==writes,"a newer card snapshot cannot be overwritten by stale onboarding choices");
        var swappedDisk=new Storage();var swappedRepo=new SaveRepository(swappedDisk);var swapped=Town();swapped.Town.Seed++;swappedRepo.Save(swapped);writes=swappedDisk.Writes;
        check(Refuses(()=>stale.Commit(swappedRepo,true))&&swappedDisk.Writes==writes,"a different mounted town with the same sequence is refused");
        using var incomplete=new OnboardingDraft(original,2,true);writes=staleDisk.Writes;
        check(Refuses(()=>incomplete.Commit(staleRepo,true))&&staleDisk.Writes==writes,"Save and begin requires a selected home before touching storage");
    }
    private static void FailureChecks(Action<bool,string> check)
    {
        var source=Town();var initial=new Storage();var repo=new SaveRepository(initial);repo.Save(source);repo.Save(source);byte[] baseline=SaveCodec.Encode(source);
        foreach(bool afterWrite in new[]{false,true})for(int fail=1;fail<=8;fail++){
            var disk=initial.Copy();var store=new SaveRepository(disk);disk.FailAt=fail;disk.FailAfter=afterWrite;using var draft=Ready(source);
            bool failed=false;try{draft.Commit(store,true);}catch(IOException){failed=true;}
            check(failed&&draft.State.Name=="Alex"&&draft.State.Species==5&&draft.State.HousePlaced&&draft.State.Sequence==source.Sequence,"interrupted saving keeps the full draft visible and retryable");
            check(SaveCodec.Encode(source).SequenceEqual(baseline),"interrupted onboarding leaves the loaded source snapshot untouched");
            var recovered=store.Load()!;check(recovered!=null&&recovered.Profiles[0].Name=="Fern"&&recovered.Profiles[1].Name=="Resume"&&recovered.Town.Tiles.SequenceEqual(source.Town.Tiles),"all interrupted writes recover a complete town and preserve other residents");
            int writes=disk.Writes;disk.FailAt=0;draft.Commit(store,true);
            check(store.Load()!.Profiles[2].Name=="Alex"&&draft.State.HousePlaced,"saving can retry after every interruption including a durably written commit");
            if(afterWrite&&fail==8)check(disk.Writes==writes,"a retry recognizes an already persisted identical commit without rewriting it");
        }
    }
    private static void EmptyTownChecks(Action<bool,string> check)
    {
        var source=Town();source.DeleteProfile(1);source.DeleteProfile(0);
        var disk=new Storage();var repo=new SaveRepository(disk);repo.Save(source);repo.Save(source);
        using(var draft=Ready(source,true,3)){
            check(draft.State.Profiles.Count(p=>p.Created)==1&&draft.State.ActiveProfile==3,"an empty saved town can welcome its first resident into any slot");
            draft.Commit(repo,true);var saved=repo.Load()!;
            check(saved.ActiveProfile==3&&!saved.Profiles[0].Created&&saved.Profiles[3].Created&&saved.Town.Tiles.SequenceEqual(source.Town.Tiles),"onboarding preserves the empty slot-one save anchor and existing landscape");
        }
        var fresh=Town();fresh.DeleteProfile(1);fresh.DeleteProfile(0);using var first=Ready(fresh,false,0);var blankDisk=new Storage();var blankRepo=new SaveRepository(blankDisk);
        first.Commit(blankRepo,false);check(blankRepo.Load()!.Name=="Alex"&&blankDisk.Writes==8,"a genuinely blank card supports the explicit first-town save");
        var occupied=new Storage();var occupiedRepo=new SaveRepository(occupied);var other=Town();occupiedRepo.Save(other);int writes=occupied.Writes;
        using var unexpected=Ready(fresh,false,0);check(Refuses(()=>unexpected.Commit(occupiedRepo,true))&&occupied.Writes==writes,"a first save never overwrites an unexpected existing valid town");
        var damaged=new Storage();damaged.Files[SplitSaveCodec.FileName(0,0)]=new byte[]{1,2,3};var damagedRepo=new SaveRepository(damaged);
        check(Refuses(()=>unexpected.Commit(damagedRepo,true))&&damaged.Writes==0,"recognized but damaged town files cannot be overwritten as an empty card");
        using var removed=Ready(source,true,2);check(Refuses(()=>removed.Commit(new SaveRepository(new Storage()),false)),"a removed saved card must be reloaded instead of creating a replacement town");
    }
}
