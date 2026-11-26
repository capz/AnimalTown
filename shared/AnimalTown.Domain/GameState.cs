namespace AnimalTown;

public sealed partial class GameState
{
    // A recipient projection has no other player's private records and can
    // never be published as a card, even if a caller bypasses the online UI.
    public bool IsOnlineProjection {get;internal set;}
    // The local copy of a hosted shop is escrow, until synchronized with its
    // writer. This is a session capability, never a serialized public field.
    public bool HostedCommerceLocked {get;set;}
    public Jobs.JobIdentity[]? OnlineResidents {get;internal set;}
    public readonly Town Town;
    public const int MaximumPockets=30;
    public const int WalletLimit=99999;
    public readonly int[] Pockets=new int[MaximumPockets],Owned=new int[64],Mail=new int[128];
    public int BackpackTier;
    public int Capacity=>(BackpackTier+1)*10;
    public int BackpackUpgradePrice=>BackpackTier==0?6000:BackpackTier==1?18000:0;
    public int Wallet,Species,Palette,Tool,Shirt,HouseTier,GardenTier,PendingKind,PendingTier,PendingDay;
    public int Tutorial,MetMask,Jobs,JobDay=-1,MailCount,Sequence,Roof,Wall,Fence,Mailbox,Exterior;
    public long Savings,Debt=24000;
    public float X=Town.PlayerStartX,Z=Town.PlayerStartZ;
    public bool HousePlaced;
    public string Name="Newcomer",Catchphrase="hello there";
    public static readonly int[] HousePrices={18000,120000,320000,650000,980000,1600000};
    public static readonly int[] GardenPrices={0,40000,150000,400000,900000};
    public static readonly int[] GardenWidths={7,9,11,13,15},GardenHeights={5,7,9,11,13};
    public GameState(int seed){Town=new Town(seed);BuildingTypes[7]=1;Pockets[0]=Catalog.WorkShirt;Pockets[1]=2;Pockets[2]=18;}
    internal GameState(int seed,Town snapshot){Town=new Town(seed,snapshot);BuildingTypes[7]=1;Pockets[0]=Catalog.WorkShirt;Pockets[1]=2;Pockets[2]=18;}
    public int FreeSlot(){for(int i=0;i<Capacity;i++)if(Pockets[i]==0)return i;return -1;}
    public int Find(int item){for(int i=0;i<Capacity;i++)if(Pockets[i]==item)return i;return -1;}
    public bool UpgradeBackpack(bool free=false)
    { int cost=BackpackUpgradePrice;if(cost==0||!free&&Wallet<cost)return false;if(!free)Wallet-=cost;BackpackTier++;return true; }
    public int GiveMoney(int amount,bool forTesting)
    { if(!forTesting||amount<=0||Wallet>=99999)return 0;int added=Math.Min(amount,99999-Wallet);Wallet+=added;return added; }
    public bool Receive(int item)
    { int s=FreeSlot();if(s<0||!Catalog.Valid(item))return false;Pockets[s]=item;Owned[item]=1;return true; }
    public bool Buy(int stock,bool free=false)
        =>Town.Shop.Buy(this,this,ShopSource.Display,stock,free:free);
    public bool BuyCabinet(int slot,bool free=false)=>Town.Shop.Buy(this,this,ShopSource.Cabinet,slot,free:free);
    public bool Sell(int slot)
    {
        if(slot<0||slot>=Capacity||IsJobItem(slot))return false;int id=Pockets[slot];
        if(!Catalog.Has(id,ItemFlags.Sell)||Catalog.Items[id].Sell>WalletLimit-Wallet)return false;
        Wallet+=Catalog.Items[id].Sell;Pockets[slot]=0;return true;
    }
    public bool Deposit(int amount)
    { if(amount<=0||amount>Wallet||Savings>long.MaxValue-amount)return false;Wallet-=amount;Savings+=amount;return true; }
    public bool Withdraw(int amount)
    { if(amount<=0||amount>Savings||amount>WalletLimit-Wallet)return false;Savings-=amount;Wallet+=amount;return true; }
    public bool Repay(int amount,bool free=false)
    { if(amount<=0||!free&&amount>Wallet||amount>Debt)return false;if(!free)Wallet-=amount;Debt-=amount;return true; }
    public bool Expand(bool garden,int day,bool free=false)
        =>Expand(garden,day,out _,free);
    public bool Expand(bool garden,int day,out string reason,bool free=false)
    {
        reason=Visiting||!HousePlaced?"Place your own home before expanding.":
            !free&&Debt!=0?"Pay off your debt before expanding.":
            PendingKind!=0?"There is already construction waiting to finish.":"";
        if(reason.Length!=0)return false;
        int next=(garden?GardenTier:HouseTier)+1;
        if(garden?(next>4||HouseTier<next):(next>5)){
            reason=garden?"Upgrade your house before expanding the garden again.":"No house expansion is available yet.";return false;
        }
        if(garden&&!CanExpandGarden(next,out reason))return false;
        reason="";
        if(!free)Debt=garden?GardenPrices[next]:HousePrices[next];PendingKind=garden?2:1;PendingTier=next;PendingDay=day+1;return true;
    }
    public bool PlaceHouse(int x,int z,bool relocate,bool free=false)
        =>PlaceHouse(x,z,relocate,free,null,out _);
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
    public bool CanPlace(int id,int x,int z,int rotation,int room,int actor,bool existing=false)
        =>PlacementBlock(id,x,z,rotation,room,actor,existing)==null;
    internal bool CanPlaceOwned(int id,int x,int z,int rotation,int room,int owner,bool existing=false,int ignoredPlacement=-1)
        =>PlacementBlock(id,x,z,rotation,room,owner,existing,ignoredPlacement,true)==null;
    private string? PlacementBlock(int id,int x,int z,int rotation,int room,int actor,bool existing,int ignoredPlacement=-1,bool exactOwner=false)
    {
        if(rotation<0||rotation>3)return "Rotate the item before placing it.";
        if(!BuildingWorld.ValidSpace(room))return "You can't place furniture here.";
        if(!Catalog.Has(id,ItemFlags.Place))return "You can't place that item as furniture.";
        int profile=existing||exactOwner?OwnerProfile(actor):actor==0?ActiveProfile:OwnerProfile(actor);
        if(!existing&&actor>=1&&actor<=6)profile=-1;
        bool ownPlayer=profile>=0&&HomePlaced(profile)&&(room==0||BuildingWorld.RoomProfile(room)==profile);
        bool ownResident=actor>=1&&actor<=6&&room==15+actor;
        if(!ownPlayer&&!ownResident)return "You can only place furniture in your own home or garden.";
        if(room==0&&!Catalog.Has(id,ItemFlags.Outdoor))return "This furniture belongs indoors.";
        int personalRoom=BuildingWorld.PersonalRoom(room),homeTier=profile>=0?HomeTier(profile):0;
        if(ownPlayer&&((personalRoom==2&&homeTier<3)||(personalRoom==3&&homeTier<4)||(personalRoom==4&&homeTier<5)))return "This room isn't available yet.";
        int w=Catalog.Items[id].Width,h=Catalog.Items[id].Height;if((rotation&1)!=0){int swap=w;w=h;h=swap;}
        if(room!=0&&!existing&&!BuildingWorld.Available(this,room))return "This room isn't available yet.";
        int size=room==0?0:existing&&!BuildingCatalog.Available(BuildingTypes[BuildingWorld.Building(room)])?16:BuildingWorld.Size(this,room);
        for(int j=0;j<h;j++)for(int i=0;i<w;i++)
        {
            int tx=x+i,tz=z+j;
            if(room==0)
            {
                if(!GardenContains(profile,tx,tz))return "The whole item needs to fit inside your garden.";
                if(tx==HomeX(profile)||tz==HomeZ(profile)||tx==HomeX(profile)+GardenWidths[HomeGardenTier(profile)]-1||tz==HomeZ(profile)+GardenHeights[HomeGardenTier(profile)]-1)
                    return "The item would overlap your garden fence.";
                if(tx>=HomeX(profile)+2&&tx<=HomeX(profile)+4&&tz>=HomeZ(profile)+1&&tz<=HomeZ(profile)+3)return "The item would overlap your house.";
                if(tx==HomeX(profile)+1&&tz==HomeZ(profile)+3)return "The item would block your mailbox.";
                if(Town.At(tx,tz)!=0)return "Outdoor furniture needs a clear grass tile.";
            }
            else
            {
                if(tx<0||tz<0||tx>=size||tz>=size)return "The whole item needs to fit inside the room.";
                if(!existing&&BuildingWorld.DoorClearance(size,tx,tz,1,1))return "Keep the doorway clear.";
            }
            for(int p=0;p<Town.PlacedCount;p++)
            {
                if(p==ignoredPlacement)continue;
                var q=Town.Placed[p];if(q.Room!=room)continue;
                BuildingWorld.Footprint(q,out int qw,out int qh);
                if(tx>=q.X&&tx<q.X+qw&&tz>=q.Z&&tz<q.Z+qh)return "There's already furniture in that spot.";
            }
        }
        return null;
    }
    public bool Place(int slot,int x,int z,int rotation,int room)
        =>Place(slot,x,z,rotation,room,out _);

    public bool Place(int slot,int x,int z,int rotation,int room,out string reason)
    {
        reason=slot<0||slot>=Capacity||Pockets[slot]==0?"That pocket is empty.":
            IsJobItem(slot)?"Keep that item in your pockets for the errand.":
            Town.PlacedCount==Town.Placed.Length?"Your town has reached its furniture limit. Pick something up first.":
            (Visiting?null:PlacementBlock(Pockets[slot],x,z,rotation,room,0,false))??"";
        if(reason.Length!=0)return false;
        if(TownItemActions.Place(this,this,TownItemActor.ForActivePlayer(this),slot,x,z,rotation,room)){Town.NotifyObjectsChanged();return true;}
        reason="That spot is blocked. Choose a clear place for the item.";return false;
    }
    public bool Use(int slot,int action,int x,int z)
        =>Use(slot,action,x,z,out _);

    internal bool Use(int slot,int action,int x,int z,out string reason)
    {
        reason="";
        if(slot<0||slot>=Capacity||IsJobItem(slot)||!Catalog.Valid(Pockets[slot]))return false;int id=Pockets[slot];
        if(action==0||action==1||action==5){
            int kind=action==0?0:action==1?2:1;
            reason=Town.GroundCount==Town.Ground.Length?"The town has no room for more items on the ground. Pick something up first.":
                kind!=0&&Town.At(x,z)!=0?"Find a clear grass tile to plant or bury this.":
                !Town.Walkable(x,z)?"Choose a spot on walkable ground.":
                Town.FindGround(x,z)>=0?"There's already something on the ground there.":"";
            if(reason.Length!=0)return false;
            if(TownItemActions.AddGround(this,this,TownItemActor.ForActivePlayer(this),slot,x,z,kind))return true;
            reason="That spot is blocked. Choose a clear spot on the ground.";return false;
        }
        else if(action==2){if(!Catalog.Has(id,ItemFlags.Eat))return false;}
        else if(action==3){if(!Catalog.Has(id,ItemFlags.Wear))return false;Pockets[slot]=Shirt;Shirt=id;return true;}
        else if(action==4){if(!Catalog.Has(id,ItemFlags.Tool))return false;Pockets[slot]=Tool;Tool=id;return true;}
        else if(action==7){if(!Town.Jobs.Initialized||BuildingWorld.RoomProfile(Space)!=ActiveProfile||!Catalog.Has(id,ItemFlags.Flooring)&&!Catalog.Has(id,ItemFlags.Wallpaper))return false;bool floor=Catalog.Has(id,ItemFlags.Flooring);int old=floor?Town.Jobs.HomeFloors[ActiveProfile]:Town.Jobs.HomeWalls[ActiveProfile];if(floor)Town.Jobs.HomeFloors[ActiveProfile]=id;else Town.Jobs.HomeWalls[ActiveProfile]=id;Pockets[slot]=old;Town.Jobs.Revision++;return true;}
        else if(action==6){int amount=Catalog.MoneyValue(id);if(amount<=0||amount>WalletLimit-Wallet)return false;Wallet+=amount;}
        else return false;
        Pockets[slot]=0;return true;
    }
    public bool Pickup(int x,int z)
    {
        int index=Town.FindGround(x,z,0);
        var actor=TownItemActor.ForActivePlayer(this);
        if(Visiting)return TownItemActions.RemoveGround(this,this,actor,index);
        if(!actor.CanClaim(this,this))return false;
        if(index<0||!Receive(Town.Ground[index].Item))return false;
        var item=Town.Ground[index];
        const int distance=3*Town.ScreenTileSize;
        uint seed=unchecked((uint)Town.Seed*1664525u+(uint)(z*Town.Width+x)*2246822519u+(uint)Town.Day*3266489917u);
        int cell=(int)((seed^(seed>>16))%Town.Cells);
        for(int scanned=0;scanned<Town.Cells;scanned++){
            int tx=cell%Town.Width,tz=cell/Town.Width;
            // Coprime to the town's cell count, so the scan spreads across
            // rows and still visits every cell if suitable ground is scarce.
            cell=(cell+109)%Town.Cells;
            float dx=tx+.5f-X,dz=tz+.5f-Z;
            if(dx*dx+dz*dz<distance*distance||tx/Town.ScreenTileSize==x/Town.ScreenTileSize&&tz/Town.ScreenTileSize==z/Town.ScreenTileSize)continue;
            if(item.Item==17&&Town.At(tx,tz)!=(int)Terrain.Beach)continue;
            if(AnyGardenContains(tx,tz)||!CanWalk(tx+.5f,tz+.5f,0)||Town.FindGround(tx,tz)>=0)continue;
            item.X=tx;item.Z=tz;item.Day=Town.Day;item.Wrapped=false;
            Town.NotifyCastersChanged();
            return true;
        }
        Town.RemoveGround(index);
        return true;
    }
    public void CreditTutorial(int step)
    { if(Tutorial==step&&HousePlaced){Tutorial++;Debt=Math.Max(0,Debt-1000);} }
    public bool CanWalk(float x,float z,int room)
    {
        int tx=(int)x,tz=(int)z;
        if(room!=0)return BuildingWorld.CanStand(this,room,x,z);
        if(!Town.Walkable(tx,tz)||x<.2f||z<.2f)return false;
        if(HomeBlocks(tx,tz))return false;
        int g=Town.FindGround(tx,tz,3);if(g>=0)return false;
        for(int i=0;i<Town.PlacedCount;i++){var p=Town.Placed[i];if(p.Room!=0)continue;BuildingWorld.Footprint(p,out int w,out int d);if(x>p.X-BuildingWorld.Radius&&x<p.X+w+BuildingWorld.Radius&&z>p.Z-BuildingWorld.Radius&&z<p.Z+d+BuildingWorld.Radius)return false;}
        return true;
    }
    public string Talk(int index)
    {
        if(!ResidentPresent(index))return "Hello!";var v=Town.Residents[index];
        MetMask|=1<<index;v.Met=1;
        int neighbours=(1<<(Town.VisitorRoster?Town.LocalResidentCount:Town.SavedVillagerCount))-1;
        if(Town.Jobs.Initialized){
            if(ResidentJobs.OpeningDelivery(index))return "My order! Please collect "+Town.Residents[4].Name+"’s parcel.";
            if(Tutorial==3&&index==4)return ResidentJobs.OpeningPickup(index)?"Please take this parcel to the store.":"Make room for the store’s parcel first.";
            if(Tutorial==5&&(MetMask&neighbours)==neighbours){CreditTutorial(5);return "Everyone knows you now! Leave a message on the notice board beside town hall.";}
            return "A lovely day in our town, "+v.Phrase+".";
        }
        if(Tutorial==2&&Find(18)>=0){Pockets[Find(18)]=0;CreditTutorial(2);return "My order! Please collect "+Town.Residents[4].Name+"'s parcel.";}
        if(Tutorial==3&&index==4&&Receive(18))return "Please take this parcel to the store.";
        if(Tutorial==5&&(MetMask&neighbours)==neighbours){CreditTutorial(5);return "You have met everyone. Welcome home!";}
        if(v.Request!=0&&Find(v.Request)>=0)
        { Pockets[Find(v.Request)]=0;Wallet+=Wallet<=98999?1000:0;v.Friendship=Math.Min(255,v.Friendship+8);v.Request=0;return "Just what I wanted! Thank you, "+v.Phrase+"!"; }
        int count=0;for(int i=0;i<6;i++)if(Town.Residents[i].Request!=0)count++;
        if(v.Request==0&&count<3&&AllowsFreshResidentJobs(index)){v.Request=index%2==0?1:14;v.RequestDay=Town.Day;return "Could you bring me a "+Catalog.Items[v.Request].Name+"?";}
        return "A lovely day in our town, "+v.Phrase+".";
    }
    public bool Order(int id)
    {
        if(Visiting||!Catalog.Valid(id)||Owned[id]==0||Catalog.Items[id].Buy<=0||Wallet<Catalog.Items[id].Buy||MailCount>=Mail.Length)return false;
        Wallet-=Catalog.Items[id].Buy;Mail[MailCount++]=-id;return true;
    }
    public void Rollover(int day,int month)
    {
        // Older recovery markers receive a date, but police custody never
        // expires. A long absence must not destroy visitor-removed possessions.
        for(int i=0;i<Town.GroundCount;i++)if(Town.Ground[i].Kind==4&&Town.Ground[i].RecoveryId==0&&Town.Ground[i].Day==GroundItem.PendingRecoveryDay)Town.Ground[i].Day=Math.Max(day,Town.Day);
        // Old same-day stock can contain the retired tool. Preserve every item ID
        // in saves, but never offer a watering can as a new purchase.
        for(int i=0;i<Town.StockCount;i++)if(Town.Stock[i]==Catalog.LegacyWateringCan)Town.Stock[i]=10+(Math.Max(Town.Day,0)%4+i)%4;
        if(day<=Town.Day)return;
        CaptureProfile();
        for(int p=0;p<ProfileCount;p++)if(Profiles[p].Created){CompleteGardenExpansion(p,day);Profiles[p].Rollover(day,month,Town.Month);}
        RestoreProfile(ActiveProfile);Town.Month=month;Town.Day=day;
        int saleSlots=TownShop.SaleSlots(this);
        if(!HostedCommerceLocked){
            Town.StockCount=Town.Spending>=100000?12:Town.Spending>=25000?9:6;
            for(int i=0;i<Town.StockCount;i++)Town.Stock[i]=10+(day+i)%4;
            Town.Shop.NewDay();
        }
        for(int i=Town.GroundCount-1;i>=0;i--)
        {
            var g=Town.Ground[i];
            if(g.Kind==0&&!AnyGardenContains(g.X,g.Z)){g.Kind=4;g.Day=day;}
            else if(g.Kind==2&&g.Item!=2&&day-g.Day>=3){g.Kind=3;Town.NotifyCastersChanged();}
        }
        Town.AddGround(16,8+day%20,45,1,day);Town.AddGround(12,55+day%15,42,1,day);
        if(saleSlots!=TownShop.SaleSlots(this))NormalizeBuildingLocations();
    }
    public bool ClaimLost(int index,int actor)
        =>actor==ActiveProfile&&TownItemActions.ClaimLost(this,this,TownItemActor.ForActivePlayer(this),index);
}
