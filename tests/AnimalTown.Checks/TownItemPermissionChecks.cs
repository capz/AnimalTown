using AnimalTown;
using AnimalTown.Jobs;

internal static class TownItemPermissionChecks
{
    private sealed class Disk:ISaveStorage
    {
        private readonly Dictionary<string,byte[]> files=new();
        internal Action? BeforeWrite,AfterWrite;
        public byte[] Read(string name)=>files.TryGetValue(name,out var bytes)?(byte[])bytes.Clone():throw new IOException("Missing save");
        public void Write(string name,byte[] bytes){BeforeWrite?.Invoke();files[name]=(byte[])bytes.Clone();AfterWrite?.Invoke();}
        internal Disk Copy(){var copy=new Disk();foreach(var file in files)copy.files[file.Key]=(byte[])file.Value.Clone();return copy;}
    }
    private static GameState Empty(int seed)
    {
        var s=new GameState(seed){Wallet=20000,HousePlaced=true,GardenTier=1};Array.Clear(s.Pockets);Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);
        s.Town.GroundCount=s.Town.PlacedCount=0;s.Town.Day=100;s.Town.Month=100;return s;
    }
    private static bool Rejects(Action action){try{action();return false;}catch(InvalidOperationException){return true;}}
    internal static void Run(Action<bool,string> check)
    {
        Permissions(check);Recovery(check);Resources(check);FullStore(check);Persistence(check);VisitorGifts(check);OverflowReturn(check);
    }
    private static void Permissions(Action<bool,string> check)
    {
        var host=Empty(7401);var guest=Empty(7402);var visitor=TownItemActor.Visitor(host,guest);var resident=TownItemActor.LocalResident(host);
        guest.Pockets[0]=10;int wallet=guest.Wallet;
        check(TownItemActions.AddGround(host,guest,visitor,0,32,40,wrapped:true)&&guest.Pockets[0]==0&&guest.Wallet==wallet&&host.Find(10)<0,"guest addition consumes only the guest's item");
        int index=host.Town.FindGround(32,40);
        check(!TownItemActions.AddGround(host,guest,visitor,0,33,40)&&host.Town.GroundCount==1,"repeated addition cannot recreate a consumed pocket item");
        check(TownItemActions.MoveGround(host,guest,visitor,index,33,40)&&host.Town.Ground[index].Wrapped,"guest can move an existing item without acquiring or unwrapping it");
        check(TownItemActions.RemoveGround(host,guest,visitor,index)&&host.Town.Ground[index].Kind==4&&host.Town.Ground[index].Wrapped&&guest.Find(10)<0,"guest removal puts the exact wrapped item into police custody");
        int id=host.Town.Ground[index].RecoveryId;
        check(id>0&&!TownItemActions.RemoveGround(host,guest,visitor,index)&&host.Town.GroundCount==1,"repeated removal cannot create a second custody item");
        check(host.Town.FindGround(33,40)<0&&host.Town.FindGround(33,40,4)==index,"custody no longer occupies the original world tile");
        check(!TownItemActions.ClaimLost(host,guest,visitor,index)&&!TownItemActions.ClaimLost(host,host,TownItemActor.Npc(host),index)&&!TownItemActions.ClaimLost(host,host,default,index),"visitors, NPCs and absent authority cannot claim custody");
        check(!TownItemActions.ClaimLost(host,guest,TownItemActor.LocalResident(guest),index)&&!TownItemActions.ClaimLost(host,host,TownItemActor.Visitor(host,host),index),"a foreign resident identity or forged same-state visitor does not gain local authority");
        host.SelectProfile(1);Array.Fill(host.Pockets,1);
        check(!TownItemActions.ClaimLost(host,host,resident,index)&&!host.ClaimLost(index,0),"resident authority cannot survive active-slot switching or impersonate another slot");
        check(!host.ClaimLost(index,1)&&host.Town.Ground[index].RecoveryId==id,"full resident pockets leave custody and recovery metadata intact");
        host.Pockets[0]=0;
        check(host.ClaimLost(index,1)&&host.Pockets[0]==10&&host.Town.GroundCount==0,"any active created human resident can claim immediately into their own pockets");
        check(!TownItemActions.RestoreRecovery(host,host,TownItemActor.LocalResident(host),id),"claimed items cannot also be restored into the world");
        host.Profiles[1].Created=false;
        host.Town.AddGround(10,34,40,4,100);
        check(!host.ClaimLost(0,1),"an empty resident save slot cannot claim police property");
        host.Profiles[1].Created=true;
        guest.Pockets[0]=Catalog.CherrySapling;
        check(TownItemActions.AddGround(host,guest,visitor,0,36,40,2)&&host.Town.Ground[1].Owner==1,"visitor planting assigns a town resident owner instead of a foreign slot");
        guest.Pockets[0]=10;
        check(!TownItemActions.AddGround(host,guest,visitor,0,37,40,1)&&!TownItemActions.AddGround(host,guest,visitor,0,37,40,3)&&guest.Pockets[0]==10,"addition validates buryability and never creates mature trees from a request kind");
        host.Town.Stock[0]=11;var stockBefore=host.Town.Stock[0];int price=Catalog.Items[stockBefore].Buy;
        Array.Clear(guest.Pockets);wallet=guest.Wallet;
        check(host.Town.Shop.Buy(host,guest,ShopSource.Display,0)&&guest.Pockets[0]==stockBefore&&guest.Wallet==wallet-price&&host.Town.Stock[0]==0,"final guest shop purchases remain the paid acquisition exception");
        check(!host.Town.Shop.Buy(host,guest,ShopSource.Display,0)&&guest.Pockets.Count(i=>i==stockBefore)==1,"a paid finite purchase cannot be delivered twice");
    }
    private static void Recovery(Action<bool,string> check)
    {
        var host=Empty(7411);var guest=Empty(7412);var actor=TownItemActor.Visitor(host,guest);var local=TownItemActor.LocalResident(host);
        host.Town.AddGround(Catalog.Apple,32,40,2,91,true);int index=host.Town.FindGround(32,40);
        check(TownItemActions.RemoveGround(host,guest,actor,index),"visitor plant removal is recoverable");int id=host.Town.Ground[index].RecoveryId;
        guest.Pockets[0]=10;check(TownItemActions.AddGround(host,guest,actor,0,32,40),"a recovered object's old tile can hold a new object");
        check(!TownItemActions.RestoreRecovery(host,host,local,id)&&host.Town.Ground[index].RecoveryId==id,"restoration refuses to overwrite a later conflicting item");
        check(TownItemActions.MoveGround(host,guest,actor,host.Town.FindGround(32,40),33,40)&&TownItemActions.RestoreRecovery(host,host,local,id),"clearing the conflict permits atomic restoration");
        var restored=host.Town.Ground[host.Town.FindGround(32,40)];
        check(restored.Item==Catalog.Apple&&restored.Kind==2&&restored.Day==91&&restored.Wrapped&&restored.RecoveryId==0&&!TownItemActions.RestoreRecovery(host,host,local,id),"restore preserves original plant metadata and consumes the exact receipt once");
        host.Pockets[0]=10;check(host.Place(0,13,26,0,0),"resident furniture fixture placed");
        check(TownItemActions.MovePlacement(host,guest,actor,0,13,27,1,0),"visitor can move and rotate town furniture without pocketing it");
        check(TownItemActions.RemovePlacement(host,guest,actor,0)&&host.Town.PlacedCount==0,"removed furniture is escrowed before its world entry disappears");
        int lost=host.Town.LostAndFound.IndexAt(0),furnitureId=host.Town.Ground[lost].RecoveryId;
        host.Town.AddGround(17,13,27,0,100);
        check(!TownItemActions.RestoreRecovery(host,host,local,furnitureId)&&host.Town.Ground[lost].RecoveryId==furnitureId,"furniture restoration also respects later ground possessions in its footprint");
        host.Town.RemoveGround(host.Town.FindGround(13,27));
        check(TownItemActions.RestoreRecovery(host,host,local,furnitureId)&&host.Town.PlacedCount==1&&host.Town.Placed[0].X==13&&host.Town.Placed[0].Z==27&&host.Town.Placed[0].Rotation==1&&host.Town.Placed[0].Owner==0,"furniture restoration preserves room, position, rotation and owner");
        check(host.Town.LostAndFound.FindRecovery(furnitureId)<0&&!TownItemActions.RestoreRecovery(host,host,local,furnitureId),"restored furniture has no remaining claimable duplicate");
        check(TownItemActions.RemoveGround(host,guest,actor,host.Town.FindGround(33,40)),"second custody record allocated");
        int last=host.Town.Ground[host.Town.LostAndFound.IndexAt(0)].RecoveryId;
        check(last>furnitureId&&furnitureId>id,"recovery identities are never reused after claim or restore");
    }
    private static void Resources(Action<bool,string> check)
    {
        var state=Empty(7421);state.Town.Jobs.GuestProfile=new VillagerProfile{Created=true,Tool=Catalog.Shovel};state.Town.Jobs.ActiveVisitor=true;state.RestoreProfile(GameState.ProfileCount);Array.Clear(state.Pockets);
        state.Town.AddGround(16,32,40,1,100);
        check(TownItemActions.Dig(state,0)&&state.Find(16)<0&&state.Town.Ground[0].Kind==4,"offline-card visitor digging also sends the find to custody");
        check(!state.ClaimLost(0,0)&&!state.ClaimLost(0,GameState.ProfileCount),"offline visitor cannot impersonate a resident or use slot four to claim");
        state.Town.AddGround(Catalog.CherrySapling,34,40,3,80);int tree=state.Town.FindGround(34,40);state.Tool=Catalog.Axe;
        check(TownItemActions.Chop(state,tree)&&state.Town.Ground[tree].Kind==4&&state.Town.Ground[tree].Item==Catalog.CherrySapling&&state.Town.Ground[tree].Day==80,"chopped tree keeps its exact sapling species and original date in custody");
        state.Town.AddGround(Catalog.Pear,35,40,3,90);tree=state.Town.FindGround(35,40);int count=state.Town.GroundCount;
        check(TownItemActions.Harvest(state,tree)&&state.Town.Ground[tree].Kind==3&&state.Town.GroundCount==count+1&&state.Find(Catalog.Pear)<0,"visitor harvest keeps the tree and sends its native fruit yield into custody");
        state.Tool=Catalog.FishingRod;check(TownItemActions.Catch(state,14)&&state.Find(14)<0,"visitor fishing cannot export town resources");
        state.Tool=Catalog.BugNet;check(TownItemActions.Catch(state,15)&&state.Find(15)<0,"visitor bug catching cannot export town resources");
        check(!TownItemActions.Catch(state,10),"ambient resource helper cannot grant arbitrary catalog items");
        state.Town.AddGround(17,36,40,0,100);
        check(state.Pickup(36,40)&&state.Find(17)<0&&state.Town.FindGround(36,40)<0,"existing visitor pickup entry point recovers instead of granting or respawning");
        state.Pockets[0]=2;
        check(state.Use(0,1,37,40)&&state.Town.Ground[state.Town.FindGround(37,40)].Owner<GameState.ProfileCount,"offline visitor additions stay within the persisted local-owner range");
    }
    private static void FullStore(Action<bool,string> check)
    {
        var host=Empty(7431);var guest=Empty(7432);var actor=TownItemActor.Visitor(host,guest);host.Pockets[0]=10;check(host.Place(0,13,26,0,0),"full-store furniture fixture placed");
        for(int i=0;i<host.Town.Ground.Length;i++){var g=host.Town.Ground[i];g.Item=10;g.Kind=4;g.X=g.Z=g.Owner=0;g.Day=100;}host.Town.GroundCount=host.Town.Ground.Length;
        check(!TownItemActions.RemovePlacement(host,guest,actor,0)&&host.Town.PlacedCount==1&&host.Town.GroundCount==512,"full custody never loses the removed furnishing");
        guest.Tool=Catalog.FishingRod;int pockets=guest.Pockets.Sum();
        check(!TownItemActions.Catch(host,guest,actor,14)&&guest.Pockets.Sum()==pockets&&host.Town.GroundCount==512,"full custody rejects an ambient yield without granting it to the visitor");
        var tree=host.Town.Ground[511];tree.Item=Catalog.Pear;tree.Kind=3;tree.X=35;tree.Z=40;
        check(!TownItemActions.Harvest(host,guest,actor,511)&&tree.Kind==3&&tree.Item==Catalog.Pear,"full custody cannot consume or alter a tree while failing to store its yield");
        check(TownItemActions.RemoveGround(host,guest,actor,511)&&tree.Kind==4&&tree.Item==Catalog.Pear&&host.Town.GroundCount==512,"in-place recovery safely reuses the removed ground item's existing slot even at capacity");
    }
    private static JobIdentity Owed(GameState state,int item,bool reply=false)
    {
        var ledger=state.Town.Jobs;var id=ledger.Allocate();
        ledger.Rewards[ledger.FreeReward()]=new VillagerJobReward{Id=id,Job=ledger.Allocate(),Owner=ledger.Player(state.ActiveProfile),Giver=ledger.Npc(0),State=JobRewardState.Owed,Item=item,Category=JobContent.CategoryOf(item),ResolvedAt=JobCalendar.At(2026,6,10,12),IsReply=reply};return id;
    }
    private static JobCommand CollectCommand(GameState state,JobIdentity id)
    {
        var service=state.ResidentJobs;service.EndConversation();var npc=state.Town.Residents[0];state.Space=npc.Space;state.X=npc.PosX;state.Z=npc.PosZ;
        if(!service.BeginConversation(0,out _))throw new Exception("Custody reward conversation fixture");return service.Command(JobCommandKind.Collect,id);
    }
    private static void VisitorGifts(Action<bool,string> check)
    {
        long now=JobCalendar.At(2026,6,10,12);var home=Empty(7451);var destination=Empty(7452);home.Tutorial=destination.Tutorial=7;
        home.ResidentJobs.Enable(7451001,now);destination.ResidentJobs.Enable(7452001,now);home.Pockets[0]=Catalog.PocketCamera;
        var homeReward=Owed(home,Catalog.LeafShirt);var first=new SaveRepository(new Disk());var second=new SaveRepository(new Disk());first.Save(home);second.Save(destination);
        var travel=new OfflineTownTravel(first,second);var guest=travel.Depart(0,now);guest.Town.Stock[0]=12;int wallet=guest.Wallet;
        check(guest.Buy(0)&&guest.Find(12)>=0&&guest.Wallet==wallet-Catalog.Items[12].Buy,"offline guest shop purchase debits once and joins existing portable belongings");
        for(int i=0;i<guest.Capacity;i++)if(guest.Pockets[i]==0)guest.Pockets[i]=1;
        var paid=Owed(guest,10);var collect=CollectCommand(guest,paid);int count=guest.Town.LostAndFound.Count;
        check(guest.ResidentJobs.Execute(collect).Result==JobResult.Ok&&guest.Town.LostAndFound.Count==count+1&&guest.Find(10)<0,"a destination job gift enters custody even when visitor pockets are full");
        guest.ResidentJobs.Execute(collect);
        check(guest.Town.LostAndFound.Count==count+1,"replayed reward collection cannot create a second custody gift");
        var reply=Owed(guest,Catalog.StripedShirt,true);collect=CollectCommand(guest,reply);
        check(guest.ResidentJobs.Execute(collect).Result==JobResult.Ok&&guest.Town.LostAndFound.Count==count+2&&guest.MailCount==0&&guest.Find(Catalog.StripedShirt)<0,"visitor reply presents enter custody without creating an invalid fifth inbox");
        var pending=Owed(guest,11);second.Save(guest);var returned=travel.Return(now+60);var host=second.Load()!;
        check(!returned.Visiting&&returned.Find(Catalog.PocketCamera)>=0&&returned.Find(12)>=0,"return preserves original belongings and the final shop purchase");
        check(returned.Town.Jobs.FindReward(homeReward)>=0&&returned.Town.Jobs.Reward(returned.Town.Jobs.FindReward(homeReward)).Item==Catalog.LeafShirt,"the home-issued owed reward returns unchanged instead of entering destination custody");
        check(returned.Town.Jobs.FindReward(pending)<0&&returned.Find(11)<0&&host.Town.LostAndFound.Count==count+3,"an uncollected destination reward is settled durably before the visitor returns");
        check(host.Town.Ground.Take(host.Town.GroundCount).Count(g=>g.Kind==4&&g.Item==11)==1,"save and return leave exactly one custody copy of the pending gift");
        int slot=host.Town.LostAndFound.IndexAt(0);Array.Clear(host.Pockets);
        check(host.ClaimLost(slot,host.ActiveProfile),"a resident can claim a destination reward after the visitor leaves");
    }
    private static void OverflowReturn(Action<bool,string> check)
    {
        long now=JobCalendar.At(2026,6,10,12);var home=Empty(7461);var destination=Empty(7462);home.Tutorial=destination.Tutorial=7;
        home.ResidentJobs.Enable(7461001,now);destination.ResidentJobs.Enable(7462001,now);home.Pockets[0]=Catalog.PocketCamera;var homeReward=Owed(home,Catalog.LeafShirt);
        for(int i=0;i<destination.Town.Ground.Length;i++){var g=destination.Town.Ground[i];g.Kind=4;g.Item=10;g.X=g.Z=g.Owner=0;g.Day=100;}destination.Town.GroundCount=512;
        var a=new Disk();var b=new Disk();var first=new SaveRepository(a);var second=new SaveRepository(b);first.Save(home);second.Save(destination);
        var travel=new OfflineTownTravel(first,second);var guest=travel.Depart(0,now);guest.Town.Stock[0]=12;
        check(guest.Buy(0),"overflow visit retains the paid shop exception");int wallet=guest.Wallet;
        var issued=Owed(guest,13);var command=CollectCommand(guest,issued);
        check(guest.ResidentJobs.Execute(command).Result==JobResult.Ok&&guest.Town.Jobs.Reward(guest.Town.Jobs.FindReward(issued)).State==JobRewardState.Custody&&guest.Find(13)<0,"full ground storage settles a newly paid destination gift into its reserved custody slot");
        var pending=Owed(guest,11);int slot=guest.Town.Jobs.FindReward(pending);var reward=guest.Town.Jobs.Rewards[slot];reward.Currency=321;guest.Town.Jobs.Rewards[slot]=reward;
        var pendingJob=reward.Job;guest.Town.Jobs.Jobs[OfflineJobLedger.Visitor*OfflineJobLedger.PersonalPerPlayer]=new VillagerJobRecord{Id=pendingJob,Owner=reward.Owner,AcceptedBy=reward.Owner,Claimant=reward.Owner,Requester=reward.Giver,Target=reward.Giver,Obligation=reward.Id,Template=JobTemplate.DeliveryClothing,Status=JobStatus.RewardOwed,Revision=1,Item=Catalog.WorkShirt,AcceptedAt=now-20,StageAt=now-10,OriginalDueAt=now+86400,DueAt=now+86400,CompletedAt=now};
        second.Save(guest);var baseA=a.Copy();var baseB=b.Copy();
        void Verify(SaveRepository homeCard,SaveRepository hostCard)
        {
            var returned=homeCard.Load()!;var host=hostCard.Load()!;
            check(!returned.ProfileLocked(0)&&!host.Visiting&&host.Town.Jobs.GuestProfile==null,"full custody never prevents a completed visitor return");
            check(returned.Wallet==wallet&&returned.Find(12)>=0&&returned.Find(Catalog.PocketCamera)>=0&&returned.Find(11)<0&&returned.Find(13)<0,"return retains the purchase and carried belongings without exporting deferred gifts");
            check(host.Town.GroundCount==512&&host.Town.LostAndFound.Count==514,"overflow gifts coexist with every original ground custody item");
            check(host.Town.Jobs.Rewards.Count(r=>r.State==JobRewardState.Custody&&r.Item==11&&r.Currency==0)==1&&host.Town.Jobs.Rewards.Count(r=>r.State==JobRewardState.Custody&&r.Item==13)==1,"each deferred item survives exactly once without the guest capsule");
            check(returned.Town.Jobs.Rewards.Count(r=>r.State==JobRewardState.Owed&&r.Currency==321&&r.Item==0&&r.Id!=pending)==1&&returned.Town.Jobs.FindJob(pendingJob)<0,"only the mixed reward's money travels under a fresh identity, without its completed job or town item");
            check(returned.Town.Jobs.FindReward(homeReward)>=0,"a carried home obligation is not mistaken for destination custody");
        }
        int writes=0;a.BeforeWrite=b.BeforeWrite=()=>writes++;travel.Return(now+60);a.BeforeWrite=b.BeforeWrite=null;Verify(first,second);
        for(int after=0;after<2;after++)for(int cut=1;cut<=writes;cut++){
            var x=baseA.Copy();var y=baseB.Copy();int at=0;Action fail=()=>{if(++at==cut)throw new IOException("Custody return power cut");};
            if(after==0)x.BeforeWrite=y.BeforeWrite=fail;else x.AfterWrite=y.AfterWrite=fail;
            var hr=new SaveRepository(x);var dr=new SaveRepository(y);try{new OfflineTownTravel(hr,dr).Return(now+60);}catch(IOException){}
            x.BeforeWrite=y.BeforeWrite=x.AfterWrite=y.AfterWrite=null;
            if(hr.Load()!.Town.Jobs.Trip.Pending||dr.Load()!.Town.Jobs.Trip.Pending){var recovery=new OfflineTownTravel(hr,dr);var recovered=recovery.Recover(now+60);if(recovered.Visiting)recovery.Return(now+60);}
            Verify(hr,dr);
        }
        var pendingA=baseA.Copy();var pendingB=baseB.Copy();var pendingHome=new SaveRepository(pendingA);var pendingHost=new SaveRepository(pendingB);
        pendingA.BeforeWrite=()=>throw new IOException("Pause after destination return decision");try{new OfflineTownTravel(pendingHome,pendingHost).Return(now+60);}catch(IOException){}pendingA.BeforeWrite=null;
        var interim=pendingHost.Load()!;check(interim.Town.Jobs.Trip.Phase==OfflineTripPhase.ReturnPrepared&&!interim.Visiting,"destination exposes only its resident after the durable return decision");
        int interimIndex=512+interim.Town.Jobs.FindReward(pending);Array.Clear(interim.Pockets);
        check(interim.ClaimLost(interimIndex,interim.ActiveProfile)&&interim.Town.Jobs.Reward(interim.Town.Jobs.FindReward(pending)).Currency==321,"a resident may claim the deferred item during return without consuming the visitor's money");
        pendingHost.Save(interim);new OfflineTownTravel(pendingHome,pendingHost).Recover(now+60);
        check(pendingHost.Load()!.Find(11)>=0&&pendingHome.Load()!.Find(11)<0&&pendingHome.Load()!.Town.Jobs.Rewards.Count(r=>r.State==JobRewardState.Owed&&r.Currency==321)==1,"a pending-return claim saves and recovers with one local item and one visitor money obligation");
        var anotherVisit=new OfflineTownTravel(first,second);anotherVisit.Depart(0,now+120);anotherVisit.Return(now+180);Verify(first,second);
        var local=second.Load()!;local.SelectProfile(1);local.DeleteProfile(0);
        check(local.Town.LostAndFound.Count==514,"another guest session and resident profile deletion cannot clean up unclaimed town custody");
        int deferred=-1;for(int row=0;row<local.Town.LostAndFound.Count;row++){int index=local.Town.LostAndFound.IndexAt(row);if(index>=512&&local.Town.LostAndFound.ItemAt(index)==11){deferred=index;break;}}
        check(deferred>=512&&local.Town.LostAndFound.RecoveryIdAt(deferred)==0&&!local.Town.LostAndFound.WrappedAt(deferred),"deferred listings expose a safe item view without a fabricated removed-object receipt");
        var words=local.Town.Jobs.Encode();words[1]=12;
        check(Rejects(()=>new OfflineJobLedger().Decode(words)),"a legacy job schema cannot silently reinterpret deferred custody");
        Array.Clear(local.Pockets);Array.Fill(local.Pockets,1,0,local.Capacity);check(!local.ClaimLost(deferred,local.ActiveProfile),"full local pockets leave a deferred custody item intact");local.Pockets[0]=0;
        var foreign=first.Load()!;check(!TownItemActions.ClaimLost(local,foreign,TownItemActor.Visitor(local,foreign),deferred),"visitor authority cannot claim a deferred gift");
        check(local.ClaimLost(deferred,local.ActiveProfile)&&local.Pockets[0]==11&&!local.ClaimLost(deferred,local.ActiveProfile)&&local.Town.LostAndFound.Count==513,"a different active resident claims the deferred item exactly once after cleanup");
        second.Save(local);check(second.Load()!.Town.LostAndFound.Count==513,"a deferred claim stays consumed after durable reload");
        Console.WriteLine($"Police overflow custody: {writes} return writes cut before and after publication; mixed money, paid purchases, revisit and profile cleanup passed");
    }
    private static void Persistence(Action<bool,string> check)
    {
        var host=Empty(7441);var guest=Empty(7442);var actor=TownItemActor.Visitor(host,guest);
        host.Town.AddGround(Catalog.Apple,32,40,2,91,true);host.Town.AddGround(16,33,40,1,93);
        check(TownItemActions.RemoveGround(host,guest,actor,0)&&TownItemActions.RemoveGround(host,guest,actor,1),"two independent persistent custody receipts created");
        int first=host.Town.Ground[0].RecoveryId,second=host.Town.Ground[1].RecoveryId;
        var valid=host.Town.LostAndFound.Encode();
        var malformed=(int[])valid.Clone();malformed[18]=first;
        check(Rejects(()=>host.Town.LostAndFound.Decode(malformed))&&host.Town.Ground[1].RecoveryId==second,"duplicate recovery IDs are rejected without partially altering the ledger");
        malformed=(int[])valid.Clone();malformed[4]=512;check(Rejects(()=>host.Town.LostAndFound.Decode(malformed)),"out-of-range custody references are rejected");
        malformed=(int[])valid.Clone();malformed[6]=77;check(Rejects(()=>host.Town.LostAndFound.Decode(malformed)),"unknown original sources are rejected");
        host.Town.Ground[0].Kind=0;check(Rejects(()=>host.Town.LostAndFound.Decode(valid)),"recovery metadata cannot attach to a live world item");host.Town.Ground[0].Kind=4;
        foreach(var copy in new[]{SaveCodec.Decode(SaveCodec.Encode(host)),SplitSaveCodec.Decode(SplitSaveCodec.Encode(host))}){
            check(copy.Town.Ground[0].RecoveryId==first&&copy.Town.Ground[0].Wrapped&&copy.Town.Ground[1].RecoveryId==second,"combined and split saves preserve linked custody and wrapping");
            copy.Rollover(200,101);check(copy.Town.LostAndFound.FindRecovery(first)>=0&&copy.Town.LostAndFound.FindRecovery(second)>=0,"police custody survives long absence without expiry");
            check(copy.ClaimLost(copy.Town.LostAndFound.FindRecovery(first),copy.ActiveProfile),"restored resident can claim one recovery item");
            check(TownItemActions.RestoreRecovery(copy,copy,TownItemActor.LocalResident(copy),second)&&copy.Town.FindGround(33,40,1)>=0,"array swap after claim keeps the other receipt attached to its exact item");
            check(!TownItemActions.RestoreRecovery(copy,copy,TownItemActor.LocalResident(copy),first),"save reload never restores a previously claimed item twice");
        }
        var disk=new Disk();var repository=new SaveRepository(disk);repository.Save(host);var loaded=repository.Load()!;
        check(loaded.Town.Ground[0].RecoveryId==first&&loaded.Town.Ground[1].RecoveryId==second,"incremental memory-card commits preserve recovery metadata");
        check(TownItemActions.RestoreRecovery(loaded,loaded,TownItemActor.LocalResident(loaded),first),"durable recovery restores after an incremental reload");repository.Save(loaded);loaded=repository.Load()!;
        check(loaded.Town.FindGround(32,40,2)>=0&&loaded.Town.LostAndFound.FindRecovery(first)<0&&loaded.Town.LostAndFound.FindRecovery(second)>=0,"committed restoration removes only its matching custody record");
    }
}
