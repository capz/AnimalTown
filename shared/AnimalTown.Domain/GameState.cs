namespace AnimalTown;

public sealed class GameState
{
    public readonly Town Town;
    public const int MaximumPockets=30;
    public readonly int[] Pockets=new int[MaximumPockets],Owned=new int[64],Mail=new int[128];
    public int BackpackTier;
    public int Capacity=>(BackpackTier+1)*10;
    public int BackpackUpgradePrice=>BackpackTier==0?6000:BackpackTier==1?18000:0;
    public int Wallet,Species,Palette,Tool,Shirt,HouseTier,GardenTier,PendingKind,PendingTier,PendingDay;
    public int Tutorial,MetMask,Jobs,JobDay=-1,MailCount,Sequence,Roof,Wall,Fence,Mailbox,Exterior;
    public long Savings,Debt=24000;
    public float X=18,Z=38;
    public bool HousePlaced;
    public string Name="Newcomer",Catchphrase="hello there";
    public static readonly int[] HousePrices={18000,120000,320000,650000,980000,1600000};
    public static readonly int[] GardenPrices={0,40000,150000,400000,900000};
    public static readonly int[] GardenWidths={7,9,11,13,15},GardenHeights={5,7,9,11,13};
    public GameState(int seed){Town=new Town(seed);Pockets[0]=4;Pockets[1]=2;Pockets[2]=18;}
    public int FreeSlot(){for(int i=0;i<Capacity;i++)if(Pockets[i]==0)return i;return -1;}
    public int Find(int item){for(int i=0;i<Capacity;i++)if(Pockets[i]==item)return i;return -1;}
    public bool UpgradeBackpack()
    { int cost=BackpackUpgradePrice;if(cost==0||Wallet<cost)return false;Wallet-=cost;BackpackTier++;return true; }
    public bool Receive(int item)
    { int s=FreeSlot();if(s<0||!Catalog.Valid(item))return false;Pockets[s]=item;Owned[item]=1;return true; }
    public bool Buy(int stock)
    {
        if(stock<0||stock>=Town.StockCount)return false;int id=Town.Stock[stock];
        if(!Catalog.Valid(id)||Catalog.Items[id].Buy<=0||Wallet<Catalog.Items[id].Buy||!Receive(id))return false;
        Wallet-=Catalog.Items[id].Buy;Town.Spending+=Catalog.Items[id].Buy;Town.Stock[stock]=0;return true;
    }
    public bool Sell(int slot)
    {
        if(slot<0||slot>=Capacity)return false;int id=Pockets[slot];
        if(!Catalog.Has(id,ItemFlags.Sell)||Catalog.Items[id].Sell>99999-Wallet)return false;
        Wallet+=Catalog.Items[id].Sell;Pockets[slot]=0;return true;
    }
    public bool Deposit(int amount)
    { if(amount<=0||amount>Wallet||Savings>long.MaxValue-amount)return false;Wallet-=amount;Savings+=amount;return true; }
    public bool Withdraw(int amount)
    { if(amount<=0||amount>Savings||amount>99999-Wallet)return false;Savings-=amount;Wallet+=amount;return true; }
    public bool Repay(int amount)
    { if(amount<=0||amount>Wallet||amount>Debt)return false;Wallet-=amount;Debt-=amount;return true; }
    public bool Expand(bool garden,int day)
    {
        if(!HousePlaced||Debt!=0||PendingKind!=0)return false;
        int next=(garden?GardenTier:HouseTier)+1;
        if(garden?(next>4||HouseTier<next):(next>5))return false;
        Debt=garden?GardenPrices[next]:HousePrices[next];PendingKind=garden?2:1;PendingTier=next;PendingDay=day+1;return true;
    }
    public bool PlaceHouse(int x,int z,bool relocate)
    {
        if(!Town.ValidPlot(x,z)||(relocate&&Wallet<50000))return false;
        if(!HouseEntrance(x,z,out float arrivalX,out float arrivalZ))return false;
        int dx=x-Town.HouseX,dz=z-Town.HouseZ;
        for(int i=0;i<Town.PlacedCount;i++)if(Town.Placed[i].Room==0){Town.Placed[i].X+=dx;Town.Placed[i].Z+=dz;}
        Town.HouseX=x;Town.HouseZ=z;HousePlaced=true;X=arrivalX;Z=arrivalZ;if(relocate)Wallet-=50000;return true;
    }
    private bool HouseEntrance(int x,int z,out float arrivalX,out float arrivalZ)
    {
        arrivalX=x+3.5f;arrivalZ=z+4.5f;
        for(int row=4;row<13;row++)if(Town.Walkable(x+3,z+row)&&Town.FindGround(x+3,z+row,3)<0)
        {arrivalZ=z+row+.5f;return true;}
        return false;
    }
    public bool MoveToHouseEntrance()
    {if(!HousePlaced||!HouseEntrance(Town.HouseX,Town.HouseZ,out float x,out float z))return false;X=x;Z=z;return true;}
    public bool InGarden(int x,int z)=>HousePlaced&&x>=Town.HouseX&&x<Town.HouseX+GardenWidths[GardenTier]&&z>=Town.HouseZ&&z<Town.HouseZ+GardenHeights[GardenTier];
    public bool CanPlace(int id,int x,int z,int rotation,int room,int actor)
    {
        if(actor!=0||rotation<0||rotation>3||room<0||room>4||!Catalog.Has(id,ItemFlags.Place)||(!HousePlaced))return false;
        if(room==0&&!Catalog.Has(id,ItemFlags.Outdoor))return false;
        if((room==2&&HouseTier<3)||(room==3&&HouseTier<4)||(room==4&&HouseTier<5))return false;
        int w=Catalog.Items[id].Width,h=Catalog.Items[id].Height;if((rotation&1)!=0){int swap=w;w=h;h=swap;}
        int size=room==1?(HouseTier==0?4:HouseTier==1?6:8):room==3?8:6;
        for(int j=0;j<h;j++)for(int i=0;i<w;i++)
        {
            int tx=x+i,tz=z+j;
            if(room==0)
            {
                if(!InGarden(tx,tz)||Town.At(tx,tz)!=0||tx==Town.HouseX||tz==Town.HouseZ||tx==Town.HouseX+GardenWidths[GardenTier]-1||tz==Town.HouseZ+GardenHeights[GardenTier]-1)return false;
                if(tx>=Town.HouseX+2&&tx<=Town.HouseX+4&&tz>=Town.HouseZ+1&&tz<=Town.HouseZ+3)return false;
                if(tx==Town.HouseX+1&&tz==Town.HouseZ+3)return false;
            }
            else if(tx<0||tz<0||tx>=size||tz>=size)return false;
            for(int p=0;p<Town.PlacedCount;p++)
            {
                var q=Town.Placed[p];if(q.Room!=room)continue;
                int qw=Catalog.Items[q.Item].Width,qh=Catalog.Items[q.Item].Height;if((q.Rotation&1)!=0){int s=qw;qw=qh;qh=s;}
                if(tx>=q.X&&tx<q.X+qw&&tz>=q.Z&&tz<q.Z+qh)return false;
            }
        }
        return true;
    }
    public bool Place(int slot,int x,int z,int rotation,int room)
    {
        if(slot<0||slot>=Capacity||Town.PlacedCount==Town.Placed.Length||!CanPlace(Pockets[slot],x,z,rotation,room,0))return false;
        var p=Town.Placed[Town.PlacedCount++];p.Item=Pockets[slot];p.X=x;p.Z=z;p.Room=room;p.Rotation=rotation;p.Owner=0;Pockets[slot]=0;return true;
    }
    public bool Use(int slot,int action,int x,int z)
    {
        if(slot<0||slot>=Capacity||!Catalog.Valid(Pockets[slot]))return false;int id=Pockets[slot];
        if(action==0){if(!Town.AddGround(id,x,z,0,Town.Day))return false;}
        else if(action==1){if(!Catalog.Has(id,ItemFlags.Plant)||Town.At(x,z)!=0||!Town.AddGround(id,x,z,2,Town.Day))return false;}
        else if(action==2){if(!Catalog.Has(id,ItemFlags.Eat))return false;}
        else if(action==3){if(!Catalog.Has(id,ItemFlags.Wear))return false;Pockets[slot]=Shirt;Shirt=id;return true;}
        else if(action==4){if(!Catalog.Has(id,ItemFlags.Tool))return false;Pockets[slot]=Tool;Tool=id;return true;}
        else if(action==5){if(!Catalog.Has(id,ItemFlags.Bury)||Town.At(x,z)!=0||!Town.AddGround(id,x,z,1,Town.Day))return false;}
        else if(action==6){if(id<22||id>24||Catalog.Items[id].Buy>99999-Wallet)return false;Wallet+=Catalog.Items[id].Buy;}
        else return false;
        Pockets[slot]=0;return true;
    }
    public bool Pickup(int x,int z)
    {
        int i=Town.FindGround(x,z,0);if(i<0||!Receive(Town.Ground[i].Item))return false;Town.RemoveGround(i);return true;
    }
    public void CreditTutorial(int step)
    { if(Tutorial==step&&HousePlaced){Tutorial++;Debt=Math.Max(0,Debt-1000);} }
    public bool CanWalk(float x,float z,int room)
    {
        int tx=(int)x,tz=(int)z;
        if(room!=0){int size=HouseTier==0?4:HouseTier==1?6:8;return x>=.2f&&z>=.2f&&x<size-.2f&&z<size-.2f;}
        if(!Town.Walkable(tx,tz)||x<.2f||z<.2f)return false;
        if(HousePlaced&&tx>=Town.HouseX+2&&tx<=Town.HouseX+4&&tz>=Town.HouseZ+1&&tz<=Town.HouseZ+3)return false;
        int g=Town.FindGround(tx,tz,3);if(g>=0)return false;
        return true;
    }
    public string Talk(int index)
    {
        if(index<0||index>=6)return "Hello!";var v=Town.Residents[index];
        MetMask|=1<<index;v.Met=1;
        if(Tutorial==2&&Find(18)>=0){Pockets[Find(18)]=0;CreditTutorial(2);return "My order! Please collect "+Town.Residents[4].Name+"'s parcel.";}
        if(Tutorial==3&&index==4&&Receive(18))return "Please take this parcel to the store.";
        if(Tutorial==5&&MetMask==63){CreditTutorial(5);return "You have met everyone. Welcome home!";}
        if(v.Request!=0&&Find(v.Request)>=0)
        { Pockets[Find(v.Request)]=0;Wallet+=Wallet<=98999?1000:0;v.Friendship=Math.Min(255,v.Friendship+8);v.Request=0;return "Just what I wanted! Thank you, "+v.Phrase+"!"; }
        int count=0;for(int i=0;i<6;i++)if(Town.Residents[i].Request!=0)count++;
        if(v.Request==0&&count<3){v.Request=index%2==0?1:14;v.RequestDay=Town.Day;return "Could you bring me a "+Catalog.Items[v.Request].Name+"?";}
        return "A lovely day in our town, "+v.Phrase+".";
    }
    public bool Order(int id)
    {
        if(!Catalog.Valid(id)||Owned[id]==0||Catalog.Items[id].Buy<=0||Wallet<Catalog.Items[id].Buy||MailCount>=Mail.Length)return false;
        Wallet-=Catalog.Items[id].Buy;Mail[MailCount++]=-id;return true;
    }
    public void Rollover(int day,int month)
    {
        // A cropped save may be months old. Start recovered possessions' full
        // retention window on the first calendar update, before catch-up expiry.
        for(int i=0;i<Town.GroundCount;i++)if(Town.Ground[i].Kind==4&&Town.Ground[i].Day==GroundItem.PendingRecoveryDay)Town.Ground[i].Day=Math.Max(day,Town.Day);
        if(day<=Town.Day)return;
        if(Town.Month>=0&&month>Town.Month&&Savings<=long.MaxValue-Savings/200)Savings+=Savings/200;
        Town.Month=month;Town.Day=day;Jobs=0;JobDay=day;
        if(PendingKind!=0&&day>=PendingDay){if(PendingKind==1)HouseTier=PendingTier;else GardenTier=PendingTier;PendingKind=0;}
        Town.StockCount=Town.Spending>=100000?12:Town.Spending>=25000?9:6;
        for(int i=0;i<Town.StockCount;i++)Town.Stock[i]=i<5?i+5:10+(day+i)%4;
        for(int i=0;i<MailCount;i++)if(Mail[i]<0)Mail[i]=-Mail[i];
        for(int i=0;i<6;i++){var v=Town.Residents[i];if(v.Request!=0){v.Friendship=Math.Max(0,v.Friendship-2);v.Request=0;}}
        for(int i=Town.GroundCount-1;i>=0;i--)
        {
            var g=Town.Ground[i];
            if(g.Kind==0&&!InGarden(g.X,g.Z)){g.Kind=4;g.Day=day;}
            else if(g.Kind==4&&day-g.Day>=7)Town.RemoveGround(i);
            else if(g.Kind==2&&g.Item!=2&&day-g.Day>=3)g.Kind=3;
        }
        Town.AddGround(16,8+day%20,45,1,day);Town.AddGround(12,55+day%15,42,1,day);
    }
    public bool ClaimLost(int index,int actor)
    {
        if(index<0||index>=Town.GroundCount)return false;var g=Town.Ground[index];
        if(g.Kind!=4||(actor!=g.Owner&&(g.Day==GroundItem.PendingRecoveryDay||Town.Day-g.Day<3))||!Receive(g.Item))return false;Town.RemoveGround(index);return true;
    }
}
