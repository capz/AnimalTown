namespace AnimalTown;

// Demand for shared species/house definitions, independent of GPU handles.
// A broad guard prefetches before the animated envelope enters the view.
public sealed class AssetDemand
{
    public const int Species=6,Parts=7,BuildingStart=Species,Capacity=Species+BuildingCatalog.MaximumDefinitions*Parts;
    public const float PrefetchGuard=32;
    public readonly int[] Priority=new int[Capacity]; // 0 cold, 1 prefetch, 2 required
    private uint prefetchSpaces;
    public static int BuildingPart(int type,int part)=>BuildingStart+type*Parts+part;
    private static int Need(GameState state,ActorLocation p,int viewer,float focusX,float focusZ)
    {
        if(p.Space.Value!=viewer)return 0;
        if(viewer!=0)return 2; // Camera rotation must never cause a restore.
        float h=TerrainElevation.At(state.Town,p.X,p.Z),eye=TerrainElevation.At(state.Town,focusX,focusZ);
        var center=CylinderWorld.Surface(p.Z,focusZ,h+.9f);
        if(!RenderView.Sphere(p.X,center.Y,center.Z,2.7f+PrefetchGuard,focusX,focusZ,eye)
            ||!RenderView.AboveHorizon(center.Y,center.Z,2.7f+PrefetchGuard,focusZ,eye))return 0;
        return RenderView.Sphere(p.X,center.Y,center.Z,10.7f,focusX,focusZ,eye)
            &&RenderView.AboveHorizon(center.Y,center.Z,10.7f,focusZ,eye)?2:1;
    }
    private void Interior(GameState state,int space)
    {
        if(space==0)return;prefetchSpaces|=1u<<space;int type=state.BuildingTypes[BuildingWorld.Building(space)];
        for(int p=2;p<Parts;p++)Priority[BuildingPart(type,p)]=2;
    }
    public void Prepare(GameState state,PortalSystem doors,bool allWarm)
    {
        Array.Clear(Priority,0,Priority.Length);
        prefetchSpaces=0;
        for(int type=0;type<BuildingCatalog.Count;type++)for(int p=0;p<2;p++)Priority[BuildingPart(type,p)]=2;
        Priority[state.Species]=2;
        int viewer=state.Space,nextViewer=viewer;var player=doors.Actors[0];
        Interior(state,viewer);
        if(doors.Busy(0)){nextViewer=player.Destination.Space.Value;Interior(state,player.Source.Space.Value);Interior(state,nextViewer);}
        if(viewer==0&&!doors.Busy(0))for(int b=0;b<BuildingWorld.Count;b++)if(BuildingWorld.Exists(state,b)){
            BuildingWorld.Entrance(state,b,out float x,out float z);float dx=x-state.X,dz=z-state.Z;
            // Approach prefetch, not a pin after exiting facing away from a door.
            if(dx*dx+dz*dz<=400&&dx*(float)Math.Sin(state.Facing)+dz*(float)Math.Cos(state.Facing)>.1f)Interior(state,BuildingWorld.Space(b));
        }
        for(int i=0;i<Species;i++){
            var position=BuildingWorld.Location(state,i+1);var door=doors.Actors[i+1];
            int wanted=Need(state,position,viewer,state.X,state.Z);
            if(position.Space.Value!=0&&(prefetchSpaces&(1u<<position.Space.Value))!=0)wanted=Math.Max(wanted,1);
            if(nextViewer!=viewer)wanted=Math.Max(wanted,Need(state,position,nextViewer,player.Destination.X,player.Destination.Z));
            if(doors.Busy(i+1)){
                wanted=Math.Max(wanted,Math.Max(Need(state,door.Source,viewer,state.X,state.Z),Need(state,door.Destination,viewer,state.X,state.Z)));
                if(door.Destination.Space.Value!=0&&(prefetchSpaces&(1u<<door.Destination.Space.Value))!=0)wanted=Math.Max(wanted,1);
                if(nextViewer!=viewer)wanted=Math.Max(wanted,Math.Max(Need(state,door.Source,nextViewer,player.Destination.X,player.Destination.Z),Need(state,door.Destination,nextViewer,player.Destination.X,player.Destination.Z)));
            }
            Priority[i]=Math.Max(Priority[i],wanted);
        }
        if(allWarm)for(int i=0;i<Priority.Length;i++)Priority[i]=2;
    }
    public void UpdateDoorReadiness(GameState state,PortalSystem doors,bool[] ready)
    {
        for(int actor=0;actor<BuildingWorld.Actors;actor++){
            bool available=true;var transition=doors.Actors[actor];
            if(doors.Busy(actor)){
                int target=transition.Destination.Space.Value;
                if(actor==0){
                    if(target!=0){int type=state.BuildingTypes[BuildingWorld.Building(target)];for(int p=2;p<Parts;p++)available&=ready[BuildingPart(type,p)];}
                    for(int i=0;i<Species;i++)if(Priority[i]==2)available&=ready[i];
                }else if(target==state.Space&&Priority[actor-1]>0)available=ready[actor-1];
            }
            doors.VisualReady[actor]=available;
        }
    }
}
