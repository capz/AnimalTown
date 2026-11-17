namespace AnimalTown;

[Flags]
public enum ItemFlags { None=0, Sell=1, Gift=2, Place=4, Outdoor=8, Eat=16, Plant=32, Tool=64, Wear=128, Bury=256, Money=512 }
public sealed class ItemDef
{
    public readonly int Id, Buy, Sell, Width, Height, MoneyValue;
    public readonly string Name;
    public readonly ItemFlags Flags;
    public ItemDef(int id,string name,int buy,int sell,ItemFlags flags,int width=1,int height=1,int moneyValue=0)
    { Id=id;Name=name;Buy=buy;Sell=sell;Flags=flags;Width=width;Height=height;MoneyValue=moneyValue; }
}
public static class Catalog
{
    public const int Version=1;
    public const int WorkShirt=4;
    public const int Acorns1000=22,Acorns10000=23,LegacyAcorns50000=24;
    public const int FishingRod=5,BugNet=6,Shovel=7,LegacyWateringCan=8,Axe=9;
    public const int ToolCount=4;
    private static readonly int[] tools={FishingRod,BugNet,Shovel,Axe};
    public static readonly ItemDef[] Items={
        new ItemDef(0,"Empty",0,0,0),
        new ItemDef(1,"Peach",400,100,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Eat|ItemFlags.Plant|ItemFlags.Bury),
        new ItemDef(2,"Wildflower",80,20,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Plant),
        new ItemDef(3,"Pine sapling",240,60,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Plant),
        new ItemDef(WorkShirt,"Work shirt",200,50,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Wear),
        new ItemDef(5,"Fishing rod",500,125,ItemFlags.Sell|ItemFlags.Tool),
        new ItemDef(6,"Bug net",500,125,ItemFlags.Sell|ItemFlags.Tool),
        new ItemDef(7,"Shovel",500,125,ItemFlags.Sell|ItemFlags.Tool),
        // Keep ID 8 readable/sellable for existing saves; it is not a GameCube tool.
        new ItemDef(8,"Watering can",0,125,ItemFlags.Sell),
        new ItemDef(9,"Axe",500,125,ItemFlags.Sell|ItemFlags.Tool),
        new ItemDef(10,"Pine chair",1200,300,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Place|ItemFlags.Outdoor),
        new ItemDef(11,"Garden table",2400,600,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Place|ItemFlags.Outdoor,2,1),
        new ItemDef(12,"Clay chime",1600,400,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Place|ItemFlags.Outdoor),
        new ItemDef(13,"Market stall",3000,750,ItemFlags.Sell|ItemFlags.Place|ItemFlags.Outdoor,2,1),
        new ItemDef(14,"River fish",0,350,ItemFlags.Sell|ItemFlags.Gift),
        new ItemDef(15,"Butterfly",0,160,ItemFlags.Sell|ItemFlags.Gift),
        new ItemDef(16,"Fossil",0,1200,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Bury),
        new ItemDef(17,"Seashell",0,60,ItemFlags.Sell|ItemFlags.Gift),
        new ItemDef(18,"Parcel",0,0,ItemFlags.Gift),
        new ItemDef(19,"Letter",0,0,ItemFlags.Gift),
        new ItemDef(20,"Lost umbrella",0,0,ItemFlags.None),
        new ItemDef(21,"Weed",0,5,ItemFlags.Sell),
        new ItemDef(Acorns1000,"1000 acorns",0,0,ItemFlags.Money|ItemFlags.Bury,moneyValue:1000),
        new ItemDef(Acorns10000,"10000 acorns",0,0,ItemFlags.Money|ItemFlags.Bury,moneyValue:10000),
        // Non-GameCube denomination: retain its full value for older saves.
        new ItemDef(LegacyAcorns50000,"50000 acorns",0,0,ItemFlags.Money,moneyValue:50000),
        new ItemDef(25,"Reading lamp",1800,450,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Place)
    };
    public static int ToolAt(int index)=>index>=0&&index<ToolCount?tools[index]:0;
    public static int MoneyValue(int id)=>Has(id,ItemFlags.Money)?Items[id].MoneyValue:0;
    public static bool IsCatalogEntry(int id)=>Valid(id)&&id!=LegacyWateringCan&&!Has(id,ItemFlags.Money);
    public static int EntryCount
    {get{int count=0;for(int id=1;id<Items.Length;id++)if(IsCatalogEntry(id))count++;return count;}}
    public static int EntryAt(int row)
    {for(int id=1;id<Items.Length;id++)if(IsCatalogEntry(id)&&row--==0)return id;return 0;}
    public static bool Valid(int id)=>id>0&&id<Items.Length;
    public static bool Has(int id,ItemFlags flags)=>Valid(id)&&(Items[id].Flags&flags)==flags;
}
