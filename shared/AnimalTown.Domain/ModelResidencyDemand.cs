namespace AnimalTown;

// Residency follows shared species, spaces and an expanded drawable envelope.
// The player and both sides of a doorway remain pinned until the crossing ends.
public sealed class ModelResidencyDemand
{
    public const int SpeciesCount=6;
    // 24 metres gives about four seconds at combined player/NPC running speed.
    // This is extra distance beyond the visible silhouette, including horizon
    // and viewport edges, for queued, frame-budgeted ARAM restoration.
    public const float OutdoorGuard=24;
    public const float ImminentGuard=12;
    public readonly bool[] Required=new bool[SpeciesCount];
    public int DestinationSpace {get;private set;}=-1;
    public void Prepare(GameState state,PortalSystem doors,bool pinAll=false,int targetSpace=-1,bool underMemoryPressure=false)
    {
        DestinationSpace=doors.Busy(0)?doors.Actors[0].Destination.Space.Value:targetSpace;
        for(int species=0;species<Required.Length;species++)
            Required[species]=pinAll||species==state.Species||UsesSpace(state,doors,species,state.Space,underMemoryPressure?ImminentGuard:OutdoorGuard)||
                DestinationSpace>=0&&UsesSpace(state,doors,species,DestinationSpace,underMemoryPressure?ImminentGuard:OutdoorGuard);
    }
    public static bool UsesSpace(GameState state,PortalSystem doors,int species,int space)
        =>UsesSpace(state,doors,species,space,OutdoorGuard);
    public static bool ImminentUsesSpace(GameState state,PortalSystem doors,int species,int space)
        =>UsesSpace(state,doors,species,space,ImminentGuard);
    private static bool UsesSpace(GameState state,PortalSystem doors,int species,int space,float guard)
    {
        if(species==state.Species)return true;
        for(int npc=0;npc<Town.NpcActorCount;npc++){
            if(!state.NpcPresent(npc)||state.NpcSpecies(npc)!=species)continue;int actor=npc+1;
            var resident=state.Town.NpcAt(npc);
            if(resident.Space==space&&(space!=0||OutdoorImminent(state,doors,resident.PosX,resident.PosZ,guard)))return true;
            if(!doors.Busy(actor))continue;
            var transition=doors.Actors[actor];
            if(transition.Source.Space.Value==space&&(space!=0||OutdoorImminent(state,doors,transition.Source.X,transition.Source.Z,guard))||
                transition.Destination.Space.Value==space&&(space!=0||OutdoorImminent(state,doors,transition.Destination.X,transition.Destination.Z,guard)))return true;
        }
        return false;
    }
    // Lookahead may request an invisible actor without blocking simulation or
    // drawing. Only the actual drawable envelope gates the current scene.
    public static bool DrawableUsesSpace(GameState state,PortalSystem doors,int species,int space)
    {
        if(species==state.Species)return true;
        for(int npc=0;npc<Town.NpcActorCount;npc++){
            if(!state.NpcPresent(npc)||state.NpcSpecies(npc)!=species||state.Town.NpcAt(npc).Space!=space)continue;
            if(space!=0)return true;var location=doors.VisualLocation(state,npc+1);
            if(OutdoorImminent(state,doors,location.X,location.Z,0))return true;
        }
        return false;
    }
    private static bool OutdoorImminent(GameState state,PortalSystem doors,float x,float z,float guard)
    {
        OutdoorFocus(state,doors,out float focusX,out float focusZ);
        float eyeHeight=TerrainElevation.At(state.Town,focusX,focusZ);
        var point=CylinderWorld.Surface(z,focusZ,TerrainElevation.At(state.Town,x,z)+.9f);
        return RenderView.Sphere(x,point.Y,point.Z,2.7f+guard,focusX,focusZ,eyeHeight)&&
            RenderView.AboveHorizon(point.Y,point.Z,2.7f+guard,focusZ,eyeHeight);
    }
    private static void OutdoorFocus(GameState state,PortalSystem doors,out float focusX,out float focusZ)
    {
        focusX=state.X;focusZ=state.Z;
        if(state.Space!=0){
            int building=BuildingWorld.Building(state.Space);
            if(!BuildingWorld.Exists(state,building))return;
            var crossing=doors.Actors[0];
            if(doors.Busy(0)&&crossing.Destination.Space.IsTown){focusX=crossing.Destination.X;focusZ=crossing.Destination.Z;}
            else{BuildingWorld.Entrance(state,building,out focusX,out focusZ);focusZ+=1;}
        }
    }
}
