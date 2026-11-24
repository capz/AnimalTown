using AnimalTown;

internal static class WorldObjectCandidateChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(36);var town=state.Town;
        town.GroundCount=512;town.PlacedCount=256;
        for(int i=0;i<town.GroundCount;i++){
            var g=town.Ground[i];g.X=i%32*4;g.Z=i/32*8;g.Kind=i%4;g.Item=i%3+1;
        }
        for(int i=0;i<town.PlacedCount;i++){
            var p=town.Placed[i];p.X=i%16*8;p.Z=i/16*8;p.Room=i%5==0?1:i%7==0?16:0;
        }
        var candidates=new WorldObjectCandidates();var seen=new bool[512];int accepted=0;
        foreach(bool wide in new[]{false,true})foreach(float eye in new[]{0f,2f}){
            RenderView.SetWidescreen(wide);
            for(int z=8;z<Town.Height;z+=16)for(int x=8;x<Town.Width;x+=16){
                candidates.Prepare(state,x,z,eye,0);Array.Clear(seen);
                for(int n=0;n<candidates.GroundCount;n++){
                    int index=candidates.GroundIndices[n];check(!seen[index]&&(n==0||candidates.GroundIndices[n-1]<index),"shared object candidates preserve source order and uniqueness");seen[index]=true;
                }
                accepted+=candidates.GroundCount;
                for(int i=0;i<town.GroundCount;i++){
                    var g=town.Ground[i];
                    if(TreeDrawBatch.IsTree(g)&&TreeDrawBatch.Visible(town,g,x,z,eye))check(seen[i],"bin culling retains every precisely visible tree across camera, terrace and aspect sweeps");
                    var p=CylinderWorld.Surface(g.Z,z,TerrainElevation.At(town,g.X,g.Z)+.25f);
                    if(RenderView.Sphere(g.X+.5f,p.Y,p.Z,.7f,x,z,eye)&&RenderView.AboveHorizon(p.Y,p.Z,.7f,z,eye))check(seen[i],"bin culling retains visible low ground objects");
                }
                Array.Clear(seen);
                for(int n=0;n<candidates.PlacementCount;n++){int i=candidates.PlacementIndices[n];check(!seen[i]&&town.Placed[i].Room==0,"outdoor candidates contain only unique town placements");seen[i]=true;}
                for(int i=0;i<town.PlacedCount;i++){
                    var q=town.Placed[i];if(q.Room!=0)continue;
                    var p=CylinderWorld.Surface(q.Z,z,TerrainElevation.At(town,q.X,q.Z)+.35f);
                    if(RenderView.Sphere(q.X+.35f,p.Y,p.Z,.8f,x,z,eye)&&RenderView.AboveHorizon(p.Y,p.Z,.8f,z,eye))check(seen[i],"bin culling retains every visible placement");
                }
            }
        }
        check(candidates.Rebuilds==1,"camera movement and aspect switches do not rescan world records");
        check(accepted<512*64*2*2/2,"spatial broad phase rejects at least half the distant records in the camera sweep");
        candidates.Prepare(state,20,20,0,16);int indoor=0;
        for(int i=0;i<town.PlacedCount;i++)if(town.Placed[i].Room==16)indoor++;
        check(candidates.GroundCount==0&&candidates.PlacementCount==indoor,"indoor candidates directly select only the current room");
        town.Placed[0].Room=16;town.Placed[0].X=4;town.NotifyObjectsChanged();candidates.Prepare(state,20,20,0,16);
        check(candidates.PlacementCount==indoor+1&&candidates.PlacementIndices[0]==0,"same-count placement edits invalidate spatial membership");
        town.Ground[0].X=20;town.Ground[0].Z=20;town.NotifyCastersChanged();candidates.Prepare(state,20,20,0,0);
        check(candidates.GroundIndices.Take(candidates.GroundCount).Contains(0),"same-count ground edits invalidate spatial membership");
        int rebuilds=candidates.Rebuilds;long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<10000;i++)candidates.Prepare(state,20,20,0,0);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated&&candidates.Rebuilds==rebuilds,"unchanged shared candidate selection is allocation free");
        var batch=new int[32];int cursor=0,total=0;
        while(cursor<candidates.GroundCount)total+=TreeDrawBatch.Fill(town,candidates,20,20,0,batch,ref cursor);
        int expected=0;for(int i=0;i<town.GroundCount;i++)if(TreeDrawBatch.IsTree(town.Ground[i])&&TreeDrawBatch.Visible(town,town.Ground[i],20,20,0))expected++;
        check(total==expected,"tree batches consume shared candidates without changing visibility");
        RenderView.SetWidescreen(false);
        Console.WriteLine("Spatial candidates: visible-object coverage, source ordering, edits, rooms and zero-allocation reuse passed");
    }
}
