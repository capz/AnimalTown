namespace AnimalTown;

public enum ShopSource { Display, Cabinet, Consignment }

// Shared by the console and service. No models, network state or per-frame allocations.
public sealed class TownShop
{
    public const int DisplayCount=12,SaleCount=8,NpcCapacity=16,Room=24;
    private static readonly int[] essentials={1,2,3,4,5,6,7,9};
    public static int CabinetCount=>essentials.Length;
    public static int CabinetItem(int index)=>(uint)index<essentials.Length?essentials[index]:0;
    public readonly int[] SaleItems=new int[SaleCount],SalePrices=new int[SaleCount],SaleOwners=new int[SaleCount];
    public readonly long[] Proceeds=new long[GameState.ProfileCount];
    public readonly int[] NpcWallet=new int[6],NpcCount=new int[6],NpcDay=new int[6],NpcTarget=new int[6],NpcItems=new int[6*NpcCapacity];
    public int Revision;
    public TownShop(){for(int i=0;i<6;i++){NpcWallet[i]=6000;NpcDay[i]=NpcTarget[i]=-1;}}
    public int Item(GameState host,ShopSource source,int slot)=>source==ShopSource.Cabinet?CabinetItem(slot):source==ShopSource.Display?(uint)slot<host.Town.StockCount?host.Town.Stock[slot]:0:source==ShopSource.Consignment&&(uint)slot<SaleCount?SaleItems[slot]:0;
    public int Price(GameState host,ShopSource source,int slot)
    {int item=Item(host,source,slot);return !Catalog.Valid(item)?0:source==ShopSource.Consignment?SalePrices[slot]:Catalog.Items[item].Buy;}
    public static bool Sellable(int item)=>Catalog.Has(item,ItemFlags.Sell)&&!Catalog.Has(item,ItemFlags.Money)&&Catalog.Items[item].Sell>0;
    public bool List(GameState host,int slot,int pocket,int price)
    {
        if((uint)slot>=SaleCount||SaleItems[slot]!=0||(uint)pocket>=host.Capacity||!Sellable(host.Pockets[pocket])||price<1||price>GameState.WalletLimit)return false;
        SaleItems[slot]=host.Pockets[pocket];SalePrices[slot]=price;SaleOwners[slot]=host.ActiveProfile;host.Pockets[pocket]=0;Changed();return true;
    }
    public bool Remove(GameState host,int slot)
    {
        if((uint)slot>=SaleCount||SaleItems[slot]==0||SaleOwners[slot]!=host.ActiveProfile||!host.Receive(SaleItems[slot]))return false;
        ClearSale(slot);Changed();return true;
    }
    public bool Reprice(GameState host,int slot,int price)
    {if((uint)slot>=SaleCount||SaleItems[slot]==0||SaleOwners[slot]!=host.ActiveProfile||price<1||price>GameState.WalletLimit)return false;SalePrices[slot]=price;Changed();return true;}
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
    {for(int i=0;i<SaleCount;i++)if(SaleOwners[i]==profile)ClearSale(i);Proceeds[profile]=0;Changed();}
    private void ClearSale(int slot){SaleItems[slot]=SalePrices[slot]=SaleOwners[slot]=0;}
    private void Changed(){Revision=Revision==int.MaxValue?0:Revision+1;}
    public void NewDay(){for(int i=0;i<6;i++)NpcWallet[i]=Math.Min(20000,NpcWallet[i]+1000);Changed();}
    public int ChooseNpcDisplay(GameState host,int npc)
    {
        if(NpcCount[npc]>=NpcCapacity||NpcDay[npc]==host.Town.Day)return -1;
        for(int n=0;n<DisplayCount+SaleCount;n++){
            int index=(n+npc+Math.Max(host.Town.Day,0))%(DisplayCount+SaleCount);
            var source=index<DisplayCount?ShopSource.Display:ShopSource.Consignment;int slot=index<DisplayCount?index:index-DisplayCount;
            int price=Price(host,source,slot);if(price>0&&price<=NpcWallet[npc])return index;
        }
        return -1;
    }
    // Optional bounded extension inside the existing checksummed shop record.
    public int[] Encode()
    {
        var data=new int[154];int at=0;data[at++]=0x31504853;data[at++]=Revision;
        for(int i=0;i<SaleCount;i++){data[at++]=SaleItems[i];data[at++]=SalePrices[i];data[at++]=SaleOwners[i];}
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
        for(int i=0;i<SaleCount;i++){SaleItems[i]=Read(0,Catalog.Items.Length-1);SalePrices[i]=Read(0,99999);SaleOwners[i]=Read(0,3);if(SaleItems[i]==0?SalePrices[i]!=0||SaleOwners[i]!=0:!Sellable(SaleItems[i])||SalePrices[i]==0)throw new InvalidOperationException("Invalid shop listing");}
        for(int i=0;i<4;i++){uint low=(uint)data[at++];int high=Read(0,int.MaxValue);Proceeds[i]=((long)high<<32)|low;}
        for(int i=0;i<6;i++){NpcWallet[i]=Read(0,20000);NpcCount[i]=Read(0,NpcCapacity);NpcDay[i]=Read(-1,int.MaxValue);NpcTarget[i]=Read(-1,DisplayCount+SaleCount-1);}
        for(int i=0;i<NpcItems.Length;i++){NpcItems[i]=Read(0,Catalog.Items.Length-1);if((i%NpcCapacity<NpcCount[i/NpcCapacity])!=(NpcItems[i]!=0))throw new InvalidOperationException("Invalid NPC pockets");}
    }
}

// One source for model pedestals, interaction hit tests, collision and NPC targets.
public static class ShopLayout
{
    public const int Cabinet=20,Counter=21,FixtureCount=22;
    public static float[] Fixtures()
    {
        var data=new float[FixtureCount*6];
        for(int i=0;i<FixtureCount;i++){
            Bounds(i,out float x,out float z,out float w,out float d,out float h);int at=i*6;
            data[at]=x;data[at+1]=z;data[at+2]=w;data[at+3]=d;data[at+4]=h;data[at+5]=0;
        }
        return data;
    }
    public static void Bounds(int fixture,out float x,out float z,out float w,out float d,out float height)
    {
        w=d=.6f;height=.45f;
        if(fixture<12){x=1.5f+1.4f*(fixture%4);z=1.8f+1.7f*(fixture/4);}
        else if(fixture<20){x=fixture<16?.15f:7.25f;z=1.1f+1.35f*((fixture-12)%4);}
        else if(fixture==Cabinet){x=.2f;z=.1f;w=1.5f;d=.6f;height=1.6f;}
        else{x=3;z=.1f;w=3.5f;d=.7f;height=.9f;}
    }
    public static void Approach(int fixture,out float x,out float z)
    {
        Bounds(fixture,out x,out z,out float w,out float d,out _);
        if(fixture==Cabinet){x=1.25f;z=1.1f;}
        else if(fixture>=12&&fixture<20){x=fixture<16?1.05f:6.95f;z+=d*.5f;}
        else{x+=w*.5f;z+=d+.5f;}
    }
    public static int Near(float x,float z)
    {
        int result=-1;float nearest=.85f*.85f;
        for(int i=0;i<FixtureCount;i++){Approach(i,out float ax,out float az);float d=(x-ax)*(x-ax)+(z-az)*(z-az);if(d<nearest){nearest=d;result=i;}}
        return result;
    }
}
