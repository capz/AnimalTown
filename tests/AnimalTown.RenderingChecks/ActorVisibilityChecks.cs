using AnimalTown;

internal static class ActorVisibilityChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(17){X=80,Z=80};var doors=new PortalSystem();var camera=new InteriorCamera();var view=new ActorFrameVisibility();var clock=new AnimationFrameClock(7);
        Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);Array.Clear(state.Town.Elevation);
        var npc=state.Town.Residents[0];npc.Space=0;npc.PosX=80;npc.PosZ=80;
        view.Prepare(state,doors,camera,0,0);check(view.Visible[0]&&view.Visible[1],"nearby player and NPC are visible with the drawing camera");
        clock.Step(1,0,true);npc.PosX=159;view.Prepare(state,doors,camera,0,0);
        check(!view.Visible[1]&&clock.Consume(1,view.Visible[1])==0,"an actor moving outside the viewport before rendering cannot consume a native pose update");
        for(int n=0;n<900;n++){clock.Step(1,0,true);check(clock.Consume(1,view.Visible[1])==0,"hidden elapsed time is discarded every frame");}
        npc.PosX=80;view.Prepare(state,doors,camera,0,0);clock.Step(1,1,true);
        check(view.Visible[1]&&clock.Clip(1)==1&&Math.Abs(clock.Consume(1,true)-1f/30)<.000001f,"reappearance applies the latest clip without an offscreen catch-up burst");
        npc.Space=16;view.Prepare(state,doors,camera,0,0);check(!view.Visible[1],"an NPC inside another room cannot animate or draw outdoors");
        view.Prepare(state,doors,camera,1,0);check(!view.Visible[0],"water inspection excludes the player's native animation and submission together");
        state.Space=16;state.X=state.Z=2;npc.PosX=npc.PosZ=2;view.Prepare(state,doors,camera,0,0);check(view.Visible[0]&&view.Visible[1],"loaded room occupants share the interior drawing camera");
        npc.PosX=80;view.Prepare(state,doors,camera,0,0);check(!view.Visible[1],"indoor offscreen actors are rejected before native controls");
        npc.PosX=2;state.Space=0;state.X=state.Z=80;npc.Space=0;npc.PosX=80;npc.PosZ=80;
        var transition=doors.Actors[1];transition.Phase=DoorPhase.Crossing;transition.Building=1;transition.Tick=17;
        var visual=doors.VisualLocation(state,1);view.Prepare(state,doors,camera,0,0);
        check(view.Locations[1].X==visual.X&&view.Locations[1].Z==visual.Z,"doorway culling and drawing use the interpolated visual location, not the simulation origin");
        doors.Actors[0].Building=1;doors.Actors[0].Phase=DoorPhase.Crossing;doors.Actors[0].Tick=18;view.Prepare(state,doors,camera,0,0);
        for(int i=0;i<view.Visible.Length;i++)check(!view.Visible[i],"opaque doorway fade suppresses hidden actor pose work and drawing");
        doors.Actors[0].Phase=doors.Actors[1].Phase=DoorPhase.Idle;
        // Independent point/frustum oracle: any visible point in the existing
        // animated envelope must retain the actor at all viewport/horizon edges.
        int rejected=0,partial=0;
        foreach(float focus in new[]{1f,80f,159f})foreach(float height in new[]{0f,2f}){
            state.X=80;state.Z=focus;
            for(int z=0;z<160;z+=2)for(int x=0;x<160;x+=2){
                npc.PosX=x;npc.PosZ=z;view.Prepare(state,doors,camera,0,height);
                if(!view.Visible[1])rejected++;
                var center=CylinderWorld.Surface(z,focus,TerrainElevation.At(state.Town,x,z)+.9f);
                bool centerVisible=RenderView.Sphere(x,center.Y,center.Z,0,80,focus,height)&&RenderView.AboveHorizon(center.Y,center.Z,0,focus,height);
                if(centerVisible)check(view.Visible[1],"frustum-edge culling never removes a visible actor center");
                if(view.Visible[1]&&!centerVisible)partial++;
            }
        }
        check(rejected>0&&partial>0,"visibility rejects outside bounds and retains partially visible silhouettes");
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)view.Prepare(state,doors,camera,0,0);
        check(GC.GetAllocatedBytesForCurrentThread()==before,"shared visibility allocates no frame-time managed memory");
    }
}
