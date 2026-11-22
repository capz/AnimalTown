namespace AnimalTown;

public enum TownItemRole { None,LocalResident,Visitor,Npc }

// Built by trusted console/session or authenticated server code, never from a
// request's claimed role. Binding both state objects and the active profile
// prevents reusing a resident capability for a visitor or after slot switching.
public readonly struct TownItemActor
{
    private readonly GameState? host,player;
    private readonly VillagerProfile? profile;
    private readonly int slot;
    public TownItemRole Role {get;}
    private TownItemActor(GameState host,GameState player,TownItemRole role)
    {this.host=host;this.player=player;Role=role;slot=player.ActiveProfile;profile=ActiveHuman(player)?player.ProfileAt(slot):null;}
    private static bool ActiveHuman(GameState state)
        =>state.Visiting?state.Town.Jobs.ActiveVisitor&&state.Town.Jobs.GuestProfile?.Created==true:
          (uint)state.ActiveProfile<GameState.ProfileCount&&state.Profiles[state.ActiveProfile].Created&&!state.ProfileLocked(state.ActiveProfile);
    public static TownItemActor LocalResident(GameState host)=>new TownItemActor(host,host,TownItemRole.LocalResident);
    public static TownItemActor Visitor(GameState host,GameState player)=>new TownItemActor(host,player,TownItemRole.Visitor);
    public static TownItemActor Npc(GameState host)=>new TownItemActor(host,host,TownItemRole.Npc);
    public static TownItemActor ForActivePlayer(GameState state)=>state.Visiting?Visitor(state,state):LocalResident(state);
    internal bool CanModify(GameState town,GameState character)
    {
        if(!ReferenceEquals(host,town)||!ReferenceEquals(player,character)||slot!=character.ActiveProfile||!ActiveHuman(character)||!ReferenceEquals(profile,character.ProfileAt(slot)))return false;
        return Role==TownItemRole.LocalResident?ReferenceEquals(town,character)&&!character.Visiting:
            Role==TownItemRole.Visitor&&(!ReferenceEquals(town,character)||character.Visiting);
    }
    internal bool CanClaim(GameState town,GameState character)=>Role==TownItemRole.LocalResident&&CanModify(town,character);
}

public static class TownItemActions
{
    private static bool Pocket(GameState player,int pocket)=>pocket>=0&&pocket<player.Capacity&&!player.IsJobItem(pocket)&&Catalog.Valid(player.Pockets[pocket]);
    private static bool GroundPosition(GameState host,int kind,int x,int z,int ignored=-1)
    {
        if(!host.Town.Walkable(x,z)||host.HomeBlocks(x,z)||kind==1&&host.Town.At(x,z)!=(int)Terrain.Grass||kind==2&&host.Town.At(x,z)!=(int)Terrain.Grass)return false;
        int other=host.Town.FindGround(x,z);if(other>=0&&other!=ignored)return false;
        for(int i=0;i<host.Town.PlacedCount;i++){
            var p=host.Town.Placed[i];if(p.Room!=0)continue;BuildingWorld.Footprint(p,out int w,out int h);
            if(x>=p.X&&x<p.X+w&&z>=p.Z&&z<p.Z+h)return false;
        }
        return true;
    }
    public static bool AddGround(GameState host,GameState player,TownItemActor actor,int pocket,int x,int z,int kind=0,bool wrapped=false)
    {
        if(!actor.CanModify(host,player)||!Pocket(player,pocket)||(uint)kind>2||wrapped&&kind!=0||!GroundPosition(host,kind,x,z))return false;
        int item=player.Pockets[pocket];if(kind==1&&!Catalog.Has(item,ItemFlags.Bury)||kind==2&&!Catalog.Has(item,ItemFlags.Plant))return false;
        if(!host.Town.AddGround(item,x,z,kind,host.Town.Day,wrapped))return false;
        player.Pockets[pocket]=0;return true;
    }
    private static int DestinationOwner(GameState host,TownItemActor actor,int item,int x,int z,int rotation,int room)
    {
        if(actor.Role==TownItemRole.LocalResident)return host.CanPlaceOwned(item,x,z,rotation,room,host.PlayerOwner)?host.PlayerOwner:-1;
        for(int p=0;p<GameState.ProfileCount;p++){
            if(!host.Profiles[p].Created)continue;int owner=p==0?0:p+6;
            if(host.CanPlaceOwned(item,x,z,rotation,room,owner))return owner;
        }
        return -1;
    }
    private static bool PlacementGroundClear(GameState host,int item,int x,int z,int rotation,int room)
    {
        if(room!=0)return true;
        int w=Catalog.Items[item].Width,h=Catalog.Items[item].Height;if((rotation&1)!=0){int swap=w;w=h;h=swap;}
        for(int i=0;i<host.Town.GroundCount;i++){var g=host.Town.Ground[i];if(g.Kind!=4&&g.X>=x&&g.X<x+w&&g.Z>=z&&g.Z<z+h)return false;}
        return true;
    }
    public static bool Place(GameState host,GameState player,TownItemActor actor,int pocket,int x,int z,int rotation,int room)
    {
        if(!actor.CanModify(host,player)||!Pocket(player,pocket)||host.Town.PlacedCount==host.Town.Placed.Length)return false;
        int item=player.Pockets[pocket],owner=DestinationOwner(host,actor,item,x,z,rotation,room);if(owner<0||!PlacementGroundClear(host,item,x,z,rotation,room))return false;
        var p=host.Town.Placed[host.Town.PlacedCount++];p.Item=item;p.X=x;p.Z=z;p.Rotation=rotation;p.Room=room;p.Owner=owner;
        player.Pockets[pocket]=0;host.Town.NotifyCastersChanged();return true;
    }
    public static bool MoveGround(GameState host,GameState player,TownItemActor actor,int index,int x,int z)
    {
        if(!actor.CanModify(host,player)||(uint)index>=host.Town.GroundCount)return false;
        var g=host.Town.Ground[index];if(g.Kind==4||g.X==x&&g.Z==z||!GroundPosition(host,g.Kind,x,z,index))return false;
        g.X=x;g.Z=z;host.Town.NotifyCastersChanged();return true;
    }
    public static bool MovePlacement(GameState host,GameState player,TownItemActor actor,int index,int x,int z,int rotation,int room)
    {
        if(!actor.CanModify(host,player)||(uint)index>=host.Town.PlacedCount)return false;
        var p=host.Town.Placed[index];
        if(p.X==x&&p.Z==z&&p.Rotation==rotation&&p.Room==room||!host.CanPlaceOwned(p.Item,x,z,rotation,room,p.Owner,false,index)||!PlacementGroundClear(host,p.Item,x,z,rotation,room))return false;
        p.X=x;p.Z=z;p.Rotation=rotation;p.Room=room;host.Town.NotifyCastersChanged();return true;
    }
    public static bool RemoveGround(GameState host,GameState player,TownItemActor actor,int index)
    {
        if(!actor.CanModify(host,player)||(uint)index>=host.Town.GroundCount)return false;
        var g=host.Town.Ground[index];if(g.Kind==4||actor.Role==TownItemRole.Visitor&&!host.Town.LostAndFound.CanRecord)return false;
        if(actor.Role==TownItemRole.Visitor)host.Town.LostAndFound.RecordGround(index);
        g.Kind=4;host.Town.NotifyCastersChanged();return true;
    }
    public static bool RemovePlacement(GameState host,GameState player,TownItemActor actor,int index)
    {
        if(!actor.CanModify(host,player)||(uint)index>=host.Town.PlacedCount||actor.Role==TownItemRole.Visitor&&!host.Town.LostAndFound.CanRecord)return false;
        var p=host.Town.Placed[index];int owner=GameState.OwnerProfile(p.Owner);if(owner<0)owner=host.Town.ResidentOwner;
        if(!host.Town.LostAndFound.Add(p.Item,p.X,p.Z,owner,host.Town.Day,false,out var custody))return false;
        if(actor.Role==TownItemRole.Visitor)host.Town.LostAndFound.RecordPlacement(host.Town.GroundCount-1,p);
        host.Town.Placed[index]=host.Town.Placed[--host.Town.PlacedCount];host.Town.Placed[host.Town.PlacedCount]=p;
        host.Town.NotifyCastersChanged();return true;
    }
    public static bool ClaimLost(GameState host,GameState player,TownItemActor actor,int index)
    {
        if(!actor.CanClaim(host,player))return false;
        if((uint)index>=host.Town.GroundCount)return host.Town.LostAndFound.ClaimDeferred(player,index);
        var g=host.Town.Ground[index];if(g.Kind!=4||!player.Receive(g.Item))return false;
        host.Town.RemoveGround(index);return true;
    }
    public static bool RestoreRecovery(GameState host,GameState player,TownItemActor actor,int recoveryId)
    {
        if(!actor.CanClaim(host,player))return false;
        int index=host.Town.LostAndFound.FindRecovery(recoveryId);if(index<0)return false;
        var g=host.Town.Ground[index];var r=host.Town.LostAndFound.RecoveryAt(index);if(r==null||r.Id!=g.RecoveryId||!r.Matches(g))return false;
        if(r.Source==TownRecoverySource.Ground){
            if(!GroundPosition(host,r.Kind,r.X,r.Z))return false;
            g.Kind=r.Kind;g.Owner=r.Owner;g.Day=r.Day;g.Wrapped=r.Wrapped;host.Town.LostAndFound.ClearRecovery(index);host.Town.NotifyCastersChanged();return true;
        }
        if(r.Source==TownRecoverySource.Placement){
            if(host.Town.PlacedCount==host.Town.Placed.Length||!host.CanPlaceOwned(r.Item,r.X,r.Z,r.Rotation,r.Room,r.Owner)||!PlacementGroundClear(host,r.Item,r.X,r.Z,r.Rotation,r.Room))return false;
            var p=host.Town.Placed[host.Town.PlacedCount++];p.Item=r.Item;p.X=r.X;p.Z=r.Z;p.Rotation=r.Rotation;p.Room=r.Room;p.Owner=r.Owner;
        }
        // A caught/harvested yield had no removed original object to recreate.
        host.Town.RemoveGround(index);return true;
    }
    public static bool Dig(GameState state,int index)=>Dig(state,state,TownItemActor.ForActivePlayer(state),index);
    public static bool Dig(GameState host,GameState player,TownItemActor actor,int index)
    {
        if(!actor.CanModify(host,player)||player.Tool!=Catalog.Shovel||(uint)index>=host.Town.GroundCount||host.Town.Ground[index].Kind!=1)return false;
        if(actor.Role==TownItemRole.Visitor)return RemoveGround(host,player,actor,index);
        if(!player.Receive(host.Town.Ground[index].Item))return false;host.Town.RemoveGround(index);return true;
    }
    public static bool Harvest(GameState state,int index)=>Harvest(state,state,TownItemActor.ForActivePlayer(state),index);
    public static bool Harvest(GameState host,GameState player,TownItemActor actor,int index)
    {
        if(!actor.CanModify(host,player)||(uint)index>=host.Town.GroundCount)return false;
        var g=host.Town.Ground[index];if(g.Kind!=3)return false;
        int fruit=Catalog.Has(g.Item,ItemFlags.Fruit)?g.Item:host.Town.Jobs.Initialized?host.Town.Jobs.NativeFruit:1;
        return Resource(host,player,actor,fruit,g.X,g.Z);
    }
    public static bool Chop(GameState state,int index)=>Chop(state,state,TownItemActor.ForActivePlayer(state),index);
    public static bool Chop(GameState host,GameState player,TownItemActor actor,int index)
    {
        if(player.Tool!=Catalog.Axe||(uint)index>=host.Town.GroundCount||host.Town.Ground[index].Kind!=3)return false;
        // Keep the exact planted fruit/sapling species, rather than changing all
        // removed trees into pine saplings. It can be replanted after claiming.
        return RemoveGround(host,player,actor,index);
    }
    public static bool Catch(GameState state,int item)=>Catch(state,state,TownItemActor.ForActivePlayer(state),item);
    public static bool Catch(GameState host,GameState player,TownItemActor actor,int item)
    {
        if(!actor.CanModify(host,player)||!(player.Tool==Catalog.FishingRod&&Catalog.Has(item,ItemFlags.Fish)||player.Tool==Catalog.BugNet&&Catalog.Has(item,ItemFlags.Insect)))return false;
        return Resource(host,player,actor,item,Math.Clamp((int)player.X,0,Town.Width-1),Math.Clamp((int)player.Z,0,Town.Height-1));
    }
    // Used only after a trusted job/mail producer has checked source-town
    // provenance. Paid shop deliveries deliberately continue through Receive.
    internal static bool ReceiveTownGift(GameState state,int item)
    {
        var actor=TownItemActor.ForActivePlayer(state);if(!actor.CanModify(state,state))return false;
        return Resource(state,state,actor,item,Math.Clamp((int)state.X,0,Town.Width-1),Math.Clamp((int)state.Z,0,Town.Height-1));
    }
    private static bool Resource(GameState host,GameState player,TownItemActor actor,int item,int x,int z)
    {
        if(actor.Role==TownItemRole.LocalResident)return player.Receive(item);
        if(!host.Town.LostAndFound.CanRecord||!host.Town.LostAndFound.Add(item,x,z,host.Town.ResidentOwner,host.Town.Day,false,out var custody))return false;
        host.Town.LostAndFound.RecordYield(host.Town.GroundCount-1);return true;
    }
}
