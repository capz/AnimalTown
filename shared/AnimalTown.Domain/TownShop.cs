namespace AnimalTown;

public enum ShopSource { Display, Cabinet, Consignment }

// Shared by the console and service. No models, network state or per-frame allocations.
public sealed class TownShop
{
    public const int DisplayCount=12,SaleCount=7,NpcCapacity=16,Room=24;
    // Keep the original eight serialized cells so older listings can be reclaimed.
    public const int StoredSaleCount=8;
    public static int SaleSlots(GameState host)=>host.Town.StockCount>=12?7:host.Town.StockCount>=9?5:3;
    private static readonly int[] essentials={1,2,3,4,5,6,7,9};
    public static int CabinetCount=>essentials.Length;
    public static int CabinetItem(int index)=>(uint)index<essentials.Length?essentials[index]:0;
    public readonly int[] SaleItems=new int[StoredSaleCount],SalePrices=new int[StoredSaleCount],SaleOwners=new int[StoredSaleCount];
    public readonly long[] Proceeds=new long[GameState.ProfileCount];
    public readonly int[] NpcWallet=new int[6],NpcCount=new int[6],NpcDay=new int[6],NpcTarget=new int[6],NpcItems=new int[6*NpcCapacity];
    public int Revision;
    public TownShop(){for(int i=0;i<6;i++){NpcWallet[i]=6000;NpcDay[i]=NpcTarget[i]=-1;}}
    public int Item(GameState host,ShopSource source,int slot)=>source==ShopSource.Cabinet?CabinetItem(slot):source==ShopSource.Display?(uint)slot<host.Town.StockCount?host.Town.Stock[slot]:0:source==ShopSource.Consignment&&(uint)slot<SaleSlots(host)?SaleItems[slot]:0;
    public int Price(GameState host,ShopSource source,int slot)
    {int item=Item(host,source,slot);return !Catalog.Valid(item)?0:source==ShopSource.Consignment?SalePrices[slot]:Catalog.Items[item].Buy;}
    public static bool Sellable(int item)=>Catalog.Has(item,ItemFlags.Sell)&&!Catalog.Has(item,ItemFlags.Money)&&Catalog.Items[item].Sell>0;
    public bool List(GameState host,int slot,int pocket,int price)
    {
        if((uint)slot>=SaleSlots(host)||SaleItems[slot]!=0||(uint)pocket>=host.Capacity||!Sellable(host.Pockets[pocket])||price<1||price>GameState.WalletLimit)return false;
        SaleItems[slot]=host.Pockets[pocket];SalePrices[slot]=price;SaleOwners[slot]=host.ActiveProfile;host.Pockets[pocket]=0;Changed();return true;
    }
    public bool Remove(GameState host,int slot)
    {
        if((uint)slot>=StoredSaleCount||SaleItems[slot]==0||SaleOwners[slot]!=host.ActiveProfile||!host.Receive(SaleItems[slot]))return false;
        ClearSale(slot);Changed();return true;
    }
    public bool Reprice(GameState host,int slot,int price)
    {if((uint)slot>=SaleSlots(host)||SaleItems[slot]==0||SaleOwners[slot]!=host.ActiveProfile||price<1||price>GameState.WalletLimit)return false;SalePrices[slot]=price;Changed();return true;}
    public int StoredListingCount(GameState host)
    {int count=0;for(int i=SaleSlots(host);i<StoredSaleCount;i++)if(SaleItems[i]!=0&&SaleOwners[i]==host.ActiveProfile)count++;return count;}
    public int StoredListing(GameState host,int index)
    {for(int i=SaleSlots(host);i<StoredSaleCount;i++)if(SaleItems[i]!=0&&SaleOwners[i]==host.ActiveProfile&&index--==0)return i;return -1;}
    // A guest supplies their own private GameState. Only the host stock is touched.
    // Expected revision prevents a stale selection buying a newly replaced listing.
    public bool Buy(GameState host,GameState buyer,ShopSource source,int slot,int expectedRevision=-1,bool free=false)
    {
        int item=Item(host,source,slot),price=Price(host,source,slot);
        bool waive=free&&source!=ShopSource.Consignment;
        if(expectedRevision>=0&&expectedRevision!=Revision||price<=0||!Catalog.Valid(item)||buyer.FreeSlot()<0||!waive&&buyer.Wallet<price||!CanCredit(source,slot,price))return false;
        if(!buyer.Receive(item))return false;
        if(!waive)buyer.Wallet-=price;
        Complete(host,source,slot,price);return true;
    }
    public bool BuyNpc(GameState host,int npc,ShopSource source,int slot,int expectedRevision=-1)
    {
        if((uint)npc>=6)return false;int item=Item(host,source,slot),price=Price(host,source,slot);
        if(expectedRevision>=0&&expectedRevision!=Revision||price<=0||!Catalog.Valid(item)||NpcCount[npc]>=NpcCapacity||NpcWallet[npc]<price||!CanCredit(source,slot,price))return false;
        NpcWallet[npc]-=price;NpcItems[npc*NpcCapacity+NpcCount[npc]++]=item;NpcDay[npc]=host.Town.Day;
        Complete(host,source,slot,price);return true;
    }
    private bool CanCredit(ShopSource source,int slot,int price)=>source!=ShopSource.Consignment||Proceeds[SaleOwners[slot]]<=long.MaxValue-price;
    private void Complete(GameState host,ShopSource source,int slot,int price)
    {
        if(source==ShopSource.Display)host.Town.Stock[slot]=0;
        if(source!=ShopSource.Consignment)host.Town.Spending=(int)Math.Min(int.MaxValue,(long)host.Town.Spending+price);
        if(source==ShopSource.Consignment){int owner=SaleOwners[slot];Proceeds[owner]+=price;ClearSale(slot);ClaimProceeds(host,owner);}
        if(source!=ShopSource.Cabinet)Changed();
    }
    public void ClaimProceeds(GameState host,int profile)
    {
        // Keep earnings in the town ledger while the seller's character card is absent.
        if(!host.Profiles[profile].Created)return;
        long savings=profile==host.ActiveProfile?host.Savings:host.Profiles[profile].Savings,amount=Math.Min(Proceeds[profile],long.MaxValue-savings);
        if(amount<=0)return;
        if(profile==host.ActiveProfile)host.Savings+=amount;else host.Profiles[profile].Savings+=amount;
        Proceeds[profile]-=amount;
    }
    public void DeleteSeller(int profile)
    {for(int i=0;i<StoredSaleCount;i++)if(SaleOwners[i]==profile)ClearSale(i);Proceeds[profile]=0;Changed();}
    private void ClearSale(int slot){SaleItems[slot]=SalePrices[slot]=SaleOwners[slot]=0;}
    private void Changed(){Revision=Revision==int.MaxValue?0:Revision+1;}
    public void NewDay(){for(int i=0;i<6;i++)NpcWallet[i]=Math.Min(20000,NpcWallet[i]+1000);Changed();}
    public int ChooseNpcDisplay(GameState host,int npc)
    {
        if(NpcCount[npc]>=NpcCapacity||NpcDay[npc]==host.Town.Day)return -1;
        int count=DisplayCount+SaleSlots(host);
        for(int n=0;n<count;n++){
            int index=(n+npc+Math.Max(host.Town.Day,0)%count)%count;
            var source=index<DisplayCount?ShopSource.Display:ShopSource.Consignment;int slot=index<DisplayCount?index:index-DisplayCount;
            int price=Price(host,source,slot);if(price>0&&price<=NpcWallet[npc])return index;
        }
        return -1;
    }
    // Optional bounded extension inside the existing checksummed shop record.
    public int[] Encode()
    {
        var data=new int[154];int at=0;data[at++]=0x31504853;data[at++]=Revision;
        for(int i=0;i<StoredSaleCount;i++){data[at++]=SaleItems[i];data[at++]=SalePrices[i];data[at++]=SaleOwners[i];}
        for(int i=0;i<4;i++){data[at++]=(int)Proceeds[i];data[at++]=(int)(Proceeds[i]>>32);}
        for(int i=0;i<6;i++){data[at++]=NpcWallet[i];data[at++]=NpcCount[i];data[at++]=NpcDay[i];data[at++]=NpcTarget[i];}
        for(int i=0;i<NpcItems.Length;i++)data[at++]=NpcItems[i];
        if(at!=data.Length)throw new InvalidOperationException("Shop save budget");return data;
    }
    public void Decode(int[] data)
    {
        if(data.Length!=154||data[0]!=0x31504853)throw new InvalidOperationException("Invalid shop save");int at=1;
        int Read(int lo,int hi){int n=data[at++];if(n<lo||n>hi)throw new InvalidOperationException("Invalid shop value");return n;}
        Revision=Read(0,int.MaxValue);
        for(int i=0;i<StoredSaleCount;i++){SaleItems[i]=Read(0,Catalog.Items.Length-1);SalePrices[i]=Read(0,99999);SaleOwners[i]=Read(0,3);if(SaleItems[i]==0?SalePrices[i]!=0||SaleOwners[i]!=0:!Sellable(SaleItems[i])||SalePrices[i]==0)throw new InvalidOperationException("Invalid shop listing");}
        for(int i=0;i<4;i++){uint low=(uint)data[at++];int high=Read(0,int.MaxValue);Proceeds[i]=((long)high<<32)|low;}
        for(int i=0;i<6;i++){NpcWallet[i]=Read(0,20000);NpcCount[i]=Read(0,NpcCapacity);NpcDay[i]=Read(-1,int.MaxValue);NpcTarget[i]=Read(-1,DisplayCount+StoredSaleCount-1);}
        for(int i=0;i<NpcItems.Length;i++){NpcItems[i]=Read(0,Catalog.Items.Length-1);if((i%NpcCapacity<NpcCount[i/NpcCapacity])!=(NpcItems[i]!=0))throw new InvalidOperationException("Invalid NPC pockets");}
    }
}

// One source for model pedestals, interaction hit tests, collision and NPC targets.
public static class ShopLayout
{
    public const int Cabinet=TownShop.DisplayCount+TownShop.SaleCount,Counter=Cabinet+1,FixtureCount=Counter+1;
    public const int CollisionCount=3+TownShop.SaleCount;
    public static bool Active(GameState host,int fixture)=>fixture>=0&&fixture<FixtureCount&&(fixture<TownShop.DisplayCount?fixture<host.Town.StockCount:fixture<Cabinet?fixture-TownShop.DisplayCount<TownShop.SaleSlots(host):true);
    public static int Inspect(GameState host,int index)
    {
        int displays=host.Town.StockCount,sales=TownShop.SaleSlots(host);index%=displays+sales+2;
        return index<displays?index:index<displays+sales?TownShop.DisplayCount+index-displays:Cabinet+index-displays-sales;
    }
    public static void Island(out float x,out float z,out float w,out float d,out float height)
    {x=2.1f;z=2.35f;w=3.8f;d=3.3f;height=.5f;}
    public static void CollisionBounds(int fixture,out float x,out float z,out float w,out float d,out float height)
    {
        if(fixture==0)Island(out x,out z,out w,out d,out height);
        else Bounds(fixture==1?Cabinet:fixture==2?Counter:TownShop.DisplayCount+fixture-3,out x,out z,out w,out d,out height);
    }
    public static float[] Fixtures()
    {
        var data=new float[CollisionCount*6];
        for(int i=0;i<CollisionCount;i++){
            CollisionBounds(i,out float x,out float z,out float w,out float d,out float h);int at=i*6;
            data[at]=x;data[at+1]=z;data[at+2]=w;data[at+3]=d;data[at+4]=h;data[at+5]=0;
        }
        return data;
    }
    public static void Bounds(int fixture,out float x,out float z,out float w,out float d,out float height)
    {
        w=d=.6f;height=.5f;
        if(fixture<8){x=2.35f+.9f*(fixture%4);z=fixture<4?2.4f:4.95f;}
        else if(fixture<12){x=fixture<10?2.15f:5.25f;z=3.35f+.85f*(fixture%2);}
        else if(fixture<Cabinet){
            int sale=fixture-TownShop.DisplayCount;
            if(sale==2){x=2.15f;z=.15f;}
            else{x=(sale<2?sale:sale-3)%2==0?.2f:7.2f;z=sale<2?2.8f:sale<5?4.4f:6;}
        }
        else if(fixture==Cabinet){x=.2f;z=.1f;w=1.5f;d=.6f;height=1.6f;}
        else{x=3;z=.1f;w=3.5f;d=.7f;height=.9f;}
    }
    public static void Approach(int fixture,out float x,out float z)
    {
        Bounds(fixture,out x,out z,out float w,out float d,out _);
        if(fixture==Cabinet){x=1.25f;z=1.1f;}
        else if(fixture<4){x+=w*.5f;z=1.85f;}
        else if(fixture<8){x+=w*.5f;z=6.15f;}
        else if(fixture<12){x=fixture<10?1.65f:6.35f;z+=d*.5f;}
        else if(fixture<Cabinet&&fixture!=14){x=x<4?1.3f:6.7f;z+=d*.5f;}
        else{x+=w*.5f;z+=d+.5f;}
    }
    public static int Near(GameState host,float x,float z)
    {
        int result=-1;float nearest=.85f*.85f;
        for(int i=0;i<FixtureCount;i++){if(!Active(host,i))continue;Approach(i,out float ax,out float az);float d=(x-ax)*(x-ax)+(z-az)*(z-az);if(d<nearest){nearest=d;result=i;}}
        return result;
    }
}
