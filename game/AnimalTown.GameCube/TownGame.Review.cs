#if ANIMALTOWN_ONLINE_PLAY
using System;
using AnimalTown;
using AnimalTown.Online;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    OnlineMenuUi? reviewUi;
    Camera? reviewCamera;
    float reviewX,reviewZ;
    long reviewInputAt;
    internal void BeginReview(OnlineMenuUi ui)
    {
        if(!state.IsOnlineProjection||session.Online!=null)throw new InvalidOperationException("Review requires a read-only public scene.");
        reviewUi=ui;reviewCamera=new Camera(CylinderWorld.CameraFieldOfView,device.Display.AspectRatio*.5f,CylinderWorld.Near,CylinderWorld.Far);
        screen="review";clockFade=default;
    }
    internal void UpdateReview(GameState scene)
    {OnlineTownProjection.Update(state,scene);}
    internal void FocusReview(int space,float x,float z)
    {
        if(!BuildingWorld.ValidSpace(space))throw new ArgumentOutOfRangeException(nameof(space));
        room=space;reviewX=x;reviewZ=z;state.X=x;state.Z=z;reviewInputAt=Clock.Timestamp;
        state.Town.NotifyObjectsChanged();
    }
    internal void InspectReview(ControllerState pad)
    {
        long now=Clock.Timestamp;float seconds=Math.Min(.1f,(now-reviewInputAt)/1000000f);reviewInputAt=now;
        float x=Math.Abs(pad.Stick.X)>.18f?pad.Stick.X:0,z=Math.Abs(pad.Stick.Y)>.18f?-pad.Stick.Y:0;
        if(room==0)
        {
            state.X=Math.Clamp(state.X+x*seconds*3,Math.Max(.5f,reviewX-4),Math.Min(Town.Width-.5f,reviewX+4));
            state.Z=Math.Clamp(state.Z+z*seconds*3,Math.Max(.5f,reviewZ-4),Math.Min(Town.Height-.5f,reviewZ+4));
        }
        else indoorCamera.Tick(pad.Stick.X,false);
    }
    void BeginReviewViewport()
    {
        device.SetViewport(0,0,device.Width/2,device.Height);
        device.SetScissor(0,0,device.Width/2,device.Height);
    }
    void SubmitReviewUi()
    {
        device.SetViewport(0,0,device.Width,device.Height);
        device.SetScissor(0,0,device.Width,device.Height);
        reviewUi!.SubmitFramePart();
    }
}
#endif
