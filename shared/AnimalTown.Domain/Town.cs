namespace AnimalTown;

public enum Terrain { Grass, Water, Bridge, Beach, Cliff, Plaza, Building, Camp, Sea, CoastNorth, CoastEast, CoastSouth, CoastWest }
public sealed class Villager
{
    public int X,Z,Friendship,Request,RequestDay,Met;
    public int HomeX,HomeZ;
    public int Space,JourneyBuilding=-1,JourneyPhase,Dwell,JourneyNextX=-1,JourneyNextZ=-1;
    public float PosX,PosZ,Yaw;
    public string Name="",Phrase="";
}
public sealed class Placement
{
    public int Item,X,Z,Rotation,Room,Owner;
}
public sealed class GroundItem
{
    public const int PendingRecoveryDay=int.MinValue;
    private const int WrappedBit=8;
    public int Item,X,Z,Owner,Day,Kind; // 0 drop, 1 buried, 2 planted, 3 tree, 4 lost/found
    // Independent of item identity: any ordinary dropped item may be gift-wrapped.
    public bool Wrapped;
    // Fits the existing item layout; full receipt references are allocated
    // lazily by the town only after a visitor actually removes something.
    public int RecoveryId {get;internal set;}
    public int PackedKind=>Kind|(Wrapped?WrappedBit:0);
    public void RestoreKind(int value)
    {
        if(value<0||value>12||(value&7)>4)throw new InvalidOperationException("Invalid ground item kind");
        Kind=value&7;Wrapped=(value&WrappedBit)!=0;
    }
}
public sealed partial class Town
{
    public const int ScreenTileSize=20;
    // 15,360 fine cells: exactly 60% of the previous 160 x 160 town.
    // The easternmost acre is partial; individual cells keep their world scale.
    public const int Width=128,Height=120,Cells=Width*Height;
    public const int ScreenColumns=(Width+ScreenTileSize-1)/ScreenTileSize,ScreenRows=(Height+ScreenTileSize-1)/ScreenTileSize;
    // Fine cells support furniture/plant footprints within each screen tile.
    public readonly byte[] Tiles;
    public readonly byte[] Elevation;
    public readonly TownPaths Paths;
    public readonly TileTraffic Traffic=new TileTraffic();
    public readonly Jobs.TownJobState JobState;
    public readonly Jobs.OfflineJobLedger Jobs=new Jobs.OfflineJobLedger();
    public int TerrainVersion {get;private set;}
    public int ShadowVersion {get;private set;}
    public int ObjectVersion {get;private set;}
    public void NotifyCastersChanged(){ShadowVersion++;}
    // Placement relocations can keep the same item count. Bulk editors notify
    // once after changing coordinates/rooms; this value is not saved gameplay.
    public void NotifyObjectsChanged(){ObjectVersion++;}
    // Bulk importers that write Tiles directly call this once after committing.
    public void NotifyTerrainChanged(){TerrainVersion++;}
    public bool SetTerrain(int x,int z,Terrain terrain)
    {if(!Inside(x,z)||(uint)terrain>(uint)Terrain.CoastWest||Tiles[z*Width+x]==(byte)terrain)return false;Tiles[z*Width+x]=(byte)terrain;NotifyTerrainChanged();return true;}
    public readonly Villager[] Residents=new Villager[SavedVillagerCount];
    public readonly GroundItem[] Ground=new GroundItem[512];
    public readonly Placement[] Placed=new Placement[256];
    public readonly int[] Stock=new int[12];
    public readonly TownShop Shop=new TownShop();
    public readonly TownLostAndFound LostAndFound;
    public int Seed,Day=-1,Month=-1,GroundCount,PlacedCount,Spending,StockCount=6;
    public int HouseX=12,HouseZ=22;
    private int daylightLocation;
    public int DaylightLocation
    {
        get=>daylightLocation;
        set
        {
            if((uint)value>DaylightLocations.Count)throw new ArgumentOutOfRangeException(nameof(value));
            daylightLocation=value;
        }
    }
    public int ActiveProfile {get;internal set;}
    public Town(int seed):this(seed,null){}
    // Save readback validates terrain against the live snapshot without writing
    // to it. Other mutable records remain private to this validation town.
    internal Town(int seed,Town? snapshot,bool generate=true)
    {
        Seed=seed;LostAndFound=new TownLostAndFound(this);JobState=new Jobs.TownJobState(seed);Tiles=snapshot==null?new byte[Cells]:snapshot.Tiles;Elevation=snapshot==null?new byte[Cells]:snapshot.Elevation;Paths=new TownPaths();if(snapshot!=null){Array.Copy(snapshot.Paths.Packed,Paths.Packed,TownPaths.PackedBytes);Array.Copy(snapshot.Traffic.Visits,Traffic.Visits,TileTraffic.Bytes);}
        for(int i=0;i<SavedVillagerCount;i++){
            int x=10+i*11+(i==3?3:0);string name=VillagerNames.Generate(seed,i);
            bool duplicate;do{duplicate=false;for(int j=0;j<i;j++)if(Residents[j].Name==name){name+="y";duplicate=true;break;}}while(duplicate);
            Residents[i]=new Villager{Name=name,Phrase=VillagerNames.DefaultPhrase(i),X=x,Z=14,HomeX=x,HomeZ=10,PosX=x+.5f,PosZ=14.5f};
        }
        for(int i=0;i<Ground.Length;i++)Ground[i]=new GroundItem();
        for(int i=0;i<Placed.Length;i++)Placed[i]=new Placement();
        if(snapshot==null&&generate)Generate();
    }
    public bool Inside(int x,int z)=>x>=0&&x<Width&&z>=0&&z<Height;
    public int At(int x,int z)=>Inside(x,z)?Koban.Contains(x,z)?(int)Terrain.Building:Tiles[z*Width+x]:(int)Terrain.Sea;
    public bool Walkable(int x,int z)
    { int t=At(x,z);return Inside(x,z)&&!WaterSurface.IsWater(this,x,z)&&!WaterTerrain.IsCoast(t)&&t!=(int)Terrain.Building&&t!=(int)Terrain.Cliff; }
    public void Generate()
    {
        if(Day>=0||PlacedCount!=0)throw new InvalidOperationException("Generate is only for an unplayed town; saved terrain must be loaded intact");
        GroundCount=0;TownGenerator.Landscape(this);
        // Required service acres retain their existing entrances and starter
        // reserve. Their placement constraints are part of this template set.
        for(int z=29;z<=35;z++)for(int x=30;x<=37;x++)Tiles[z*Width+x]=(byte)Terrain.Plaza;
        for(int z=47;z<56;z++)for(int x=62;x<71;x++)Tiles[z*Width+x]=(byte)Terrain.Camp;
        Building(30,25,5,3);Building(48,25,5,3);Building(HelipadSite.X,HelipadSite.Z,HelipadSite.Width,HelipadSite.Depth);
        for(int i=0;i<SavedVillagerCount;i++)Building(Residents[i].HomeX,Residents[i].HomeZ,3,3);
        TerrainElevation.Generate(this);
        TownGenerator.Populate(this);
        DistributeNewPopulation();
        Paths.Clear();
        NotifyTerrainChanged();
    }
    private void Building(int x,int z,int w,int h)
    { for(int j=0;j<h;j++)for(int i=0;i<w;i++)Tiles[(z+j)*Width+x+i]=(byte)Terrain.Building; }
    // A generation-only safety clearing, retained so existing town seeds keep
    // their landscape. Player plot ownership/placement uses GameState instead.
    public bool InReserve(int x,int z)=>x>=HouseX&&x<HouseX+15&&z>=HouseZ&&z<HouseZ+13;
    public bool ValidPlot(int x,int z)
    {
        for(int j=0;j<13;j++)for(int i=0;i<15;i++)if(At(x+i,z+j)!=0||TerrainElevation.Mode(this,x+i,z+j)!=0||(x!=HouseX||z!=HouseZ)&&Paths.At(x+i,z+j)!=0)return false;
        return true;
    }
    public int FindGround(int x,int z,int kind=-1)
    { for(int i=0;i<GroundCount;i++)if(Ground[i].X==x&&Ground[i].Z==z&&(kind<0?Ground[i].Kind!=4:Ground[i].Kind==kind))return i;return -1; }
    public bool AddGround(int item,int x,int z,int kind,int day,bool wrapped=false)
        =>AddGround(item,x,z,kind,day,out _,wrapped);

    internal bool AddGround(int item,int x,int z,int kind,int day,out string reason,bool wrapped=false)
    {
        reason=GroundCount==Ground.Length?"The town has no room for more items on the ground. Pick something up first.":
            kind<0||kind>4?"That item type cannot be put on the ground.":
            !Catalog.Valid(item)?"There's no item to put down.":
            !Walkable(x,z)?"Choose a spot on walkable ground.":
            FindGround(x,z)>=0?"There's already something on the ground there.":"";
        if(reason.Length!=0)return false;
        int index=GroundCount++;var g=Ground[index];g.Item=item;g.X=x;g.Z=z;g.Kind=kind;g.Wrapped=wrapped;g.Day=day;g.Owner=ResidentOwner;LostAndFound.ClearRecovery(index);NotifyCastersChanged();return true;
    }
    // A visiting capsule is slot 4, outside the on-disk resident-owner range.
    internal int ResidentOwner=>ActiveProfile<GameState.ProfileCount?ActiveProfile:Math.Clamp(Jobs.Trip.ResidentAnchor,0,GameState.ProfileCount-1);
    public void RemoveGround(int index)
    { int last=GroundCount-1;LostAndFound.RemoveGroundAt(index,last);var old=Ground[index];Ground[index]=Ground[last];Ground[last]=old;GroundCount=last;NotifyCastersChanged(); }
    // Onboarding pauses world mutation and records tree-removal indices. Undo
    // them in reverse order to restore the exact compact table and custody
    // indices, without cloning the town or allocating another set of objects.
    internal void RestoreRemovedTree(int index)
    {
        int last=GroundCount;
        if(last>=Ground.Length||(uint)index>(uint)last||!TreeDrawBatch.IsTree(Ground[last])||Ground[last].RecoveryId!=0)
            throw new InvalidOperationException("Invalid draft tree restoration");
        LostAndFound.RestoreGroundAt(index,last);
        var tree=Ground[last];Ground[last]=Ground[index];Ground[index]=tree;GroundCount++;NotifyCastersChanged();
    }
}
