using AnimalTown.Jobs;

namespace AnimalTown;

public sealed partial class Town
{
    public const int LocalResidentCount=5, VisitorCount=2, NpcActorCount=LocalResidentCount+VisitorCount;
    // Preserve the original sixth slot in archives, including its cottage and job identity.
    public const int SavedVillagerCount=6;
    internal const int PlayerStartX=18, PlayerStartZ=38;
    private const int PopulationColumns=4, PopulationRows=2, PopulationLandDepth=Height-40;
    private const int PopulationSectors=PopulationColumns*PopulationRows;
    private sealed class VisitorSlots
    {
        internal readonly bool KeepLegacyResident;
        internal readonly SavedVisitor[] Records=new SavedVisitor[VisitorCount];
        internal readonly Villager?[] Actors=new Villager?[VisitorCount];
        internal VisitorSlots(bool keepLegacyResident){KeepLegacyResident=keepLegacyResident;}
    }
    private VisitorSlots? visitors;

    internal bool VisitorRoster=>visitors!=null;
    internal bool LegacyResidentPresent=>visitors?.KeepLegacyResident??true;

    public Villager NpcAt(int npc)
    {
        if((uint)(npc-LocalResidentCount)<VisitorCount&&visitors?.Actors[npc-LocalResidentCount] is Villager visitor)return visitor;
        if((uint)npc<SavedVillagerCount)return Residents[npc];
        throw new ArgumentOutOfRangeException(nameof(npc));
    }

    internal SavedVisitor? VisitorAt(int npc)=>(uint)(npc-LocalResidentCount)<VisitorCount&&visitors?.Actors[npc-LocalResidentCount]!=null
        ?visitors.Records[npc-LocalResidentCount]:null;

    internal void ConfigureVisitors(GameState state,SavedVisitor[] candidates,bool keepLegacy)
    {
        var current=new VisitorSlots(keepLegacy);
        visitors=current;
        int slot=keepLegacy?1:0;
        int limit=state.Visiting?VisitorCount-1:VisitorCount;
        for(int i=0,count=0;i<candidates.Length&&count<limit&&slot<VisitorCount;i++){
            var saved=candidates[i];
            if(saved.TownId==0||saved.TownId==Jobs.TownId||(uint)saved.Slot>=SavedVillagerCount||
                !saved.Identity.IsValid||(uint)saved.Species>=ModelResidencyDemand.SpeciesCount||string.IsNullOrEmpty(saved.Name))continue;
            if(Jobs.FindNpc(saved.Identity)>=0||Jobs.FindPlayer(saved.Identity)>=0||Jobs.PermanentlyRemoved(saved.Identity))continue;
            var first=current.Records[0];
            if(slot>0&&(first.Identity==saved.Identity||first.TownId==saved.TownId&&first.Slot==saved.Slot))continue;
            if(!TryVisitorSpawn(state,out int x,out int z))break;
            var visitor=new Villager{Name=saved.Name,Phrase=saved.Catchphrase,HomeX=-1,HomeZ=-1};
            SetOutdoor(visitor,x,z);
            current.Records[slot]=saved;
            current.Actors[slot]=visitor;
            slot++;count++;
        }
    }

    // Give the player a starting area and rotate independent NPC starting areas
    // by town seed. The original positions from saved towns are read back later.
    private void DistributeNewPopulation()
    {
        int playerSector=Sector(PlayerStartX,PlayerStartZ);
        int offset=(int)((uint)Seed%(PopulationSectors-1));
        for(int npc=0;npc<SavedVillagerCount;npc++){
            int sector=(npc+offset)%(PopulationSectors-1);
            if(sector>=playerSector)sector++;
            if(TrySpawn(sector,null,out int x,out int z))SetOutdoor(Residents[npc],x,z);
        }
    }

    private bool TryVisitorSpawn(GameState state,out int bestX,out int bestZ)
    {
        float playerX=state.Space==0?state.X:PlayerStartX,playerZ=state.Space==0?state.Z:PlayerStartZ;
        int occupied=playerZ<PopulationLandDepth?1<<Sector((int)playerX,(int)playerZ):0;
        for(int npc=0;npc<NpcActorCount;npc++){
            if(!state.NpcPresent(npc))continue;
            var other=NpcAt(npc);
            if(other.Space==0&&other.Z<PopulationLandDepth)occupied|=1<<Sector(other.X,other.Z);
        }

        bool foundEmpty=false;
        float bestSeparation=-1;
        bestX=bestZ=0;
        int offset=(int)((uint)Seed%PopulationSectors);
        for(int i=0;i<PopulationSectors;i++){
            int sector=(i+offset)%PopulationSectors;
            bool empty=(occupied&(1<<sector))==0;
            if(foundEmpty&&!empty||!TrySpawn(sector,state,out int x,out int z))continue;

            float separation=DistanceSquared(x+.5f,z+.5f,playerX,playerZ);
            for(int npc=0;npc<NpcActorCount;npc++){
                if(!state.NpcPresent(npc))continue;
                var other=NpcAt(npc);
                if(other.Space==0)separation=Math.Min(separation,DistanceSquared(x+.5f,z+.5f,other.PosX,other.PosZ));
            }
            if(separation<1||empty==foundEmpty&&separation<=bestSeparation)continue;
            foundEmpty=empty;
            bestSeparation=separation;
            bestX=x;bestZ=z;
        }
        return bestSeparation>=0;
    }

    private static int Sector(int x,int z)=>z*PopulationRows/PopulationLandDepth*PopulationColumns+x*PopulationColumns/Width;

    private bool TrySpawn(int sector,GameState? state,out int x,out int z)
    {
        int column=sector%PopulationColumns,row=sector/PopulationColumns;
        int left=column*Width/PopulationColumns,right=(column+1)*Width/PopulationColumns;
        int top=row*PopulationLandDepth/PopulationRows,bottom=(row+1)*PopulationLandDepth/PopulationRows;
        int centerX=(left+right)/2,centerZ=(top+bottom)/2;
        float bestDistance=float.MaxValue;
        x=z=0;
        for(int rowZ=Math.Max(2,top);rowZ<bottom;rowZ++)for(int colX=Math.Max(2,left);colX<Math.Min(Width-2,right);colX++){
            float distance=DistanceSquared(colX,rowZ,centerX,centerZ);
            if(distance>=bestDistance||!ClearSpawn(colX,rowZ,state))continue;
            bestDistance=distance;x=colX;z=rowZ;
        }
        return bestDistance<float.MaxValue;
    }

    private bool ClearSpawn(int x,int z,GameState? state)
    {
        if(InReserve(x,z)||At(x,z)!=(int)Terrain.Grass||TerrainElevation.Mode(this,x,z)>1)return false;
        if(!Walkable(x-1,z)||!Walkable(x+1,z)||!Walkable(x,z-1)||!Walkable(x,z+1))return false;
        return state==null?FindGround(x,z,3)<0:state.CanWalk(x+.5f,z+.5f,0);
    }

    private static float DistanceSquared(float x,float z,float otherX,float otherZ)
    {float dx=x-otherX,dz=z-otherZ;return dx*dx+dz*dz;}

    private static void SetOutdoor(Villager villager,int x,int z)
    {villager.X=x;villager.Z=z;villager.PosX=x+.5f;villager.PosZ=z+.5f;}
}

public sealed partial class GameState
{
    public void SetTownVisitors(SavedVisitor[] visitors)
    {
        Town.ConfigureVisitors(this,visitors,KeepLegacyResident());
    }

    public SavedVisitor? NpcVisitor(int npc)=>Town.VisitorAt(npc);
    public int NpcSpecies(int npc)=>NpcVisitor(npc)?.Species??ResidentSpecies(npc);
    public bool NpcPresent(int npc)=>NpcVisitor(npc).HasValue||ResidentPresent(npc);
    internal bool ResidentPresent(int npc)=>(uint)npc<Town.SavedVillagerCount&&
        (npc<Town.LocalResidentCount||Town.LegacyResidentPresent)&&!Town.Jobs.NpcInTransit(npc);
    internal bool AllowsFreshResidentJobs(int npc)=>npc<Town.LocalResidentCount||!Town.VisitorRoster;

    private bool KeepLegacyResident()
    {
        // Old saved jobs still need their original giver, recipient or reward source.
        var ledger=Town.Jobs;
        if(!ledger.Initialized)return Town.Residents[Town.LocalResidentCount].Request!=0;
        var identity=ledger.Npc(Town.LocalResidentCount);
        foreach(var job in ledger.Jobs)if(job.Occupying&&(job.Requester==identity||job.Target==identity||job.NextBorrower==identity))return true;
        foreach(var offer in ledger.Offers)if(!offer.Id.IsEmpty&&(offer.Requester==identity||offer.Target==identity))return true;
        foreach(var reward in ledger.Rewards)if(reward.State!=JobRewardState.Empty&&reward.Giver==identity)return true;
        return false;
    }
}
