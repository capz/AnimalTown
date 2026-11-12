namespace AnimalTown;

// Residency follows shared species and spaces, not a camera's per-frame cull.
// The player and both sides of a doorway remain pinned until the crossing ends.
public sealed class ModelResidencyDemand
{
    public const int SpeciesCount=6;
    // More than four seconds of maximum sprint travel, plus NPC motion. This
    // expands restore demand only: outdoor camera culling never triggers eviction.
    public const float OutdoorGuard=24;
    public readonly bool[] Required=new bool[SpeciesCount];
    public int DestinationSpace {get;private set;}=-1;
    public void Prepare(GameState state,PortalSystem doors,bool pinAll=false,int targetSpace=-1)
    {
        DestinationSpace=doors.Busy(0)?doors.Actors[0].Destination.Space.Value:targetSpace;
        for(int species=0;species<Required.Length;species++)
            Required[species]=pinAll||species==state.Species||UsesSpace(state,doors,species,state.Space)||
                DestinationSpace>=0&&UsesSpace(state,doors,species,DestinationSpace);
    }
    public static bool UsesSpace(GameState state,PortalSystem doors,int species,int space)
    {
        if(species==state.Species)return true;
        int actor=species+1;
        var resident=state.Town.Residents[species];
        if(resident.Space==space&&(space!=0||OutdoorImminent(state,doors,resident.PosX,resident.PosZ)))return true;
        if(!doors.Busy(actor))return false;
        var transition=doors.Actors[actor];
        return transition.Source.Space.Value==space&&(space!=0||OutdoorImminent(state,doors,transition.Source.X,transition.Source.Z))||
            transition.Destination.Space.Value==space&&(space!=0||OutdoorImminent(state,doors,transition.Destination.X,transition.Destination.Z));
    }
    // Lookahead may request an invisible actor without blocking simulation or
    // drawing. Only the original drawable envelope gates the current scene.
    public static bool DrawableUsesSpace(GameState state,PortalSystem doors,int species,int space)
    {
        if(species==state.Species)return true;
        if(state.Town.Residents[species].Space!=space)return false;
        if(space!=0)return true;
        var location=doors.VisualLocation(state,species+1);
        return OutdoorImminent(state,doors,location.X,location.Z,0);
    }
    private static bool OutdoorImminent(GameState state,PortalSystem doors,float x,float z,float guard=OutdoorGuard)
    {
        float focusX=state.X,focusZ=state.Z;
        if(state.Space!=0){
            int building=BuildingWorld.Building(state.Space);
            if(!BuildingWorld.Exists(state,building))return true;
            var crossing=doors.Actors[0];
            if(doors.Busy(0)&&crossing.Destination.Space.IsTown){focusX=crossing.Destination.X;focusZ=crossing.Destination.Z;}
            else{BuildingWorld.Entrance(state,building,out focusX,out focusZ);focusZ+=1;}
        }
        float eyeHeight=TerrainElevation.At(state.Town,focusX,focusZ);
        var point=CylinderWorld.Surface(z,focusZ,TerrainElevation.At(state.Town,x,z)+.9f);
        return RenderView.Sphere(x,point.Y,point.Z,2.7f+guard,focusX,focusZ,eyeHeight)&&
            RenderView.AboveHorizon(point.Y,point.Z,2.7f+guard,focusZ,eyeHeight);
    }
}
