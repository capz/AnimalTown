using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    private readonly HelipadRenderer helipad=new HelipadRenderer();
    private bool helipadVisible;

    private void StartHelipadFlight(float x,float z)
    {
        state.Space=0;state.X=x;state.Z=z;faceX=0;faceZ=-1;
        facingYaw=state.Facing=(float)System.Math.PI;
        moveClip=0;desiredPlayerSpeed=.85f;playerRunning=false;animationClock.Reset(0);
        screen="world";
        session.Flight.Board(SaveCards.Device==4?1:-1);
        DebugOverlay.WriteLine("ANIMALTOWN FLIGHT BOARD "+state.Town.Seed);
    }

    internal bool StepHelipadFlight()
    {
        var flight=session.Flight;
        if(!flight.Active)return false;
        // The terrain can be presentable while the destination player model
        // is still cold. Cruise keeps rendering until ReleaseIntro admits it.
        if(!resourcesReady||!terrain.Ready||flight.Phase==FlightPhase.Cruise)return true;
        var before=flight.Phase;
        flight.Tick();
        moveClip=flight.PlayerWalking?0:-1;
        StepActorAnimation(0,state.Species,moveClip);
        if(before!=FlightPhase.Cruise&&flight.Phase==FlightPhase.Cruise){
            DebugOverlay.WriteLine("ANIMALTOWN FLIGHT CRUISE "+flight.Direction);
            TravelRestartRequested=true;
        }
        if(!flight.Active){
            facingYaw=state.Facing=0;faceX=0;faceZ=1;
            DebugOverlay.WriteLine("ANIMALTOWN FLIGHT ARRIVED "+state.Town.Seed);
        }
        return true;
    }

    private void PrepareHelipad()
    {
        helipadVisible=room==0&&HelipadSite.Exists(state.Town)&&
            (session.Flight.Active||Visible(HelipadSite.CenterX,HelipadSite.CenterZ,3.1f));
        if(helipadVisible){helipad.PreparePad();helipad.PrepareCraft();}
    }

    private void DrawHelipad(int tint)
    {
        if(!helipadVisible)return;
        const float z=HelipadSite.CenterZ;
        helipad.DrawPad(renderer,SurfacePoint(HelipadSite.CenterX,z),SurfaceRotation(z),tint);
        var flight=session.Flight;
        flight.CraftPose(out float x,out float height,out float yaw,out float door);
        float rotor=flight.PlayerOnboard?Clock.Timestamp%2000000/125000f:.28f;
        helipad.DrawCraft(renderer,SurfacePoint(x,z,height),SurfaceRotation(z,yaw),door,rotor,tint);
    }
}
