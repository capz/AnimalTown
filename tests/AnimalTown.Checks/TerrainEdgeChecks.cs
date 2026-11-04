using AnimalTown;

internal static class TerrainEdgeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        WaterSurfaceChecks.Run(check);
        for(int edge=0;edge<4;edge++)for(int depth=2;depth<CylinderWorld.Far-2;depth+=3){
            float x=0,up=0;
            float tangent=edge<2?RenderView.TanX:RenderView.TanY;
            float normal=(float)Math.Sqrt(1+tangent*tangent);
            float offset=depth*tangent+(TerrainTiles.GroundCellRadius+.5f)*normal;
            if(edge<2)x=(edge==0?-1:1)*offset;else up=(edge==2?-1:1)*offset;
            float y=CylinderWorld.CameraHeight+depth*RenderView.ForwardY-up*RenderView.ForwardZ;
            float z=CylinderWorld.CameraBack+depth*RenderView.ForwardZ+up*RenderView.ForwardY;
            check(!RenderView.Sphere(x,y,z,TerrainTiles.GroundCellRadius,0,0),"regression point falls outside the former tight terrain bound");
            check(RenderView.Sphere(x,y,z,TerrainTiles.GroundVisibilityRadius,0,0),"terrain keeps a half-cell of extra leeway at each viewport plane");
        }
        check(!RenderView.Sphere(100,0,0,TerrainTiles.GroundVisibilityRadius,0,0),"terrain far outside the viewport is still rejected");
        var data=new byte[FloorStrips.Records];int[] header={0x32525441,1,13,13,FloorTexture.Maximum};
        void Put(int at,int value){for(int i=0;i<4;i++)data[at+i]=(byte)(value>>(i*8));}
        for(int i=0;i<header.Length;i++)Put(i*4,header[i]);
        for(int s=0;s<13;s++){Put(20+s*20,1);Put(24+s*20,s);for(int v=1;v<4;v++)Put(24+s*20+v*4,-1);}
        FloorStrips.Validate(data);
        var state=new GameState(17);var strips=new FloorStrips();
        // Alternating materials exercise the maximum split cost rather than
        // relying on a uniform floor that merges into one strip per row.
        for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++)state.Town.Tiles[z*Town.Width+x]=(byte)((x+z)%2);
        int extra=0,visibleSamples=0,maximum=0;
        for(int offset=0;offset<16;offset++){
            state.X=99.01f+offset*.137f;state.Z=99.01f+offset*.113f;
            strips.Prepare(state,0,data);maximum=Math.Max(maximum,strips.Count);
            check(strips.Count<=FloorStrips.Capacity&&strips.Count*6<=8190,"padded checkerboard floor fits the existing geometry budget");
            int snapZ=(int)state.Z/2*2,centerX=(int)state.X/2*2;
            // Audit well outside the generated window too: reducing lookahead
            // must never hide a visible or padded cell by omitting its row.
            for(int z=snapZ-64;z<=snapZ+40;z++){
                var a=CylinderWorld.Surface(z,state.Z);var b=CylinderWorld.Surface(z+1,state.Z);
                if(!RenderView.SurfaceFront(z,state.Z)&&!RenderView.SurfaceFront(z+1,state.Z))continue;
                for(int x=centerX-64;x<centerX+64;x++){
                    bool old=RenderView.Sphere(x+.5f,(a.Y+b.Y)*.5f,(a.Z+b.Z)*.5f,.8f,state.X,state.Z);
                    bool padded=RenderView.Sphere(x+.5f,(a.Y+b.Y)*.5f,(a.Z+b.Z)*.5f,TerrainTiles.GroundVisibilityRadius,state.X,state.Z);
                    bool onscreen=false;
                    for(int dz=0;dz<=2;dz++)for(int dx=0;dx<=2;dx++){
                        float y=a.Y+(b.Y-a.Y)*dz*.5f,pz=a.Z+(b.Z-a.Z)*dz*.5f;
                        onscreen|=Inside(x+dx*.5f,y,pz,state.X,state.Z);
                    }
                    if(!onscreen&&!padded)continue;
                    int hits=0;for(int n=0;n<strips.Count;n++)if(strips.Z0[n]==z&&x>=strips.X0[n]&&x<strips.X1[n])hits++;
                    check(hits==1,"on-screen and guard-band cells survive row bounds and strip merging exactly once");
                    if(onscreen)visibleSamples++;if(padded&&!old)extra++;
                }
            }
        }
        check(extra>500&&visibleSamples>1000,"fractional-camera sweep retains visible cells and a real extra border");
        Console.WriteLine("Terrain edge sweep: "+extra+" extra cells retained; maximum checkerboard strips "+maximum+" / "+FloorStrips.Capacity);
    }
    private static bool Inside(float x,float y,float z,float fx,float fz){
        float dy=y-CylinderWorld.CameraHeight,dz=z-fz-CylinderWorld.CameraBack;
        float depth=dy*RenderView.ForwardY+dz*RenderView.ForwardZ,up=-dy*RenderView.ForwardZ+dz*RenderView.ForwardY;
        return depth>=CylinderWorld.Near&&depth<=CylinderWorld.Far&&Math.Abs(x-fx)<=depth*RenderView.TanX&&Math.Abs(up)<=depth*RenderView.TanY;
    }
}
