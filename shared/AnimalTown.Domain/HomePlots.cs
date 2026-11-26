namespace AnimalTown;

public sealed partial class GameState
{
    // Land belongs to the garden that exists, plus an already ordered extension.
    // The generator's separate starter clearing is not a player-owned reserve.
    public int HomeReservedGardenTier(int profile)
    {
        int tier=HomeGardenTier(profile),kind=profile==ActiveProfile?PendingKind:Profiles[profile].PendingKind;
        int pending=profile==ActiveProfile?PendingTier:Profiles[profile].PendingTier;
        return kind==2&&(uint)pending<GardenWidths.Length?Math.Max(tier,pending):tier;
    }
    public int HomePlotWidth(int profile)=>GardenWidths[HomeReservedGardenTier(profile)];
    public int HomePlotHeight(int profile)=>GardenHeights[HomeReservedGardenTier(profile)];
    private static bool Contains(int x,int z,int w,int h,int tx,int tz)=>tx>=x&&tz>=z&&tx-x<w&&tz-z<h;
    private static bool Overlaps(int x,int z,int w,int h,int bx,int bz,int bw,int bh)
        =>x<bx+bw&&x+w>bx&&z<bz+bh&&z+h>bz;
    private static bool PlotFits(int x,int z,int tier)
        =>(uint)tier<GardenWidths.Length&&x>=1&&z>=1&&x<=Town.Width-GardenWidths[tier]&&z<=Town.Height-GardenHeights[tier];

    private void EntranceCells(int profile,int x,int z,out int left,out int top,out int width,out int depth)
    {
        var definition=BuildingWorld.Definition(this,BuildingWorld.PlayerBuilding(profile));
        float doorX=x+2+definition.DoorX,doorZ=z+1+definition.DoorZ;
        left=(int)Math.Floor(doorX-BuildingWorld.Radius);top=(int)Math.Floor(doorZ-BuildingWorld.Radius);
        width=(int)Math.Floor(doorX+BuildingWorld.Radius)-left+1;
        // PortalSystem lands one metre beyond the approach socket. With a
        // starter garden that final cell is just outside its southern edge.
        depth=(int)Math.Floor(doorZ+1+BuildingWorld.Radius)-top+1;
    }
    internal bool HomeReservationIntersects(int profile,int x,int z,int width,int height)
    {
        if(!HomePlaced(profile))return false;
        int hx=HomeX(profile),hz=HomeZ(profile);
        if(Overlaps(x,z,width,height,hx,hz,HomePlotWidth(profile),HomePlotHeight(profile)))return true;
        EntranceCells(profile,hx,hz,out int ex,out int ez,out int ew,out int eh);
        return Overlaps(x,z,width,height,ex,ez,ew,eh);
    }
    public bool AnyHomeReservationContains(int x,int z)
    {for(int profile=0;profile<ProfileCount;profile++)if(HomeReservationIntersects(profile,x,z,1,1))return true;return false;}

    public bool CanPlaceHouse(int x,int z,out string reason)
    {
        if(Visiting){reason="Only a resident can place a home.";return false;}
        if(Space!=0){reason="Choose a home site outside.";return false;}
        if((uint)GardenTier>=GardenWidths.Length||PendingKind==2&&(uint)PendingTier>=GardenWidths.Length){reason="This garden size is unavailable.";return false;}
        return CanBuildPlot(ActiveProfile,x,z,HomeReservedGardenTier(ActiveProfile),GardenTier,false,out reason);
    }
    public bool CanExpandGarden(int nextTier,out string reason)
    {
        if(Visiting||!HousePlaced){reason="Place your own home before expanding its garden.";return false;}
        if(nextTier!=GardenTier+1||(uint)nextTier>=GardenWidths.Length){reason="No garden expansion is available yet.";return false;}
        return CanBuildPlot(ActiveProfile,Town.HouseX,Town.HouseZ,nextTier,GardenTier,true,out reason);
    }

    private bool CanBuildPlot(int profile,int x,int z,int targetTier,int currentTier,bool expansion,out string reason)
    {
        if(!PlotFits(x,z,targetTier)){reason="Leave room for the whole home and garden.";return false;}
        int w=GardenWidths[targetTier],h=GardenHeights[targetTier];
        EntranceCells(profile,x,z,out int ex,out int ez,out int ew,out int eh);
        for(int other=0;other<ProfileCount;other++)if(other!=profile&&HomePlaced(other)&&
            (HomeReservationIntersects(other,x,z,w,h)||HomeReservationIntersects(other,ex,ez,ew,eh))){
            reason="Leave some space around another home.";return false;
        }
        bool retainOwnPaths=expansion||HomePlaced(profile)&&x==HomeX(profile)&&z==HomeZ(profile);
        for(int dz=0;dz<h;dz++)for(int dx=0;dx<w;dx++)
            if(!BuildablePlotCell(profile,x+dx,z+dz,retainOwnPaths,out reason))return false;
        for(int dz=0;dz<eh;dz++)for(int dx=0;dx<ew;dx++){
            int tx=ex+dx,tz=ez+dz;
            if(!Town.Walkable(tx,tz)||TerrainElevation.Mode(Town,tx,tz)!=0){reason="Keep the doorway and exit on level, walkable ground.";return false;}
            if(HomeBlocksExcept(profile,tx,tz)){reason="Keep the doorway clear of other buildings.";return false;}
        }
        // Service and villager doors are outside their terrain footprints.
        // Reserving those exact approach/exit cells prevents a small new plot
        // from blocking a neighbouring entrance without requiring a large moat.
        for(int building=0;building<BuildingWorld.Count;building++){
            if(building==BuildingWorld.PlayerBuilding(profile)||!BuildingWorld.Exists(this,building))continue;
            BuildingWorld.Entrance(this,building,out float doorX,out float doorZ);
            int left=(int)Math.Floor(doorX-BuildingWorld.Radius),top=(int)Math.Floor(doorZ-BuildingWorld.Radius);
            int width=(int)Math.Floor(doorX+BuildingWorld.Radius)-left+1,depth=(int)Math.Floor(doorZ+1+BuildingWorld.Radius)-top+1;
            if(Overlaps(x,z,w,h,left,top,width,depth)){reason="Keep the entrance to another building clear.";return false;}
        }
        int oldX=HomeX(profile),oldZ=HomeZ(profile),owner=profile==0?0:profile+6;
        for(int i=0;i<Town.GroundCount;i++){
            var item=Town.Ground[i];if(item.Kind==4||TreeDrawBatch.IsTree(item))continue;
            if(ConstructionCell(profile,x,z,targetTier,currentTier,expansion,item.X,item.Z,false)||
                !expansion&&MovedFurnitureContains(owner,x-oldX,z-oldZ,item.X,item.Z)){
                reason="Keep the house, doorway and fence clear of plants and items.";return false;
            }
        }
        for(int i=0;i<Town.PlacedCount;i++){
            var item=Town.Placed[i];if(item.Room!=0)continue;
            BuildingWorld.Footprint(item,out int iw,out int ih);
            if(item.Owner!=owner){
                if(Overlaps(x,z,w,h,item.X,item.Z,iw,ih)||Overlaps(ex,ez,ew,eh,item.X,item.Z,iw,ih)){
                    reason="Keep the home and garden clear of other furniture.";return false;
                }
                continue;
            }
            int px=item.X+(expansion?0:x-oldX),pz=item.Z+(expansion?0:z-oldZ);
            for(int dz=0;dz<ih;dz++)for(int dx=0;dx<iw;dx++){
                int tx=px+dx,tz=pz+dz;
                if(!Contains(x,z,GardenWidths[currentTier],GardenHeights[currentTier],tx,tz)||
                    ConstructionCell(profile,x,z,targetTier,currentTier,expansion,tx,tz,false)){
                    reason="Move furniture away from the house, doorway and new fence first.";return false;
                }
            }
        }
        reason="A lovely spot!";return true;
    }
    private bool HomeBlocksExcept(int profile,int x,int z)
    {
        for(int p=0;p<ProfileCount;p++)if(p!=profile&&HomePlaced(p)&&Contains(HomeX(p)+2,HomeZ(p)+1,3,3,x,z))return true;
        return false;
    }
    private bool BuildablePlotCell(int profile,int x,int z,bool retainOwnPaths,out string reason)
    {
        int kind=Town.At(x,z);
        if(kind==(int)Terrain.Water||kind==(int)Terrain.Sea||WaterTerrain.IsCoast(kind)){
            reason="Choose dry land away from the water.";return false;
        }
        if(TerrainElevation.Mode(Town,x,z)!=0){reason="Choose flat, level ground.";return false;}
        if(kind!=(int)Terrain.Grass){reason="Find clear grass with room for a garden.";return false;}
        // Walking may have worn paths inside an established garden. Preserve
        // those bytes; only newly claimed ground must be free of existing paths.
        if(Town.Paths.At(x,z)!=0&&(!retainOwnPaths||!GardenContains(profile,x,z))){
            reason="Keep existing paths outside the new garden area.";return false;
        }
        reason="";return true;
    }
    private static bool FenceCell(int x,int z,int w,int h,int tx,int tz)
        =>Contains(x,z,w,h,tx,tz)&&(tx==x||tx==x+w-1||tz==z||tz==z+h-1&&tx!=x+3);
    private bool ConstructionCell(int profile,int x,int z,int targetTier,int currentTier,bool expansion,int tx,int tz,bool tree)
    {
        int w=GardenWidths[targetTier],h=GardenHeights[targetTier];
        if(Contains(x+2,z+1,3,3,tx,tz)||tx==x+1&&tz==z+3||FenceCell(x,z,w,h,tx,tz))return true;
        if(!expansion&&FenceCell(x,z,GardenWidths[currentTier],GardenHeights[currentTier],tx,tz))return true;
        EntranceCells(profile,x,z,out int ex,out int ez,out int ew,out int eh);
        if(Contains(ex,ez,ew,eh,tx,tz))return true;
        // An extension clears trees only in newly acquired ground. Flowers,
        // buried/dropped items and established interior garden trees stay put.
        return tree&&expansion&&Contains(x,z,w,h,tx,tz)&&!Contains(x,z,GardenWidths[currentTier],GardenHeights[currentTier],tx,tz);
    }
    private bool MovedFurnitureContains(int owner,int dx,int dz,int x,int z)
    {
        for(int i=0;i<Town.PlacedCount;i++){
            var p=Town.Placed[i];if(p.Room!=0||p.Owner!=owner)continue;
            BuildingWorld.Footprint(p,out int w,out int h);
            if(Contains(p.X+dx,p.Z+dz,w,h,x,z))return true;
        }
        return false;
    }
    private int ClearConstructionTrees(int profile,int x,int z,int targetTier,int currentTier,bool expansion,int[]? removed)
    {
        int count=0,owner=profile==0?0:profile+6,dx=x-HomeX(profile),dz=z-HomeZ(profile);
        for(int i=Town.GroundCount-1;i>=0;i--){
            var item=Town.Ground[i];
            if(!TreeDrawBatch.IsTree(item)||!ConstructionCell(profile,x,z,targetTier,currentTier,expansion,item.X,item.Z,true)&&
                (expansion||!MovedFurnitureContains(owner,dx,dz,item.X,item.Z)))continue;
            if(removed!=null)removed[count]=i;
            count++;Town.RemoveGround(i);
        }
        return count;
    }
    internal bool PlaceHouse(int x,int z,bool relocate,bool free,int[]? removedTrees,out int removedCount)
    {
        removedCount=0;
        if((relocate&&!free&&Wallet<50000)||!CanPlaceHouse(x,z,out _))return false;
        // Validation is complete before touching any tree, furnishing or money.
        removedCount=ClearConstructionTrees(ActiveProfile,x,z,GardenTier,GardenTier,false,removedTrees);
        int dx=x-Town.HouseX,dz=z-Town.HouseZ;
        for(int i=0;i<Town.PlacedCount;i++)if(Town.Placed[i].Room==0&&Town.Placed[i].Owner==PlayerOwner){Town.Placed[i].X+=dx;Town.Placed[i].Z+=dz;}
        Town.HouseX=x;Town.HouseZ=z;HousePlaced=true;
        var definition=BuildingWorld.Definition(this,BuildingWorld.PlayerBuilding(ActiveProfile));
        X=x+2+definition.DoorX;Z=z+1+definition.DoorZ;
        Town.NotifyObjectsChanged();Town.NotifyCastersChanged();if(relocate&&!free)Wallet-=50000;return true;
    }
    private void CompleteGardenExpansion(int profile,int day)
    {
        var p=Profiles[profile];
        if(p.PendingKind!=2||day<p.PendingDay||!p.HousePlaced||p.PendingTier<p.GardenTier||
            !CanBuildPlot(profile,p.HouseX,p.HouseZ,p.PendingTier,p.GardenTier,true,out _))return;
        ClearConstructionTrees(profile,p.HouseX,p.HouseZ,p.PendingTier,p.GardenTier,true,null);
        p.GardenTier=p.PendingTier;p.PendingKind=0;Town.NotifyObjectsChanged();Town.NotifyCastersChanged();
        // A late blocker leaves the same paid order pending. Another rollover
        // can finish it after the player removes the obstacle; nothing is erased.
    }
    internal bool ValidSavedHomePlots()
    {
        for(int profile=0;profile<ProfileCount;profile++){
            var p=Profiles[profile];
            if((uint)p.GardenTier>=GardenWidths.Length||p.PendingKind==2&&(uint)p.PendingTier>=GardenWidths.Length)return false;
            int tier=p.PendingKind==2?Math.Max(p.GardenTier,p.PendingTier):p.GardenTier;
            if(!PlotFits(p.HouseX,p.HouseZ,p.HousePlaced?tier:0))return false;
            if(!p.HousePlaced)continue;
            EntranceCells(profile,p.HouseX,p.HouseZ,out int ex,out int ez,out int ew,out int eh);
            if(ex<0||ez<0||ex>Town.Width-ew||ez>Town.Height-eh)return false;
            for(int other=0;other<profile;other++)if(HomePlaced(other)&&
                (HomeReservationIntersects(other,p.HouseX,p.HouseZ,GardenWidths[tier],GardenHeights[tier])||HomeReservationIntersects(other,ex,ez,ew,eh)))return false;
        }
        return true;
    }
}
