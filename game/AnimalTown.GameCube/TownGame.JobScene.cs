using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;
using Dolphin.Diagnostics;

internal sealed partial class TownGame
{
    private readonly JobSceneRenderer jobScene=new JobSceneRenderer();
    // Opt-in read-only controller diagnostics. No per-frame logging, saved
    // setting or simulation override; the town continues while inspected.
    private void TraceJobView()
    {
        DebugOverlay.WriteLine("ANIMALTOWN JOB VIEW "+screen+" "+room+" "+(int)(state.X*1024)+" "+(int)(state.Z*1024)+" "+jobNpc+" "+jobMode+" "+jobChoice+" "+typeAt+" "+message.Length+" "+selected+" "+menuTab);
        for(int i=0;i<6;i++){var n=state.Town.Residents[i];DebugOverlay.WriteLine("ANIMALTOWN JOB NPC "+i+" "+n.Space+" "+(int)(n.PosX*1024)+" "+(int)(n.PosZ*1024)+" "+(villagers.Doors.Busy(i+1)?1:0)+" "+n.HomeX+" "+n.HomeZ);}
        for(int i=0;i<OfflineJobLedger.WorldCapacity;i++){var o=state.Town.Jobs.WorldObject(i);if(o.Kind!=JobWorldKind.Empty)DebugOverlay.WriteLine("ANIMALTOWN JOB OBJECT "+i+" "+(int)o.Kind+" "+(int)(o.X*1024)+" "+(int)(o.Z*1024)+" "+(int)(o.Radius*1024));}
        if(JobsLive)for(int i=0;i<OfflineJobLedger.JobCapacity;i++){var j=state.Town.Jobs.Job(i);if(j.Occupying)DebugOverlay.WriteLine("ANIMALTOWN JOB STATE "+i+" "+(int)j.Template+" "+(int)j.Status+" "+state.Town.Jobs.FindNpc(j.Requester)+" "+state.Town.Jobs.FindNpc(j.Target));}
        DebugOverlay.WriteLine("ANIMALTOWN JOB VIEW END");
    }
    private bool TalkNearby()
    {
        int closest=-1,withinReach=-1;
        float distance=6.25f,reach=room==0?2.8f:2;
        for(int i=0;i<Town.NpcActorCount;i++){
            if(!state.NpcPresent(i))continue;
            var npc=state.Town.NpcAt(i);if(npc.Space!=room)continue;
            float dx=npc.PosX-state.X,dz=npc.PosZ-state.Z,d=dx*dx+dz*dz;
            if(withinReach<0&&Math.Abs(dx)<reach&&Math.Abs(dz)<reach)withinReach=i;
            if(d<=distance){distance=d;closest=i;}
        }
        int target=JobsLive&&closest>=0?closest:withinReach;
        if(target<0)return false;
        var villager=state.Town.NpcAt(target);
        var visitor=state.NpcVisitor(target);
        if(visitor.HasValue)Say(visitor.Value.Name,visitor.Value.Catchphrase);
        else if(!TryJobConversation(target))Say(villager.Name,state.Talk(target));
        return true;
    }
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
        if(!actorVisibility.Visible[actor])return;
        int npc=actor-1,item;
        int species=actor==0?state.Species:state.NpcSpecies(npc);
        if(actor==0)item=JobsLive?state.Shirt:0;
        else item=state.NpcVisitor(npc)?.Outfit??(JobsLive&&npc<Town.SavedVillagerCount?state.Town.Jobs.Outfit(npc):0);
        var l=actorVisibility.Locations[actor];
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
