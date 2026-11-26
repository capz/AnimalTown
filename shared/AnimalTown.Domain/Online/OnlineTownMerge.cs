using AnimalTown.Jobs;

namespace AnimalTown.Online;

// Three-way reconciliation starts with the actual uploaded offline card. The
// caller owns a disposable candidate and publishes only after every check has
// succeeded. The baseline is the last card read back and acknowledged, never a
// newer backend snapshot that the console has not saved.
public static class OnlineTownMerge
{
    public static void Apply(GameState local,GameState baseline,GameState hosted,long now)
    {
        ulong town=baseline.Town.Jobs.TownId;
        if(town==0||local.Town.Jobs.TownId!=town||hosted.Town.Jobs.TownId!=town||
            local.Visiting||baseline.Visiting||hosted.Visiting||local.IsOnlineProjection||baseline.IsOnlineProjection||hosted.IsOnlineProjection)
            throw new InvalidOperationException("These cards do not share one home lineage.");
        local.CaptureProfile();baseline.CaptureProfile();hosted.CaptureProfile();
        var overflow=new long[GameState.ProfileCount];
        for(int p=0;p<GameState.ProfileCount;p++)
        {
            if(local.Town.Jobs.Player(p)!=baseline.Town.Jobs.Player(p)||hosted.Town.Jobs.Player(p)!=baseline.Town.Jobs.Player(p))
                throw new InvalidOperationException("Synchronize before changing the town's residents.");
            var before=baseline.Profiles[p];var remote=hosted.Profiles[p];var personal=local.Profiles[p];
            // Without an active card reservation, only passive consignment
            // earnings can change private currency. Never replay a remote
            // pocket debit over independently edited offline pockets.
            if(remote.Wallet!=before.Wallet||remote.Savings<before.Savings||!Equal(remote.Pockets,before.Pockets))
                throw new InvalidOperationException("Finish the pending online return before synchronizing.");
            long earned=remote.Savings-before.Savings;
            if(earned>0)
            {
                if(!personal.Created)throw new InvalidOperationException("A returning sale belongs to a removed resident.");
                long credit=Math.Min(earned,long.MaxValue-personal.Savings);
                personal.Savings+=credit;
                // Overflow remains owed in the shared proceeds ledger below.
                earned-=credit;
            }
            overflow[p]=earned;
        }
        for(int n=0;n<Town.SavedVillagerCount;n++)
            if(local.Town.Jobs.Npc(n)!=baseline.Town.Jobs.Npc(n)||hosted.Town.Jobs.Npc(n)!=baseline.Town.Jobs.Npc(n))
                throw new InvalidOperationException("Synchronize before moving a resident between towns.");

        bool localShop=ShopChanged(local,baseline),remoteShop=ShopChanged(hosted,baseline);
        if(localShop&&remoteShop&&!SameShop(local,hosted))
            throw new InvalidOperationException("The shared shop changed. Keep the card for reconciliation.");
        if(remoteShop||!localShop)
        {
            local.Town.Shop.Decode(hosted.Town.Shop.Encode());
            Array.Copy(hosted.Town.Stock,local.Town.Stock,hosted.Town.Stock.Length);
            local.Town.StockCount=hosted.Town.StockCount;local.Town.Spending=hosted.Town.Spending;
        }
        for(int p=0;p<GameState.ProfileCount;p++)
        {
            if(local.Town.Shop.Proceeds[p]>long.MaxValue-overflow[p])throw new InvalidOperationException("The sale proceeds ledger is full.");
            local.Town.Shop.Proceeds[p]+=overflow[p];
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
        local.ResidentJobs.MergeHostedReceipts(baseline,hosted,now);
        local.Town.NotifyObjectsChanged();
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
