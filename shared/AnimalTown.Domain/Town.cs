namespace AnimalTown;

public enum Terrain { Grass, Water, Bridge, Beach, Cliff, Plaza, Building, Camp }
public sealed class Villager
{
    public int X,Z,Friendship,Request,RequestDay,Met;
    public string Name="",Phrase="";
}
public sealed class Placement
{
    public int Item,X,Z,Rotation,Room,Owner;
}
public sealed class GroundItem
{
    public const int PendingRecoveryDay=int.MinValue;
    public int Item,X,Z,Owner,Day,Kind; // 0 drop, 1 buried, 2 planted, 3 tree, 4 lost/found
}
public sealed class Town
{
    public const int ScreenTiles=8,ScreenTileSize=20;
    public const int Width=ScreenTiles*ScreenTileSize,Height=Width,Cells=Width*Height;
    // Fine cells support furniture/plant footprints within each screen tile.
    public readonly byte[] Tiles=new byte[Cells];
    public int TerrainVersion {get;private set;}
    // Bulk importers that write Tiles directly call this once after committing.
    public void NotifyTerrainChanged(){TerrainVersion++;}
    public bool SetTerrain(int x,int z,Terrain terrain)
    {if(!Inside(x,z)||(uint)terrain>7||Tiles[z*Width+x]==(byte)terrain)return false;Tiles[z*Width+x]=(byte)terrain;NotifyTerrainChanged();return true;}
    public readonly Villager[] Residents=new Villager[6];
    public readonly GroundItem[] Ground=new GroundItem[512];
    public readonly Placement[] Placed=new Placement[256];
    public readonly int[] Stock=new int[12];
    public int Seed,Day=-1,Month=-1,GroundCount,PlacedCount,Spending,StockCount=6;
    public int HouseX=12,HouseZ=22;
    public Town(int seed)
    {
        Seed=seed;string[] names={"Rowan","Moss","Sunny","Mica","Pip","Rook"};
        string[] phrases={"tails up","little leaf","sky high","soft paws","dig deep","good cheer"};
        for(int i=0;i<6;i++)Residents[i]=new Villager{Name=names[i],Phrase=phrases[i],X=10+i*11+(i==3?3:0),Z=14};
        for(int i=0;i<Ground.Length;i++)Ground[i]=new GroundItem();
        for(int i=0;i<Placed.Length;i++)Placed[i]=new Placement();
        Generate();
    }
    public bool Inside(int x,int z)=>x>=0&&x<Width&&z>=0&&z<Height;
    public int At(int x,int z)=>Inside(x,z)?Tiles[z*Width+x]:(int)Terrain.Water;
    public bool Walkable(int x,int z)
    { int t=At(x,z);return Inside(x,z)&&t!=(int)Terrain.Water&&t!=(int)Terrain.Building&&t!=(int)Terrain.Cliff; }
    public void Generate()
    {
        if(Day>=0||PlacedCount!=0)throw new InvalidOperationException("Generate is only for an unplayed town; saved terrain must be loaded intact");
        GroundCount=0;TownGenerator.Landscape(this);
        // Required service acres retain their existing entrances and starter
        // reserve. Their placement constraints are part of this template set.
        for(int z=29;z<=35;z++)for(int x=30;x<=37;x++)Tiles[z*Width+x]=(byte)Terrain.Plaza;
        for(int z=47;z<56;z++)for(int x=62;x<71;x++)Tiles[z*Width+x]=(byte)Terrain.Camp;
        Building(30,25,5,3);Building(48,25,5,3);Building(34,7,4,3);
        for(int i=0;i<6;i++)Building(Residents[i].X,10,3,3);
        TownGenerator.Populate(this);
        NotifyTerrainChanged();
    }
    private void Building(int x,int z,int w,int h)
    { for(int j=0;j<h;j++)for(int i=0;i<w;i++)Tiles[(z+j)*Width+x+i]=(byte)Terrain.Building; }
    public bool InReserve(int x,int z)=>x>=HouseX&&x<HouseX+15&&z>=HouseZ&&z<HouseZ+13;
    public bool ValidPlot(int x,int z)
    {
        for(int j=0;j<13;j++)for(int i=0;i<15;i++)if(At(x+i,z+j)!=0)return false;
        return true;
    }
    public int FindGround(int x,int z,int kind=-1)
    { for(int i=0;i<GroundCount;i++)if(Ground[i].X==x&&Ground[i].Z==z&&(kind<0||Ground[i].Kind==kind))return i;return -1; }
    public bool AddGround(int item,int x,int z,int kind,int day)
    {
        if(GroundCount==Ground.Length||!Catalog.Valid(item)||!Walkable(x,z)||FindGround(x,z)>=0)return false;
        var g=Ground[GroundCount++];g.Item=item;g.X=x;g.Z=z;g.Kind=kind;g.Day=day;g.Owner=0;return true;
    }
    public void RemoveGround(int index)
    { var old=Ground[index];Ground[index]=Ground[--GroundCount];Ground[GroundCount]=old; }
}
