using System.Text.Json;
using AnimalTown;

// Town revisions protect compacted world indexes. A separate player revision
// protects a visitor's pocket selection when an addition consumes their item.
record TownItemOp(Guid Id,long Revision,string Kind,string Source="ground",int Index=0,
    int Pocket=0,int X=0,int Z=0,int Rotation=0,int Room=0,int GroundKind=0,
    bool Wrapped=false,long PlayerRevision=-1,int RecoveryId=0);

sealed partial class GameDatabase
{
    static void RequireLocalPlayer(GameState state)
    {
        if(state.Visiting||(uint)state.ActiveProfile>=GameState.ProfileCount||
            !state.Profiles[state.ActiveProfile].Created||state.ProfileLocked(state.ActiveProfile))
            throw new GameError(403,"local-human-resident-required");
    }

    public async Task<object> TownItems(Guid player,Guid host)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        var loaded=await Load(c,t,host,true);
        if(player!=host){
            await CheckVisit(c,t,player,host);
            if(await IsImportedPlacementTown(c,t,host))throw new GameError(409,"live-town-view-required");
        }
        RequireLocalPlayer(loaded.State);
        var state=loaded.State;long revision=loaded.Revision;int day=state.Town.Day;
        if(!await IsImportedPlacementTown(c,t,host)){
            state.Rollover(Day,DateTime.UtcNow.Year*12+DateTime.UtcNow.Month);
            if(state.Town.Day!=day){await Save(c,t,host,state);revision++;}
        }
        var result=new{
            revision,
            ground=Enumerable.Range(0,state.Town.GroundCount).Where(i=>state.Town.Ground[i].Kind!=4).Select(i=>{
                var g=state.Town.Ground[i];return new{index=i,item=g.Item,x=g.X,z=g.Z,kind=g.Kind,wrapped=g.Wrapped};
            }).ToArray(),
            placed=Enumerable.Range(0,state.Town.PlacedCount).Select(i=>{
                var p=state.Town.Placed[i];return new{index=i,item=p.Item,x=p.X,z=p.Z,rotation=p.Rotation,room=p.Room,owner=p.Owner};
            }).ToArray()
        };
        await t.CommitAsync();return result;
    }

    public async Task<object> LostFound(Guid player,Guid host)
    {
        if(player!=host)throw new GameError(403,"local-human-resident-required");
        var loaded=await State(host);RequireLocalPlayer(loaded.State);
        var state=loaded.State;var custody=state.Town.LostAndFound;
        return new{
            revision=loaded.Revision,
            resident=state.ActiveProfile,
            entries=Enumerable.Range(0,custody.Count).Select(row=>{
                int index=custody.IndexAt(row);return new{index,item=custody.ItemAt(index),wrapped=custody.WrappedAt(index),recoveryId=custody.RecoveryIdAt(index)};
            }).ToArray()
        };
    }

    public async Task<object> TownItemOperation(Guid player,Guid host,TownItemOp op)
    {
        if(op.Id==Guid.Empty||op.Revision<0||op.Kind is not ("add" or "move" or "remove" or "claim" or "restore"))
            throw new GameError(400,"invalid-town-item-operation");
        bool localRecovery=op.Kind is "claim" or "restore";
        if(localRecovery?op.Source!="lost-found":op.Source is not ("ground" or "placed"))
            throw new GameError(400,"invalid-town-item-source");
        // There is deliberately no guest pickup/acquire operation. Shop purchases
        // continue through the existing permanent, atomic purchase endpoints.
        if(localRecovery&&player!=host)throw new GameError(403,"local-human-resident-required");
        string fingerprint=Hash("town-items:"+host+":"+JsonSerializer.Serialize(op));
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        Guid first=player.CompareTo(host)<0?player:host,second=first==player?host:player;
        var a=await Load(c,t,first,true);var b=first==second?a:await Load(c,t,second,true);
        var town=first==host?a:b;var customer=first==player?a:b;
        await using(var cmd=Cmd(c,t,"SELECT fingerprint,result::text FROM operations WHERE player=$1 AND id=$2",player,op.Id))
        await using(var r=await cmd.ExecuteReaderAsync())if(await r.ReadAsync()){
            if(r.GetString(0)!=fingerprint)throw new GameError(409,"idempotency-key-reused");
            return JsonSerializer.Deserialize<JsonElement>(r.GetString(1));
        }
        if(player!=host)await CheckVisit(c,t,player,host);
        // Card-backed worlds require durable placement provenance and reconciliation.
        if(await IsImportedPlacementTown(c,t,host)||
            player!=host&&await IsImportedPlacementTown(c,t,player))
            throw new GameError(409,"online-review-required");
        RequireLocalPlayer(town.State);RequireLocalPlayer(customer.State);
        if(op.Revision!=town.Revision)throw new GameError(409,"town-items-changed");
        if(op.Kind=="add"&&player!=host&&op.PlayerRevision!=customer.Revision)
            throw new GameError(409,"visitor-inventory-changed");
        var state=town.State;var actorState=customer.State;
        var actor=player==host?TownItemActor.LocalResident(state):TownItemActor.Visitor(state,actorState);
        // Do not roll the calendar between validating the revision and applying
        // an indexed command. The next town read advances the day separately.
        bool ok=(op.Kind,op.Source) switch{
            ("add","ground")=>TownItemActions.AddGround(state,actorState,actor,op.Pocket,op.X,op.Z,op.GroundKind,op.Wrapped),
            ("add","placed")=>TownItemActions.Place(state,actorState,actor,op.Pocket,op.X,op.Z,op.Rotation,op.Room),
            ("move","ground")=>TownItemActions.MoveGround(state,actorState,actor,op.Index,op.X,op.Z),
            ("move","placed")=>TownItemActions.MovePlacement(state,actorState,actor,op.Index,op.X,op.Z,op.Rotation,op.Room),
            ("remove","ground")=>TownItemActions.RemoveGround(state,actorState,actor,op.Index),
            ("remove","placed")=>TownItemActions.RemovePlacement(state,actorState,actor,op.Index),
            ("claim","lost-found")=>TownItemActions.ClaimLost(state,actorState,actor,op.Index),
            ("restore","lost-found")=>TownItemActions.RestoreRecovery(state,actorState,actor,op.RecoveryId),
            _=>false
        };
        if(!ok)throw new GameError(409,"town-item-operation-rejected");
        await Save(c,t,host,state);
        bool saveCustomer=player!=host&&op.Kind=="add";
        if(saveCustomer)await Save(c,t,player,actorState);
        var result=new{
            ok=true,revision=town.Revision+1,
            playerRevision=player==host?town.Revision+1:customer.Revision+(saveCustomer?1:0),
            lostCount=state.Town.LostAndFound.Count
        };
        await using(var cmd=Cmd(c,t,"INSERT INTO operations VALUES($1,$2,$3,$4::jsonb)",player,op.Id,fingerprint,JsonSerializer.Serialize(result)))
            await cmd.ExecuteNonQueryAsync();
        await t.CommitAsync();return result;
    }
}
