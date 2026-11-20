using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;

internal sealed partial class TownGame
{
    private readonly JobSceneRenderer jobScene=new JobSceneRenderer();
    private void DrawJobObjects()
    {
        if(!JobsLive||room!=0)return;
        for(int i=0;i<OfflineJobLedger.WorldCapacity;i++){
            var o=state.Town.Jobs.WorldObject(i);if(o.Kind==JobWorldKind.Empty||!Visible(o.X,o.Z,1.5f))continue;
            jobScene.Ball(renderer,SurfacePoint(o.X,o.Z,o.Radius),SurfaceRotation(o.Z),o.Radius,o.Kind!=JobWorldKind.Ball);
            if(o.Kind==JobWorldKind.Snowman){jobScene.Ball(renderer,SurfacePoint(o.X,o.Z,o.Radius*2.5f),SurfaceRotation(o.Z),o.Radius*.7f,true);Box(o.X-.11f,o.Radius*2.6f,o.Z+o.Radius*.6f,.06f,.06f,.07f,new Color32(47,57,59));Box(o.X+.07f,o.Radius*2.6f,o.Z+o.Radius*.6f,.06f,.06f,.07f,new Color32(47,57,59));}
        }
        // The notice board reuses the existing wood/paper colours and box batch.
        Box(42.4f,0,12.9f,.12f,1.65f,.12f,new Color32(127,85,58));Box(43.5f,0,12.9f,.12f,1.65f,.12f,new Color32(127,85,58));Box(42.25f,.8f,12.85f,1.5f,.9f,.12f,new Color32(127,85,58));Box(42.4f,.92f,12.98f,1.18f,.6f,.025f,new Color32(246,236,203));
    }
    private void DrawJobOutfit(int actor)
    {
        if(!JobsLive||!actorVisibility.Visible[actor])return;
        var l=actorVisibility.Locations[actor];int item=actor==0?state.Shirt:state.Town.Jobs.Outfit(actor-1),species=actor==0?state.Species:state.ResidentSpecies(actor-1);
        if(!Catalog.Has(item,ItemFlags.Wear))return;
        NativeShadows.Objects(room==0?1:0);
        try{jobScene.Outfit(renderer,item,species,SurfacePoint(l.X,l.Z),SurfaceRotation(l.Z,actor==0?facingYaw:l.Yaw));}finally{NativeShadows.Objects(0);}
    }
    private void DrawJobRoomCoverings()
    {
        if(!JobsLive)return;int profile=BuildingWorld.RoomProfile(room);if(profile<0)return;
        int floor=state.Town.Jobs.HomeFloor(profile),wall=state.Town.Jobs.HomeWall(profile);float size=BuildingWorld.Size(state,room);
        if(floor!=0)for(int z=0;z<(int)size;z++)for(int x=0;x<(int)size;x++)Box(x,.018f,z,1,.012f,1,floor==Catalog.OakFloor?((z&1)==0?new Color32(177,132,85):new Color32(193,148,98)):((x+z)%2==0?new Color32(138,173,127):new Color32(157,188,139)));
        if(wall==0)return;var color=wall==Catalog.LeafWallpaper?new Color32(174,194,148):new Color32(224,167,135);
        if(indoorCamera.WallVisible(0))Box(.08f,.25f,.045f,size-.16f,1.55f,.025f,color);
        if(indoorCamera.WallVisible(1))Box(size-.07f,.25f,.08f,.025f,1.55f,size-.16f,color);
        if(indoorCamera.WallVisible(3))Box(.045f,.25f,.08f,.025f,1.55f,size-.16f,color);
    }
}
