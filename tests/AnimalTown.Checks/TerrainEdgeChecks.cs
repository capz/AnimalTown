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
        ScrollChecks(check,data);
        StreamChecks(check,data);
        var state=new GameState(17);var strips=new FloorStrips();
        // Alternating materials exercise the maximum split cost rather than
        // relying on a uniform floor that merges into one strip per row.
        for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++)state.Town.Tiles[z*Town.Width+x]=(byte)((x+z)%2==0?Terrain.Grass:Terrain.Plaza);
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
    private static void ScrollChecks(Action<bool,string> check,byte[] data)
    {
        // A rigid transform must agree with fresh analytic cylinder samples,
        // including normals, elevated ground and the row chords used by banks.
        float maxError=0;
        for(int anchor=1;anchor<Town.Height;anchor+=18)for(int offset=-12;offset<=12;offset++)foreach(int reference in new[]{0,anchor}){
            float focus=anchor+offset*.083f;var scroll=CylinderWorld.Scroll(reference,focus);
            for(int row=-44;row<=24;row+=2)foreach(float height in new[]{-.5f,0f,.008f,2f}){
                var point=CylinderWorld.Surface(anchor+row,reference,height);var moved=scroll.Apply(point);var expected=CylinderWorld.Surface(anchor+row,focus,height);
                float error=Math.Max(Math.Abs(moved.Y-expected.Y),Math.Abs(moved.Z-expected.Z));maxError=Math.Max(maxError,error);
                check(error<.00006f&&Math.Abs(moved.UpY-expected.UpY)<.000001f&&Math.Abs(moved.UpZ-expected.UpZ)<.000001f,"one axis transform exactly preserves cylinder vertices and radial normals");
                var next=CylinderWorld.Surface(anchor+row+1,reference,height);var chord=new CylinderPose((point.Y+next.Y)*.5f,(point.Z+next.Z)*.5f,0,0,0);
                var transformedChord=scroll.Apply(chord);var expectedNext=CylinderWorld.Surface(anchor+row+1,focus,height);
                check(Math.Abs(transformedChord.Y-(expected.Y+expectedNext.Y)*.5f)<.00006f&&Math.Abs(transformedChord.Z-(expected.Z+expectedNext.Z)*.5f)<.00006f,"rigid scrolling keeps bank toes, foam and floor chords coincident");
            }
        }
        // One cached selection must cover the complete sub-cell journey, at
        // both terrace heights. Alternate materials stress strip capacity.
        var state=new GameState(17);var strips=new FloorStrips();Array.Clear(state.Town.Elevation);
        for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++)state.Town.Tiles[z*Town.Width+x]=(byte)((x+z)%2==0?Terrain.Grass:Terrain.Plaza);
        int maximum=0;
        foreach(byte elevation in new byte[]{0,TerrainElevation.Block}){
            Array.Fill(state.Town.Elevation,elevation);state.X=98.01f;state.Z=98.01f;strips.Prepare(state,0,data,scrollWindow:true);
            maximum=Math.Max(maximum,strips.Count);check(strips.Count<=FloorStrips.Capacity,"cached checkerboard remains within the existing strip budget");
            var selected=new HashSet<int>();for(int n=0;n<strips.Count;n++)for(int x=strips.X0[n];x<strips.X1[n];x++)selected.Add(strips.Z0[n]*1024+x);
            for(int iz=0;iz<5;iz++)for(int ix=0;ix<5;ix++){
                state.X=98.001f+ix*.4995f;state.Z=98.001f+iz*.4995f;
                check(CylinderWorld.ScrollAnchor(state.X)==99&&CylinderWorld.ScrollAnchor(state.Z)==99,"fractional scrolling retains a common cache key");
                float h=elevation==0?0:2;
                for(int z=34;z<140;z++){
                    var a=CylinderWorld.Surface(z,state.Z,h);var b=CylinderWorld.Surface(z+1,state.Z,h);
                    if(!RenderView.SurfaceFront(z,state.Z,h)&&!RenderView.SurfaceFront(z+1,state.Z,h))continue;
                    for(int x=34;x<164;x++)if(RenderView.Sphere(x+.5f,(a.Y+b.Y)*.5f,(a.Z+b.Z)*.5f,TerrainTiles.GroundVisibilityRadius,state.X,state.Z,h))
                        check(selected.Contains(z*1024+x),"cached window covers the padded viewport at every fractional offset and terrace height");
                }
                var previous=selected;strips.Prepare(state,0,data,scrollWindow:true);
                var current=new HashSet<int>();for(int n=0;n<strips.Count;n++)for(int x=strips.X0[n];x<strips.X1[n];x++)current.Add(strips.Z0[n]*1024+x);
                check(previous.SetEquals(current),"sub-cell movement never changes cached topology or UV source cells");
            }
        }
        check(CylinderWorld.ScrollAnchor(99.999f)!=CylinderWorld.ScrollAnchor(100)&&CylinderWorld.ScrollAnchor(-.001f)==-1,"window keys advance exactly at scroll boundaries, including negative edge coordinates");
        Console.WriteLine("Rigid terrain scroll: maximum error "+maxError+"m; cached checkerboard strips "+maximum+" / "+FloorStrips.Capacity);
    }
    private static bool Inside(float x,float y,float z,float fx,float fz){
        float dy=y-CylinderWorld.CameraHeight,dz=z-fz-CylinderWorld.CameraBack;
        float depth=dy*RenderView.ForwardY+dz*RenderView.ForwardZ,up=-dy*RenderView.ForwardZ+dz*RenderView.ForwardY;
        return depth>=CylinderWorld.Near&&depth<=CylinderWorld.Far&&Math.Abs(x-fx)<=depth*RenderView.TanX&&Math.Abs(up)<=depth*RenderView.TanY;
    }
    private static void StreamChecks(Action<bool,string> check,byte[] data)
    {
        var stream=new TerrainStream();int maximum=0,updates=0;
        void Tick(float x,float z){stream.Prepare(x,z);maximum=Math.Max(maximum,stream.WantedCount);if(stream.BuildSlot>=0){check(stream.X[stream.BuildSlot]%2==0&&stream.Z[stream.BuildSlot]%2==0,"stream sections own complete diagonal cliff templates");stream.Complete();updates++;}}
        foreach(float h in new[]{0f,2f}){
            stream.Reset();for(int n=0;n<TerrainStream.Capacity;n++)Tick(80.01f,80.01f);
            check(stream.Ready&&stream.MissingWanted==0,"cold stream prepares visible ground and prefetch within its fixed slot pool");
            for(int n=0;n<500;n++){
                float x=80.01f+n*.1187f,z=80.01f+n*.1187f;Tick(x,z);
                check(stream.Ready,"running diagonally never exposes an unbuilt section or triggers a full refresh");
                for(int cz=(int)z-45;cz<(int)z+20;cz++){
                    var p=CylinderWorld.Surface(cz+.5f,z,h);
                    for(int cx=(int)x-50;cx<(int)x+50;cx++){
                        if(!RenderView.Sphere(cx+.5f,p.Y,p.Z,.8f,x,z,h)||!RenderView.AboveHorizon(p.Y,p.Z,.8f,z,h))continue;
                        int hits=0;for(int i=0;i<stream.Count;i++)if(stream.Visible[i]&&cx>=stream.X[i]&&cx<stream.X[i]+TerrainStream.Width&&cz>=stream.Z[i]&&cz<stream.Z[i]+TerrainStream.Height)hits++;
                        check(hits==1,"visible terrain has exactly one ready section throughout continuous scrolling");
                    }
                }
            }
        }
        // A reverse journey must recycle slots rather than retaining a town.
        float finish=80.01f+499*.1187f;
        for(int n=0;n<500;n++){Tick(finish-n*.1187f,finish-n*.1187f);check(stream.Ready,"fast direction reversal retains the prefetch guard");}
        int before=updates;for(int n=0;n<300;n++)Tick(80.01f,80.01f);
        check(updates-before<TerrainStream.Capacity,"stationary streaming settles without continuous rebuilding");
        before=updates;for(int n=0;n<10;n++)Tick(80.02f,80.02f);check(updates==before,"fractional movement reuses cached section geometry");
        stream.Reset();Tick(1,1);check(!stream.Ready,"cold teleport requires a covered loading transition rather than a terrain construction burst");
        foreach(float x in new[]{.001f,159.999f})foreach(float z in new[]{.001f,159.999f}){stream.Reset();for(int n=0;n<TerrainStream.Capacity;n++)Tick(x,z);check(stream.Ready&&stream.MissingWanted==0,"streaming at every town boundary remains bounded and complete");}
        var state=new GameState(17);Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);Array.Clear(state.Town.Elevation);
        var strips=new FloorStrips(TerrainStream.Width*TerrainStream.Height);strips.PrepareRegion(state,data,80,80,TerrainStream.Width,TerrainStream.Height);
        check(strips.Count==TerrainStream.Height,"uniform sections retain merged horizontal floor strips");
        for(int z=80;z<82;z++)for(int x=80;x<88;x++)state.Town.Tiles[z*Town.Width+x]=(byte)((x+z)%2==0?Terrain.Grass:Terrain.Plaza);
        strips.PrepareRegion(state,data,80,80,TerrainStream.Width,TerrainStream.Height);check(strips.Count==TerrainStream.Width*TerrainStream.Height,"maximum material splits fit the small shared staging plan");
        Console.WriteLine("Terrain stream: maximum "+maximum+" wanted sections / "+TerrainStream.Capacity+"; one section per frame, scrolling coverage and recycling passed");
    }
}
