using System;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using Dolphin.Timing;

// Loading owns just the aircraft and a small scrolling sky. The departure
// renderer has unwound before this scene is created.
internal sealed class HelipadCruise : IDisposable,Hypercube.GameCube.IFrameScene
{
    private const long MinimumCruise=1200000,ExitTime=460000;
    private readonly HelipadRenderer art=new HelipadRenderer();
    private readonly Renderer renderer=new Renderer(GraphicsDevice.Default);
    private readonly Camera camera=new Camera(53.54f,0,.1f,42);
    private readonly long started=Clock.Timestamp;
    private readonly bool performance=DebugOverlay.PerformanceVisible,text=DebugOverlay.TextVisible;
    private readonly int direction;
    private long now,exitAt;

    internal HelipadCruise(int direction)
    {
        this.direction=direction;art.PrepareCraft();art.PrepareCloud();
        camera.LookAt(new Vector3(0,3.05f,9.6f),new Vector3(0,1.6f,0),new Vector3(0,1,0));
    }

    internal bool FinishWhenReady()
    {
        long time=Clock.Timestamp;
        if(time-started<MinimumCruise)return false;
        if(exitAt==0)exitAt=time;
        return time-exitAt>=ExitTime;
    }

    internal void Draw()
    {
        now=Clock.Timestamp;
        Hypercube.GameCube.FrameRenderer.Draw(GraphicsDevice.Default,new Color32(156,204,216),this);
    }

    public void SubmitFramePart()
    {
        DebugOverlay.PerformanceVisible=false;DebugOverlay.TextVisible=false;
        float age=(now-started)/1000000f;
        float crossing=Math.Min(1,(now-started)/(float)MinimumCruise);
        float exit=exitAt==0?0:Math.Min(1,(now-exitAt)/(float)ExitTime);
        float x=direction*(-1.45f+5.65f*crossing+4.9f*exit);
        var rotation=direction>0?new Quaternion(0,0,0,1):new Quaternion(0,1,0,0);
        renderer.Begin3D(camera);
        for(int i=0;i<4;i++){
            float drift=(i*8.5f+age*(1.7f+i*.17f))%34f;
            art.DrawCloud(renderer,direction*(17f-drift),.28f+(i%2)*1.9f,-7.1f-i);
        }
        art.DrawCraft(renderer,new Vector3(x,1.3f+.11f*(float)Math.Sin(age*3.4f),0),rotation,0,age*16,0xffffff);
        renderer.End3D();
    }

    public void Dispose()
    {
        art.Dispose();
        DebugOverlay.PerformanceVisible=performance;DebugOverlay.TextVisible=text;
    }
}
