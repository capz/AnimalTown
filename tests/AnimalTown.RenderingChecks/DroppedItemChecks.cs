using AnimalTown;
using Dolphin.Graphics;

internal static class DroppedItemChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var town=new Town(7319);Array.Fill(town.Tiles,(byte)Terrain.Grass);Array.Clear(town.Elevation);town.GroundCount=6;
        for(int i=0;i<6;i++){var g=town.Ground[i];g.X=29+i%3;g.Z=32+i;g.Kind=i<2?0:i-1;}
        var plan=new DroppedItemPlan();check(plan.Prepare(town,30,38,0)&&plan.Count==2,"only ordinary dropped items become sprites");
        long revision=plan.Revision;check(!plan.Prepare(town,30,38,0)&&revision==plan.Revision,"unchanged stationary drops reuse their GPU geometry");
        town.Ground[0].X++;check(plan.Prepare(town,30,38,0),"moving a drop invalidates its retained geometry");
        town.Ground[0].Kind=1;check(plan.Prepare(town,30,38,0)&&plan.Count==1,"burying a drop removes its billboard");
        town.Ground[0].Kind=0;town.Ground[0].Z=120;check(plan.Prepare(town,30,38,0)&&plan.Count==1,"offscreen and cylinder-hidden drops are culled");
        town.Ground[0].Z=32;town.Elevation[32*Town.Width+town.Ground[0].X]=2;town.NotifyTerrainChanged();plan.Prepare(town,30,38,0);
        var ground=CylinderWorld.Surface(32.5f,38,TerrainElevation.At(town,town.Ground[0].X+.5f,32.5f));
        check(Math.Abs(plan.Y[0]-ground.Y-DroppedItemPlan.GroundOffset)<.00001f,"dropped sprite follows local raised terrain and cylinder height");
        check(plan.Prepare(town,30,38,.5f),"eye height updates culling and sorting");
        town.GroundCount=0;check(plan.Prepare(town,30,38,0)&&plan.Count==0,"picking up the last drop clears its geometry");
        town.GroundCount=town.Ground.Length;check(town.GroundCount==DroppedItemPlan.Capacity,"sprite capacity covers every persisted ground slot");
        for(int i=0;i<town.GroundCount;i++){var g=town.Ground[i];g.Kind=0;g.X=29+i%3;g.Z=30+i%7;}
        Array.Clear(town.Elevation);town.NotifyTerrainChanged();plan.Prepare(town,30,38,0);
        check(plan.Count==512,"all 512 visible drops fit without arbitrary truncation");
        for(int i=1;i<plan.Count;i++)check(plan.Depth[plan.Order[i-1]]>=plan.Depth[plan.Order[i]],"alpha sprites are sorted back to front");
        var vertices=new VertexPositionColorTexture[plan.Count*6];DroppedItemRenderer.WriteVertices(plan,vertices);
        for(int i=0;i<vertices.Length;i+=3){
            var a=vertices[i].Position;var b=vertices[i+1].Position;var c=vertices[i+2].Position;
            float nx=(b.Y-a.Y)*(c.Z-a.Z)-(b.Z-a.Z)*(c.Y-a.Y),ny=(b.Z-a.Z)*(c.X-a.X)-(b.X-a.X)*(c.Z-a.Z),nz=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
            check(ny*RenderView.ForwardY+nz*RenderView.ForwardZ>0&&Math.Abs(nx)<.00001f,"billboard winding faces camera with clockwise GX culling");
            check(Math.Abs((b.Y-a.Y)*RenderView.ForwardY+(b.Z-a.Z)*RenderView.ForwardZ)<.00001f,"quad is screen aligned rather than edge-on at the camera pitch");
        }
        for(int i=0;i<10;i++)plan.Prepare(town,30,38+i*.01f,0);
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<200;i++)plan.Prepare(town,30,38+i*.001f,0);
        check(GC.GetAllocatedBytesForCurrentThread()==before,"moving and sorting the maximum sprite count allocates no frame garbage");
    }
}
