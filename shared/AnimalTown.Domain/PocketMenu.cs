namespace AnimalTown;

public enum PocketAction { Equip,Drop,Place,Plant,Eat,Wear,Bury,OpenMoneyBag,Read,Apply,Quit }

// Stable actions are independent of the filtered row index. No per-frame lists.
public static class PocketMenu
{
    // Presentation order is separate from action IDs. Reuse it for every popup;
    // primary item actions precede dropping them.
    private static readonly PocketAction[] actions={PocketAction.Equip,PocketAction.Wear,PocketAction.OpenMoneyBag,PocketAction.Drop,
        PocketAction.Place,PocketAction.Plant,PocketAction.Eat,PocketAction.Bury,PocketAction.Read,PocketAction.Apply,PocketAction.Quit};
    public static bool HasAction(int item,int room,PocketAction action)
    {
        if(action==PocketAction.Quit)return true;
        if(!Catalog.Valid(item))return false;
        if(action==PocketAction.Equip)return Catalog.Has(item,ItemFlags.Tool);
        if(action==PocketAction.Drop)return room==0;
        if(action==PocketAction.Place)return Catalog.Has(item,ItemFlags.Place)&&(room!=0||Catalog.Has(item,ItemFlags.Outdoor));
        if(action==PocketAction.Plant)return room==0&&Catalog.Has(item,ItemFlags.Plant);
        if(action==PocketAction.Eat)return Catalog.Has(item,ItemFlags.Eat);
        if(action==PocketAction.Apply)return room!=0&&(Catalog.Has(item,ItemFlags.Flooring)||Catalog.Has(item,ItemFlags.Wallpaper));
        if(action==PocketAction.Wear)return Catalog.Has(item,ItemFlags.Wear);
        if(action==PocketAction.Bury)return room==0&&Catalog.Has(item,ItemFlags.Bury);
        if(action==PocketAction.OpenMoneyBag)return Catalog.Has(item,ItemFlags.Money);
        return action==PocketAction.Read&&item==19;
    }
    public static int Count(int item,int room)
    {int count=0;for(int i=0;i<actions.Length;i++)if(HasAction(item,room,actions[i]))count++;return count;}
    public static PocketAction At(int item,int room,int row)
    {
        if(row<0)return PocketAction.Quit;
        for(int i=0;i<actions.Length;i++)if(HasAction(item,room,actions[i])&&row--==0)return actions[i];
        return PocketAction.Quit;
    }
    public static string Label(PocketAction action)
    {
        if(action==PocketAction.Equip)return "Equip";
        if(action==PocketAction.Drop)return "Drop";
        if(action==PocketAction.Place)return "Place (R rotates)";
        if(action==PocketAction.Plant)return "Plant";
        if(action==PocketAction.Eat)return "Eat";
        if(action==PocketAction.Wear)return "Wear";
        if(action==PocketAction.Bury)return "Bury";
        if(action==PocketAction.OpenMoneyBag)return "Put in wallet";
        if(action==PocketAction.Read)return "Read";
        if(action==PocketAction.Apply)return "Use in this room";
        return "Quit";
    }
    public static bool Perform(GameState state,int slot,PocketAction action,int x,int z,int rotation,int room)
    {
        if(slot<0||slot>=state.Capacity||!HasAction(state.Pockets[slot],room,action))return false;
        if(action==PocketAction.Apply)return state.Use(slot,7,x,z);
        if(action==PocketAction.Drop)return state.Use(slot,0,x,z);
        if(action==PocketAction.Place)return state.Place(slot,x,z,rotation,room);
        if(action==PocketAction.Plant)return state.Use(slot,1,x,z);
        if(action==PocketAction.Eat)return state.Use(slot,2,x,z);
        if(action==PocketAction.Wear)return state.Use(slot,3,x,z);
        if(action==PocketAction.Equip)return state.Use(slot,4,x,z);
        if(action==PocketAction.Bury)return state.Use(slot,5,x,z);
        return action==PocketAction.OpenMoneyBag&&state.Use(slot,6,x,z);
    }
}
