namespace AnimalTown;

// One render-time decision drives both pose evaluation and model submission.
// Doorway visual positions, committed room changes and inspector cameras must
// be resolved before testing; simulation visibility can be a frame out of date.
public sealed class ActorFrameVisibility
{
    public readonly bool[] Visible=new bool[BuildingWorld.Actors];
    public readonly ActorLocation[] Locations=new ActorLocation[BuildingWorld.Actors];
    public readonly int[] GeometryLod=new int[BuildingWorld.Actors],AnimationStride=new int[BuildingWorld.Actors];
    private readonly int[] animationLod=new int[BuildingWorld.Actors];
    private readonly OpaqueActorOcclusion occlusion=new OpaqueActorOcclusion();
    public int OcclusionBuilds=>occlusion.ProjectionBuilds;
    public int OccludedActors {get;private set;}
    public void Prepare(GameState state,PortalSystem doors,InteriorCamera interior,int inspection,float eyeHeight,int opaqueBuildingTypes=0,bool terrainOcclusion=false)
    {
        bool sceneVisible=doors.PlayerFade<1;int room=state.Space;
        float size=room==0?0:BuildingWorld.Size(state,room);
        OccludedActors=0;bool preparedOcclusion=false;
        for(int actor=0;actor<Visible.Length;actor++){
            var p=doors.VisualLocation(state,actor);Locations[actor]=p;
            bool visible=sceneVisible&&p.Space.Value==room&&!(actor==0&&inspection!=0);
            if(visible){
                // The existing conservative animated envelope includes ears,
                // limbs and all facing directions; retain edge intersections.
                const float scale=1.8f,radius=scale*1.5f;
                if(room==0){
                    var center=CylinderWorld.Surface(p.Z,state.Z,TerrainElevation.At(state.Town,p.X,p.Z)+scale*.5f);
                    visible=RenderView.Sphere(p.X,center.Y,center.Z,radius,state.X,state.Z,eyeHeight)&&RenderView.AboveHorizon(center.Y,center.Z,radius,state.Z,eyeHeight);
                    if(visible&&actor>0&&(opaqueBuildingTypes!=0||terrainOcclusion)){
                        if(!preparedOcclusion){occlusion.Prepare(state,eyeHeight,opaqueBuildingTypes,terrainOcclusion);preparedOcclusion=true;}
                        if(occlusion.Hidden(p.X,center.Y,center.Z,radius)){visible=false;OccludedActors++;}
                    }
                    float pixels=VillagerLod.PixelHeight(center.Y,center.Z,state.Z,eyeHeight);
                    GeometryLod[actor]=actor==0||!VillagerLod.CoarseAvailable(actor-1)?0:VillagerLod.Select(pixels,GeometryLod[actor]);
                    animationLod[actor]=actor==0?0:VillagerLod.SelectAnimation(pixels,animationLod[actor]);
                }else visible=interior.Sphere(p.X,scale*.5f,p.Z,radius,size);
            }
            if(room!=0||actor==0){GeometryLod[actor]=0;animationLod[actor]=0;}
            AnimationStride[actor]=VillagerLod.AnimationStride(animationLod[actor]);
            Visible[actor]=visible;
        }
    }
}
