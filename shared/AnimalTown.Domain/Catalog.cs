namespace AnimalTown;

[Flags]
public enum ItemFlags { None=0, Sell=1, Gift=2, Place=4, Outdoor=8, Eat=16, Plant=32, Tool=64, Wear=128, Bury=256 }
public sealed class ItemDef
{
    public readonly int Id, Buy, Sell, Width, Height;
    public readonly string Name;
    public readonly ItemFlags Flags;
    public ItemDef(int id,string name,int buy,int sell,ItemFlags flags,int width=1,int height=1)
    { Id=id;Name=name;Buy=buy;Sell=sell;Flags=flags;Width=width;Height=height; }
}
public static class Catalog
{
    public const int Version=1;
    public static readonly ItemDef[] Items={
        new ItemDef(0,"Empty",0,0,0),
        new ItemDef(1,"Peach",400,100,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Eat|ItemFlags.Plant|ItemFlags.Bury),
        new ItemDef(2,"Wildflower",80,20,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Plant),
        new ItemDef(3,"Pine sapling",240,60,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Plant),
        new ItemDef(4,"Work shirt",200,50,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Wear),
        new ItemDef(5,"Fishing rod",500,125,ItemFlags.Sell|ItemFlags.Tool),
        new ItemDef(6,"Bug net",500,125,ItemFlags.Sell|ItemFlags.Tool),
        new ItemDef(7,"Shovel",500,125,ItemFlags.Sell|ItemFlags.Tool),
        new ItemDef(8,"Watering can",500,125,ItemFlags.Sell|ItemFlags.Tool),
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
        new ItemDef(22,"1000 acorns",1000,1000,ItemFlags.Bury),
        new ItemDef(23,"10000 acorns",10000,10000,ItemFlags.Bury),
        new ItemDef(24,"50000 acorns",50000,50000,ItemFlags.Bury),
        new ItemDef(25,"Reading lamp",1800,450,ItemFlags.Sell|ItemFlags.Gift|ItemFlags.Place)
    };
    public static bool Valid(int id)=>id>0&&id<Items.Length;
    public static bool Has(int id,ItemFlags flags)=>Valid(id)&&(Items[id].Flags&flags)==flags;
}
