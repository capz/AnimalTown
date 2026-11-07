using AnimalTown;

internal static class GroundRingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var plan=new GroundRingPlan();plan.Prepare(18.5f,39.5f);
        check(plan.Reset&&plan.RowCount==GroundRingPlan.Rows&&plan.ColumnCount==GroundRingPlan.Columns,"first view fills the bounded ring");
        plan.Prepare(18.9f,39.9f);check(!plan.Reset&&plan.RowCount==0&&plan.ColumnCount==0,"fractional movement does not rewrite vertices");
        plan.Prepare(19.1f,39.9f);check(plan.RowCount==0&&plan.ColumnCount==1,"eastward movement reassigns only the entering column");
        plan.Prepare(19.1f,40.1f);check(plan.RowCount==1&&plan.ColumnCount==0,"north/south movement reassigns only the entering row");
        plan.Prepare(-.1f,-.1f);check(plan.MinX==-33&&plan.MinZ==-37,"negative positions use floor rather than truncation");
        plan.Prepare(159.9f,159.9f);check(plan.Reset,"teleport fully refills the ring");
        plan.Prepare(159.9f,159.9f,true);check(plan.Reset,"terrain/material invalidation refills unchanged coordinates");
        check(GroundRingPlan.VertexCount<=65535&&GroundRingPlan.IndexCount%3==0,"one allocation stays within GX index bounds");
        var random=new Random(382);int oldX=0,oldZ=0;plan.Prepare(.5f,.5f);
        for(int step=0;step<80;step++){
            int x=oldX+random.Next(-2,3),z=oldZ+random.Next(-2,3);
            plan.Prepare(x+.25f,z+.75f);check(plan.RowCount==Math.Abs(z-oldZ)&&plan.ColumnCount==Math.Abs(x-oldX),"walking retains overlapping rows and columns");
            var slots=new bool[GroundRingPlan.Columns*GroundRingPlan.Rows];
            for(int cz=plan.MinZ;cz<plan.MinZ+GroundRingPlan.Rows;cz++)for(int cx=plan.MinX;cx<plan.MinX+GroundRingPlan.Columns;cx++){
                int at=GroundRingPlan.Vertex(cx,cz)/4;check(at>=0&&at<slots.Length&&!slots[at],"every retained logical cell owns a unique stable vertex slot");slots[at]=true;
            }
            oldX=x;oldZ=z;
        }
        // Enclose every potentially visible, front-facing flat ground cell,
        // including elevated/lowered focus, fractional movement and all sides.
        foreach(float eye in new[]{-.5f,0,2})foreach(float fraction in new[]{0,.25f,.75f}){
            float fx=80+fraction,fz=80+fraction;plan.Prepare(fx,fz);
            for(int z=20;z<110;z++)for(int x=15;x<145;x++){
                var p=CylinderWorld.Surface(z+.5f,fz);
                if(!RenderView.SurfaceFront(z+.5f,fz,eye)||!RenderView.Sphere(x+.5f,p.Y,p.Z,.8f,fx,fz,eye))continue;
                check(x>=plan.MinX&&x<plan.MinX+GroundRingPlan.Columns&&z>=plan.MinZ&&z<plan.MinZ+GroundRingPlan.Rows,"ring window encloses visible flat ground beyond each viewport edge");
            }
        }
        var town=new Town(5);int cell=80*Town.Width+80;
        town.Tiles[cell]=(byte)Terrain.Grass;town.Elevation[cell]=0;
        check(GroundRingPlan.Uses(town,80,80,0,0),"ordinary matching flat ground uses the ring");
        check(!GroundRingPlan.Uses(town,80,80,1,0)&&!GroundRingPlan.Uses(town,80,80,0,-1),"other materials and disabled mode retain cached geometry");
        for(byte mode=1;mode<=TerrainElevation.MaximumMode;mode++){town.Elevation[cell]=mode;check(!GroundRingPlan.Uses(town,80,80,0,0),"every raised/ramp/corner mode retains its original topology");}
        town.Elevation[cell]=0;
        foreach(var kind in new[]{Terrain.Water,Terrain.Sea,Terrain.Beach,Terrain.CoastNorth,Terrain.CoastEast,Terrain.CoastSouth,Terrain.CoastWest}){
            town.Tiles[cell]=(byte)kind;check(!GroundRingPlan.Uses(town,80,80,0,0),"water and beach/coastal transitions retain cached geometry");
        }
    }
}
