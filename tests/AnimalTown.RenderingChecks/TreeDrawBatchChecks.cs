using AnimalTown;

internal static class TreeDrawBatchChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var state=new GameState(17){X=80,Z=80};var town=state.Town;
        Array.Fill(town.Tiles,(byte)Terrain.Grass);Array.Clear(town.Elevation);
        town.GroundCount=63;
        for(int i=0;i<town.GroundCount;i++)town.Ground[i]=new GroundItem{Kind=3,Item=1+i%3,X=76+i%8,Z=79-i/8};
        var batch=new int[TreeDrawBatch.Capacity];var seen=new bool[town.GroundCount];
        int cursor=0,total=0,batches=0;
        while(cursor<town.GroundCount){
            int count=TreeDrawBatch.Fill(town,state.X,state.Z,0,batch,ref cursor);batches++;
            check(count>0&&count<=batch.Length,"dense forest draws bounded nonempty batches");
            for(int i=0;i<count;i++){check(!seen[batch[i]],"a visible tree is submitted exactly once");seen[batch[i]]=true;total++;}
        }
        check(total==town.GroundCount&&batches>1&&seen.All(x=>x),"dense forest retains every visible tree beyond the old nearest-32 limit, including the final partial batch");
        int completed=cursor;check(TreeDrawBatch.Fill(town,80,80,0,batch,ref cursor)==0&&cursor==completed,"an exhausted batch does not replay stale tree indices");
        // Sample real mesh vertices against an independent point projection and
        // ray/cylinder occlusion test, including raised ground and widescreen.
        int silhouettes=0;
        foreach(bool wide in new[]{false,true})foreach(float eyeHeight in new[]{0f,2f})foreach(float groundHeight in new[]{0f,2f}){
            RenderView.SetWidescreen(wide);Array.Fill(town.Elevation,(byte)(groundHeight==0?0:1));
            foreach(float scale in new[]{.75f,3f})for(int kind=0;kind<3;kind++){
                var mesh=LowPolyTree.Build(kind);
                for(int dz=-65;dz<=10;dz++)for(int dx=-35;dx<=35;dx+=5){
                    var item=new GroundItem{Kind=scale<1?2:3,Item=kind==1?2:kind==2?3:1,X=80+dx,Z=80+dz};
                    var origin=CylinderWorld.Surface(item.Z,80,groundHeight);bool visiblePoint=false;
                    for(int at=0;at<mesh.Triangles*9&&!visiblePoint;at+=3){
                        float x=item.X+mesh.Positions[at]*scale;
                        float y=origin.Y+(mesh.Positions[at+1]*origin.UpY-mesh.Positions[at+2]*origin.UpZ)*scale;
                        float z=origin.Z+(mesh.Positions[at+1]*origin.UpZ+mesh.Positions[at+2]*origin.UpY)*scale;
                        visiblePoint=PointVisible(x,y,z,eyeHeight);
                    }
                    if(!visiblePoint)continue;
                    check(TreeDrawBatch.Visible(town,item,80,80,eyeHeight),"tree bounds retain every actual vertex visible above the horizon or through a viewport edge");
                    if(!PointVisible(item.X,origin.Y,origin.Z,eyeHeight))silhouettes++;
                }
            }
        }
        check(silhouettes>0,"tree sweep includes canopies whose bases have already passed the horizon");
        RenderView.SetWidescreen(false);Array.Clear(town.Elevation);cursor=0;TreeDrawBatch.Fill(town,80,80,0,batch,ref cursor);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int frame=0;frame<100;frame++){cursor=0;while(cursor<town.GroundCount)TreeDrawBatch.Fill(town,80,80,0,batch,ref cursor);}
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"tree batch selection allocates no frame-time memory");
        Console.WriteLine("Tree horizon silhouettes and dense-forest batch checks passed");
    }
    private static bool PointVisible(float x,float y,float z,float eyeHeight)
    {
        double dy=y-(CylinderWorld.CameraHeight+eyeHeight),dz=z-(80+CylinderWorld.CameraBack);
        double depth=dy*RenderView.ForwardY+dz*RenderView.ForwardZ,up=-dy*RenderView.ForwardZ+dz*RenderView.ForwardY;
        if(depth<CylinderWorld.Near||depth>CylinderWorld.Far||Math.Abs(x-80)>depth*RenderView.TanX||Math.Abs(up)>depth*RenderView.TanY)return false;
        double ey=CylinderWorld.Radius+CylinderWorld.CameraHeight+eyeHeight,ez=CylinderWorld.CameraBack;
        double a=dy*dy+dz*dz,b=2*(ey*dy+ez*dz),c=ey*ey+ez*ez-CylinderWorld.Radius*CylinderWorld.Radius;
        double discriminant=b*b-4*a*c;
        if(discriminant<=0)return true;
        double entry=(-b-Math.Sqrt(discriminant))/(2*a);
        return entry<0||entry>=1-.000001;
    }
}
