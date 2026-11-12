using AnimalTown;
using Dolphin.Graphics;

internal static class RainMemoryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        GC.KeepAlive(new RainRenderer());
        long before=GC.GetAllocatedBytesForCurrentThread();var rain=new RainRenderer();
        check(GC.GetAllocatedBytesForCurrentThread()-before<2048,"dry sessions construct rain policy without a 6 KiB vertex stream");
        var state=new GameState(7319);var reference=new VertexPositionColor[DebugWeather.DropCount*6];
        foreach(var focus in new[]{(1f,1f),(80.25f,80.75f),(159f,159f)})foreach(float eye in new[]{0f,.5f,2f})foreach(double seconds in new[]{0.0,2.75,3600.125}){
            state.X=focus.Item1;state.Z=focus.Item2;
            var actual=rain.Stage(state,seconds,eye);Original(state,seconds,eye,reference);
            check(actual.Length==384,"rain keeps the full fixed native stream and degenerate invisible slots");
            for(int i=0;i<actual.Length;i++)check(actual[i].Position.X==reference[i].Position.X&&actual[i].Position.Y==reference[i].Position.Y&&actual[i].Position.Z==reference[i].Position.Z&&actual[i].Color.Packed==reference[i].Color.Packed,"lazy rain staging preserves every original triangle, cull result and painter-order slot");
        }
        var first=rain.Stage(state,1,0);
        before=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)rain.Stage(state,i*.1,0);
        check(GC.GetAllocatedBytesForCurrentThread()==before&&ReferenceEquals(first,rain.Stage(state,101,0)),"rain reuses its warm staging stream without frame allocations");
        rain.Dispose();rain.Dispose();
        before=GC.GetAllocatedBytesForCurrentThread();var restarted=rain.Stage(state,1,0);long restartBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        check(!ReferenceEquals(first,restarted)&&restartBytes>=6144&&restartBytes<6400,"disposing rain releases its staging reference and reentry allocates exactly one bounded stream");
        Original(state,1,0,reference);for(int i=0;i<restarted.Length;i++)check(restarted[i].Position.X==reference[i].Position.X&&restarted[i].Position.Y==reference[i].Position.Y&&restarted[i].Position.Z==reference[i].Position.Z,"rain reentry regenerates the same geometry after disposal");
        rain.Dispose();
    }
    private static void Original(GameState state,double seconds,float eyeHeight,VertexPositionColor[] vertices)
    {
        for(int i=0;i<DebugWeather.DropCount;i++){
            DebugWeather.Drop(i,state.X,state.Z,seconds,out float x,out float z,out float h);
            var p=CylinderWorld.Surface(z,state.Z,TerrainElevation.At(state.Town,x,z)+h);
            bool draw=RenderView.Sphere(x,p.Y,p.Z,.8f,state.X,state.Z,eyeHeight)&&RenderView.AboveHorizon(p.Y,p.Z,.8f,state.Z,eyeHeight);
            int at=i*6;
            var a=new VertexPositionColor(draw?new Vector3(x-.015f,p.Y,p.Z):Vector3.Zero,Color32.White);
            var b=new VertexPositionColor(draw?new Vector3(x-.11f,p.Y+.7f,p.Z):Vector3.Zero,Color32.White);
            var c=new VertexPositionColor(draw?new Vector3(x-.08f,p.Y+.7f,p.Z):Vector3.Zero,Color32.White);
            var d=new VertexPositionColor(draw?new Vector3(x+.015f,p.Y,p.Z):Vector3.Zero,Color32.White);
            vertices[at]=a;vertices[at+1]=b;vertices[at+2]=c;vertices[at+3]=a;vertices[at+4]=c;vertices[at+5]=d;
        }
    }
}
