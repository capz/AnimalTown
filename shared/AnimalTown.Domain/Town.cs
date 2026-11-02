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
    public int Item,X,Z,Owner,Day,Kind; // 0 drop, 1 buried, 2 planted, 3 tree, 4 lost/found
}
public sealed class Town
{
    public const int ScreenTiles=20,ScreenTileSize=20;
    public const int Width=ScreenTiles*ScreenTileSize,Height=Width,Cells=Width*Height;
    // Fine cells support furniture/plant footprints within each screen tile.
    public readonly byte[] Tiles=new byte[Cells];
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
        for(int z=0;z<Height;z++)for(int x=0;x<Width;x++)
        {
            int t=(int)Terrain.Grass;
            if(z>=Height-8)t=(int)Terrain.Beach;
            if(z>=Height-2||x==0||x==Width-1||z==0)t=(int)Terrain.Water;
            if(x>=41&&x<=43)t=(int)Terrain.Water;
            if(x>=41&&x<=43&&(z==18||z==38||z==58))t=(int)Terrain.Bridge;
            if(z==5&&x>51&&x<75)t=(int)Terrain.Cliff;
            if(x>=30&&x<=37&&z>=29&&z<=35)t=(int)Terrain.Plaza;
            if(x>=62&&x<71&&z>=47&&z<56)t=(int)Terrain.Camp;
            Tiles[z*Width+x]=(byte)t;
        }
        Building(30,25,5,3);Building(48,25,5,3);Building(34,7,4,3);
        for(int i=0;i<6;i++)Building(Residents[i].X,10,3,3);
        uint r=(uint)Seed;
        for(int i=0;i<360;i++)
        {
            r=r*1664525u+1013904223u;int x=2+(int)(r%(uint)(i<120?76:Width-4));
            r=r*1664525u+1013904223u;int z=17+(int)(r%(uint)(i<120?46:Height-27));
            if(At(x,z)==0&&!InReserve(x,z)&&!(x>=28&&x<=55&&z<=37))AddGround(i%3+1,x,z,3,0);
        }
        AddGround(17,15,Height-5,0,0);AddGround(17,27,Height-6,0,0);
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
