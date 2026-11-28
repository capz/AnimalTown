using AnimalTown.Jobs;

namespace AnimalTown.Online;

// Three-way reconciliation starts with the actual uploaded offline card. The
// caller owns a disposable candidate and publishes only after every check has
// succeeded. The baseline is the last card read back and acknowledged, never a
// newer backend snapshot that the console has not saved.
public static class OnlineTownMerge
{
    public static void Apply(GameState local,GameState baseline,GameState hosted,long now,OnlineResidentDecisions? residents=null)
    {
        ulong town=baseline.Town.Jobs.TownId;
        if(town==0||local.Town.Jobs.TownId!=town||hosted.Town.Jobs.TownId!=town||
            local.Visiting||baseline.Visiting||hosted.Visiting||local.IsOnlineProjection||baseline.IsOnlineProjection||hosted.IsOnlineProjection)
            throw new InvalidOperationException("These cards do not share one home lineage.");
        local.CaptureProfile();baseline.CaptureProfile();hosted.CaptureProfile();
        if(residents!=null)residents.Validate(local,baseline,hosted);
        else if(OnlineResidentDecisions.Changes(local,baseline)!=0||OnlineResidentDecisions.Changes(hosted,baseline)!=0)
            throw new InvalidOperationException("Review the offline resident changes before synchronizing.");
        var earned=new long[GameState.ProfileCount];
        for(int p=0;p<GameState.ProfileCount;p++)
        {
            var before=baseline.Profiles[p];var remote=hosted.Profiles[p];
            // Without an active card reservation, only passive consignment
            // earnings can change private currency. Never replay a remote
            // pocket debit over independently edited offline pockets.
            if(remote.Wallet!=before.Wallet||remote.Savings<before.Savings||!Equal(remote.Pockets,before.Pockets))
                throw new InvalidOperationException("Finish the pending online return before synchronizing.");
            earned[p]=remote.Savings-before.Savings;
        }

        bool localShop=ShopChanged(local,baseline),remoteShop=ShopChanged(hosted,baseline);
        if(residents!=null)MergeReviewedShop(local,baseline,hosted,residents);
        else if(localShop&&remoteShop&&!SameShop(local,hosted))
            throw new InvalidOperationException("The shared shop changed. Keep the card for reconciliation.");
        else if(remoteShop||!localShop)
        {
            local.Town.Shop.Decode(hosted.Town.Shop.Encode());
            Array.Copy(hosted.Town.Stock,local.Town.Stock,hosted.Town.Stock.Length);
            local.Town.StockCount=hosted.Town.StockCount;local.Town.Spending=hosted.Town.Spending;
        }
        for(int p=0;p<GameState.ProfileCount;p++)
        {
            int target=p;
            if(residents!=null)
            {
                var owner=baseline.Town.Jobs.Player(p);if(owner.IsEmpty)continue;
                target=local.Town.Jobs.FindPlayer(residents.Resolve(local,baseline,owner));
                long proceeds=hosted.Town.Shop.Proceeds[p]-baseline.Town.Shop.Proceeds[p];
                if(proceeds<0)
                {
                    if(earned[p]<-proceeds)throw new InvalidOperationException("The hosted proceeds changed outside this card lineage.");
                    earned[p]+=proceeds;
                }
                else Credit(local,target,proceeds);
            }
            Credit(local,target,earned[p]);
        }

        // Existing town possessions are protected by the hosted command
        // boundary. A committed Keep can add a placement; its decision stands
        // and any overlapping offline possession is retained in the koban.
        for(int i=0;i<baseline.Town.PlacedCount;i++)
            if(Find(hosted,baseline.Town.Placed[i])<0)
                throw new InvalidOperationException("A shared possession changed outside this card lineage.");
        for(int i=0;i<hosted.Town.PlacedCount;i++)
        {
            var placed=hosted.Town.Placed[i];
            if(Find(baseline,placed)>=0||Find(local,placed)>=0)continue;
            KeepPlacement(local,placed);
        }
        local.Town.LostAndFound.MergeHostedAdditions(baseline.Town,hosted.Town);
        local.RestoreProfile(local.ActiveProfile);
        local.ResidentJobs.MergeHostedReceipts(baseline,hosted,now,residents);
        local.Town.NotifyObjectsChanged();
    }

    static void Credit(GameState state,int profile,long amount)
    {
        if(amount==0)return;
        var personal=state.Profiles[profile];if(!personal.Created)throw new InvalidOperationException("Choose a resident for these proceeds.");
        long credit=Math.Min(amount,long.MaxValue-personal.Savings);personal.Savings+=credit;amount-=credit;
        if(state.Town.Shop.Proceeds[profile]>long.MaxValue-amount)throw new InvalidOperationException("The sale proceeds ledger is full.");
        state.Town.Shop.Proceeds[profile]+=amount;
    }

    static void MergeReviewedShop(GameState local,GameState baseline,GameState hosted,OnlineResidentDecisions residents)
    {
        var a=local.Town.Shop;var b=baseline.Town.Shop;var h=hosted.Town.Shop;
        if(!Equal(local.Town.Stock,baseline.Town.Stock)&&!Equal(local.Town.Stock,hosted.Town.Stock))
            throw new InvalidOperationException("Review the changed shop stock before synchronizing.");
        Array.Copy(hosted.Town.Stock,local.Town.Stock,hosted.Town.Stock.Length);
        local.Town.StockCount=hosted.Town.StockCount;local.Town.Spending=hosted.Town.Spending;
        for(int i=0;i<TownShop.StoredSaleCount;i++)
        {
            bool unchanged=a.SaleItems[i]==b.SaleItems[i]&&a.SalePrices[i]==b.SalePrices[i]&&a.SaleOwners[i]==b.SaleOwners[i];
            bool removedOwner=b.SaleItems[i]!=0&&local.Town.Jobs.FindPlayer(baseline.Town.Jobs.Player(b.SaleOwners[i]))<0;
            if(removedOwner&&a.SaleItems[i]==0)continue; // Offline deletion already discarded the old unsold possession.
            if(!unchanged&&(a.SaleItems[i]!=h.SaleItems[i]||a.SalePrices[i]!=h.SalePrices[i]||a.SaleOwners[i]!=h.SaleOwners[i]))
                throw new InvalidOperationException("Review the changed listing before synchronizing.");
            a.SaleItems[i]=h.SaleItems[i];a.SalePrices[i]=h.SalePrices[i];
            a.SaleOwners[i]=h.SaleItems[i]==0?0:local.Town.Jobs.FindPlayer(residents.Resolve(local,baseline,baseline.Town.Jobs.Player(h.SaleOwners[i])));
        }
        for(int n=0;n<Town.SavedVillagerCount;n++)
        {
            int old=baseline.Town.Jobs.FindNpc(local.Town.Jobs.Npc(n));if(old<0)continue;
            a.NpcWallet[n]=h.NpcWallet[old];a.NpcCount[n]=h.NpcCount[old];a.NpcDay[n]=h.NpcDay[old];a.NpcTarget[n]=h.NpcTarget[old];
            Array.Copy(h.NpcItems,old*TownShop.NpcCapacity,a.NpcItems,n*TownShop.NpcCapacity,TownShop.NpcCapacity);
        }
        a.Revision=Math.Max(a.Revision,h.Revision);a.Revision=a.Revision==int.MaxValue?0:a.Revision+1;
    }

    // No static mutable merge state: concurrent hosts reconcile independently.
    static bool Equal(int[] a,int[] b)
    {if(a.Length!=b.Length)return false;for(int i=0;i<a.Length;i++)if(a[i]!=b[i])return false;return true;}
    static bool SameShop(GameState a,GameState b)=>a.Town.StockCount==b.Town.StockCount&&a.Town.Spending==b.Town.Spending&&Equal(a.Town.Stock,b.Town.Stock)&&Equal(a.Town.Shop.Encode(),b.Town.Shop.Encode());
    static bool ShopChanged(GameState a,GameState b)=>!SameShop(a,b);
    static bool Same(Placement a,Placement b)=>a.Item==b.Item&&a.X==b.X&&a.Z==b.Z&&a.Rotation==b.Rotation&&a.Room==b.Room&&a.Owner==b.Owner;
    static int Find(GameState state,Placement p)
    {for(int i=0;i<state.Town.PlacedCount;i++)if(Same(state.Town.Placed[i],p))return i;return -1;}
    public static bool Overlaps(Placement a,Placement b)
    {
        if(a.Room!=b.Room)return false;
        BuildingWorld.Footprint(a,out int aw,out int ah);BuildingWorld.Footprint(b,out int bw,out int bh);
        return a.X<b.X+bw&&b.X<a.X+aw&&a.Z<b.Z+bh&&b.Z<a.Z+ah;
    }
    public static void KeepPlacement(GameState local,Placement placed)
    {
        var actor=TownItemActor.LocalResident(local);
        for(int j=local.Town.PlacedCount-1;j>=0;j--)
            if(Overlaps(placed,local.Town.Placed[j])&&!TownItemActions.RemovePlacement(local,local,actor,j))
                throw new InvalidOperationException("Make room in the koban before resolving this spot.");
        if(placed.Room==0)
        {
            BuildingWorld.Footprint(placed,out int w,out int h);
            for(int i=local.Town.GroundCount-1;i>=0;i--)
            {
                var ground=local.Town.Ground[i];
                if(ground.Kind!=4&&ground.X>=placed.X&&ground.X<placed.X+w&&ground.Z>=placed.Z&&ground.Z<placed.Z+h&&
                    !TownItemActions.RemoveGround(local,local,actor,i))
                    throw new InvalidOperationException("That spot cannot be cleared safely.");
            }
        }
        if(local.Town.PlacedCount==local.Town.Placed.Length||!local.CanPlace(placed.Item,placed.X,placed.Z,placed.Rotation,placed.Room,placed.Owner,existing:true))
            throw new InvalidOperationException("This placement does not fit. Keep the other placement or finish later.");
        var p=local.Town.Placed[local.Town.PlacedCount++];p.Item=placed.Item;p.X=placed.X;p.Z=placed.Z;p.Rotation=placed.Rotation;p.Room=placed.Room;p.Owner=placed.Owner;
    }
}
