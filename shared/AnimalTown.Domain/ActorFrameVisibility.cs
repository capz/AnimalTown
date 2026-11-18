namespace AnimalTown;

// One render-time decision drives both pose evaluation and model submission.
// Doorway visual positions, committed room changes and inspector cameras must
// be resolved before testing; simulation visibility can be a frame out of date.
public sealed class ActorFrameVisibility
{
    public readonly bool[] Visible=new bool[BuildingWorld.Actors];
    public readonly ActorLocation[] Locations=new ActorLocation[BuildingWorld.Actors];
    public void Prepare(GameState state,PortalSystem doors,InteriorCamera interior,int inspection,float eyeHeight)
    {
        bool sceneVisible=doors.PlayerFade<1;int room=state.Space;
        float size=room==0?0:BuildingWorld.Size(state,room);
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
                }else visible=interior.Sphere(p.X,scale*.5f,p.Z,radius,size);
            }
            Visible[actor]=visible;
        }
    }
}
