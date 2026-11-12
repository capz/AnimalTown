using AnimalTown;

internal static class WaterSurfaceChecks
{
    internal static void Run(Action<bool,string> check)
    {
        RiverCoastChecks.Run(check);
        RiverBankChecks.Run(check);
        CloudShadowChecks.Run(check);
        DebugWeatherChecks.Run(check);
        RenderDebugChecks.Run(check);
        var state=new GameState(49);var town=state.Town;
        Array.Fill(town.Tiles,(byte)Terrain.Water);Array.Clear(town.Elevation);
        check(WaterSurface.Depth(town,80,80)==1&&WaterSurface.Depth(town,-10,-10)==1,"open sea and pond interiors use deep tint, including outside world bounds");
        town.SetTerrain(80,80,Terrain.Grass);
        check(WaterSurface.Depth(town,80,80)==0&&WaterSurface.Depth(town,81,81)==0,"all four bank corners share shallow tint");
        check(WaterSurface.Depth(town,82,81)>0&&WaterSurface.Depth(town,82,81)<WaterSurface.Depth(town,83,81)&&WaterSurface.Depth(town,84,81)==1,"shore tint deepens smoothly over three cells");
        town.SetTerrain(80,80,Terrain.Bridge);
        check(!WaterSurface.IsBank(town,80,80)&&WaterSurface.Depth(town,80,80)==1,"bridges neither emit foam nor pinch river depth tint");
        check(!WaterSurface.IsBank(town,-1,0)&&WaterSurface.IsWater(town,-1,0),"map boundary continues as ocean without a false foam seam");
        check(WaterSurface.Height==WaterTerrain.RiverHeight,"all water sits on the same plane beneath ground and bridges");
        foreach(bool ripples in new[]{false,true}){
            var texture=WaterSurface.Texture(ripples);check(texture.Length==4096,"each GX I8 map uses exactly four KiB");
            int at=0,nonzero=0,bright=0;for(int by=0;by<64;by+=4)for(int bx=0;bx<64;bx+=8)for(int y=by;y<by+4;y++)for(int x=bx;x<bx+8;x++){
                int sample=texture[at++];
                check(sample==WaterSurface.Sample(x,y,ripples),"GX 8x4 intensity tile order matches logical pattern");
                check(sample==WaterSurface.Sample(x+64,y-64,ripples),"patterns repeat exactly along both axes");
                check(ripples?sample<=210:sample>=224,"opaque base stays subtle, connected foam retains bounded highlights");
                if(sample>0)nonzero++;
                if(sample>96)bright++;
            }
            if(ripples){
                check(nonzero>4096/8&&nonzero<4096/2,"periodic cellular contour spans substantial but bounded surface coverage");
                check(bright>64&&bright<4096/4,"bright connected highlights leave most water colored");
            }
        }
        var shore=WaterSurface.ShoreTexture();
        check(shore.Length==8192,"IA8 shore texture has alpha and luminance in 4x4 tiled layout");
        int foamAt=0;bool varying=false;byte initial=shore[0];
        for(int by=0;by<64;by+=4)for(int bx=0;bx<64;bx+=4)for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){
            byte alpha=shore[foamAt++],luma=shore[foamAt++];
            check(alpha==WindWakerWaterPattern.ShoreAlpha(x,y)&&alpha==WindWakerWaterPattern.ShoreAlpha(x+64,y-64),
                "shore foam alpha is periodic and GX IA8 tile ordering is deterministic");
            check(luma==255&&alpha>=80,"shore foam colors stay pale without transparent holes");
            if(alpha!=initial)varying=true;
        }
        check(varying,"shoreline mask moves visible opacity, not RGB");
        var warpTex=WaterSurface.WarpTexture();
        check(warpTex.Length==WaterSurface.TextureSize*WaterSurface.TextureSize*2,
            "indirect GX RGB565 mask is a single eight KiB native texture");
        int w=0;bool differentWarp=false;ushort firstWarp=WaterSurface.WarpSample(0,0);
        for(int by=0;by<64;by+=4)for(int bx=0;bx<64;bx+=4)
            for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){
                ushort actual=(ushort)((warpTex[w++]<<8)|warpTex[w++]);
                check(actual==WaterSurface.WarpSample(x,y),"RGB565 displacement follows GX native tile order");
                check(actual==WaterSurface.WarpSample(x+64,y-64),"indirect displacement repeats exactly across tiles");
                if(actual!=firstWarp)differentWarp=true;
            }
        check(differentWarp,"warp map has independent animated displacement components");
        check(WindWakerWaveField.Cells==16&&WindWakerWaveField.EdgeWeight(0,8)==0&&
              WindWakerWaveField.EdgeWeight(8,8)==1&&WindWakerWaveField.EdgeWeight(17,8)==0,
              "small rough-water patch fades into the immutable surrounding sea");
        foreach(double time in new[]{0.0,1,3600,86400,1e8}){
            float flat=WindWakerWaveField.Height(130,135,time,0);
            float lifted=WindWakerWaveField.Height(130,135,time,1);
            check(Math.Abs(flat-WindWakerWaveField.BaseLift)<.0000001f&&lifted>=flat&&lifted<.22f,
                "four-component waves stay above the immutable background, including long sessions");
            check(Math.Abs(WindWakerWaveField.Height(130,135,time+.001,1)-lifted)<.001f,
                "wave motion is continuous and is independent of tile boundaries");
        }
        var grid=new WindWakerWaveGrid();
        foreach(float origin in new[]{-32.5f,0,128,2048})foreach(double time in new[]{0.0,1.25,3600,86400,1e8}){
            grid.Prepare(origin,origin+7,time);
            for(int z=0;z<=WindWakerWaveField.Cells;z++)for(int x=0;x<=WindWakerWaveField.Cells;x++){
                float expected=WindWakerWaveField.Height(origin+x*.5f,origin+7+z*.5f,time,WindWakerWaveField.EdgeWeight(x,z));
                check(Math.Abs(grid.Height(x,z)-expected)<.0000001f,"cached rough-water phases preserve all authored heights, edges and long sessions");
            }
        }
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int frame=0;frame<1000;frame++){grid.Prepare(128,135,frame*.016);grid.Height(8,8);}
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"warm rough-water sampling has no per-frame allocations");
        // The visual effect is encoded in five *independent* grayscale images
        // instead of a run-time procedural pattern or regular box mipmapping.
        for(int level=0;level<SunshineWater.MipLevels;level++)
            check(SunshineWater.AlphaForLuma(level,0)==0&&SunshineWater.AlphaForLuma(level,127)==119&&
                  SunshineWater.AlphaForLuma(level,128)==136&&SunshineWater.AlphaForLuma(level,255)==255,
                  "all I4 levels use the same nearest-nibble intensity/alpha expansion without bespoke opacity gains");
        var artistLevels=new byte[SunshineWater.MipLevels][];
        for(int level=0;level<SunshineWater.MipLevels;level++){
            int size=SunshineWater.SourceEdge(level);
            var pixels=artistLevels[level]=new byte[size*size];
            for(int y=0;y<size;y++)for(int x=0;x<size;x++)
                pixels[y*size+x]=(byte)(level switch{
                    1=>70+(x*7+y*11)%170,
                    2=>160+(x*3+y*5)%96,
                    3=>20+(x+y)%30,
                    _=>0
                });
        }
        byte[] sunshine=SunshineWater.PackLevels(artistLevels);
        SunshineWater.ValidatePackage(sunshine);
        TexturePixels.Validate(sunshine);
        check(TexturePixels.Int(sunshine,0)==0x58544e44&&TexturePixels.Int(sunshine,4)==2&&
              TexturePixels.Int(sunshine,8)==0&&TexturePixels.Width(sunshine)==128&&TexturePixels.Height(sunshine)==128&&
              TexturePixels.Int(sunshine,24)==SunshineWater.MipLevels&&
              sunshine.Length==32+SunshineWater.PayloadBytes,
              "Sunshine artist mips cook into the verified 128-square GX I4 DNTX v2 chain");
        int sunshineAt=32;int[] meanBrightness=new int[SunshineWater.MipLevels];
        for(int level=0;level<SunshineWater.MipLevels;level++){
            int edge=SunshineWater.Edge(level),sourceEdge=SunshineWater.SourceEdge(level);
            for(int by=0;by<edge;by+=8)for(int bx=0;bx<edge;bx+=8)
                for(int y=by;y<by+8;y++)for(int x=bx;x<bx+8;x+=2){
                    byte packed=sunshine[sunshineAt++];
                    for(int part=0;part<2;part++){
                        byte source=artistLevels[level][y/2*sourceEdge+(x+part)/2];
                        int lum=(part==0?packed>>4:packed&15)*17;
                        check(lum==((source+8)/17)*17&&lum==SunshineWater.AlphaForLuma(level,source),
                            "I4 8x8 tiles store high nibble first, expand each authored pixel 2x and use equal decoded intensity/alpha");
                        meanBrightness[level]+=lum;
                    }
                }
            meanBrightness[level]/=edge*edge;
        }
        check(sunshineAt==sunshine.Length&&SunshineWater.PayloadBytes==10912,"native I4 chain has exactly the required five GX tile levels");
        check(meanBrightness[0]==0&&meanBrightness[4]==0,
            "base and farthest Sunshine mip levels draw no specular reflection");
        check(meanBrightness[2]>meanBrightness[1]&&meanBrightness[1]>meanBrightness[3],
            "authored third mip creates a bright middle-distance reflection band");
        check(SunshineWater.LodBias==0&&SunshineWater.SecondaryLodBias==-.5f&&
              SunshineWater.GlintLowReference==35&&SunshineWater.GlintHighReference==235&&
              SunshineWater.NearLowReference==35&&SunshineWater.NearHighReference==85,
              "surface uses the source LOD biases and exclusive low35 threshold; near waves have distinct inclusive35/high85 references");
        var truncated=(byte[])sunshine.Clone();truncated[32+SunshineWater.PayloadBytes-1]++;
        bool rejected=false;try{SunshineWater.ValidatePackage(truncated);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"Sunshine texture validation rejects nonclear final authored levels");
        byte[] nearWave=SunshineWater.PackNearWave(artistLevels[1]),distortion=SunshineWater.PackDistortion(artistLevels[1]);
        SunshineWater.ValidateNearWavePackage(nearWave);SunshineWater.ValidateDistortionPackage(distortion);
        TexturePixels.Validate(nearWave);TexturePixels.Validate(distortion);
        check(TexturePixels.Width(nearWave)==128&&TexturePixels.Height(nearWave)==256&&TexturePixels.Int(nearWave,24)==5&&nearWave.Length==32+21824,
            "separate near-wave I4 package matches 128x256, five levels and 21824 native bytes");
        check(TexturePixels.Width(distortion)==64&&TexturePixels.Height(distortion)==64&&TexturePixels.Int(distortion,24)==4&&distortion.Length==32+2720,
            "separate displacement I4 package matches 64x64, four levels and 2720 native bytes");
        foreach(var package in new[]{nearWave,distortion}){
            int width=TexturePixels.Width(package),height=TexturePixels.Height(package);
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                int lum=(int)(TexturePixels.Pixel(package,x,y)>>24);
                byte source=artistLevels[1][y*32/height*32+x*32/width];
                check(lum==((source+8)/17)*17,"near-wave and displacement bases reuse our independent caustic art with exact nearest expansion");
            }
        }
        var checker=new byte[32*32];for(int y=0;y<32;y++)for(int x=0;x<32;x++)checker[y*32+x]=(byte)(((x+y)&1)*255);
        byte[] filteredDistortion=SunshineWater.PackDistortion(checker),filteredNear=SunshineWater.PackNearWave(checker);
        // The final displacement level averages 4x4 source samples; the near
        // level averages 4x2. Both must reduce the checker to intensity136.
        foreach(var package in new[]{filteredDistortion,filteredNear}){
            int lastBytes=ReferenceEquals(package,filteredDistortion)?32:64;
            for(int i=package.Length-lastBytes;i<package.Length;i++)
                check(package[i]==0x88,"ordinary mips of the two independent overlay textures filter source detail before I4 quantization");
        }
        check(SunshineWater.SurfacePhase(0)==0&&SunshineWater.SurfacePhase(40)==0&&
              Math.Abs(SunshineWater.SurfacePhase(10)-.203125f)<.0000001f&&SunshineWater.SurfacePhase(20)==.5f&&
              Math.Abs(SunshineWater.SurfacePhase(30)-.796875f)<.0000001f,
              "surface BTK reproduces the source Hermite tangents over a forty-second 30fps cycle");
        check(SunshineWater.IndirectPhase(10)==.75f&&SunshineWater.IndirectPhase(20)==1.5f&&SunshineWater.IndirectPhase(40)==0&&
              Math.Abs(SunshineWater.NearPhase(10)-.45f)<.0000001f,
              "refraction scrolls three repeats per forty seconds and near waves advance .0015 repeats per 30fps frame");
        foreach(double seconds in new[]{-3600.0,-1,1,3600,86400,1e8}){
            check(SunshineWater.SurfacePhase(seconds)>=0&&SunshineWater.SurfacePhase(seconds)<1&&
                  SunshineWater.IndirectPhase(seconds)>=0&&SunshineWater.IndirectPhase(seconds)<3&&
                  SunshineWater.NearPhase(seconds)>=0&&SunshineWater.NearPhase(seconds)<1,
                  "all source UV phases remain finite and bounded in long sessions or negative elapsed time");
            check(Math.Abs(SunshineWater.SurfacePhase(seconds+40)-SunshineWater.SurfacePhase(seconds))<.000001f,
                "surface animation repeats without changing the authored Hermite profile");
        }
        check(SunshineWater.Cells==16&&SunshineWater.VertexFade(8,8)==255&&SunshineWater.VertexFade(4,8)==127&&
              SunshineWater.VertexFade(4,4)==127&&SunshineWater.VertexFade(0,8)==0&&SunshineWater.VertexFade(17,8)==0,
              "near-wave vertex alpha fades linearly by max-axis distance around the player");
        foreach(double time in new[]{0.0,1,3600,86400,1e8}){
            float atEdge=SunshineWater.LocalWaveHeight(0,8,12,24,time);
            float center=SunshineWater.LocalWaveHeight(8,8,12,24,time);
            check(Math.Abs(atEdge-.047f)<.000001f&&center>.005f&&center<.09f,
                "Sunshine two-wave patch fades at its rim and stays above static sea geometry");
            check(Math.Abs(center-SunshineWater.LocalWaveHeight(8,8,12,24,time+.001))<.001f,
                "Sunshine nearby geometry has continuous two-direction motion");
        }
        foreach(float depth in new[]{SunshineWater.RiverBedDepth,SunshineWater.SeaBedDepth}){
            float scale=SunshineWater.BedScale(depth),offset=SunshineWater.BedAxisOffset(depth);
            check(scale>0&&scale<1&&float.IsFinite(offset),"seabed uses a small finite inward cylinder transform");
            for(int z=-50;z<=Town.Height+30;z+=5){
                var surface=CylinderWorld.Surface(z,0,WaterSurface.Height);
                var bottom=CylinderWorld.Surface(z,0,WaterSurface.Height-depth);
                float y=surface.Y*scale+offset,projectedZ=surface.Z*scale;
                check(Math.Abs(y-bottom.Y)<.0001f&&Math.Abs(projectedZ-bottom.Z)<.0001f,
                    "reused water mesh lies at constant radial bed depth even over the cylinder horizon");
            }
        }
        check(SunshineWater.SeaBedDepth>SunshineWater.RiverBedDepth,
            "sea floor is deeper than the inland water floor");
        foreach(float speed in new[]{.012f,.025f,-.018f,.035f}){
            check(WaterSurface.Scroll(0,speed)==0,"animation starts at authored UVs");
            foreach(double time in new[]{1.0,59,3600,86400,1e8}){
                float offset=WaterSurface.Scroll(time,speed);check(offset>=0&&offset<1,"long sessions and negative flow keep UV translations bounded");
                float delta=WaterSurface.Scroll(time+.01,speed)-offset;delta-=(float)Math.Round(delta);
                check(Math.Abs(delta-speed*.01)<.000002,"wrapped scrolling remains continuous modulo texture repeat");
            }
        }
        // All surfaces deliberately share a material slot. Water must still
        // split from land and cannot depend on floor metadata/patch tint.
        var data=new byte[FloorStrips.Records];void Put(int p,int value){for(int i=0;i<4;i++)data[p+i]=(byte)(value>>(i*8));}
        int[] header={0x32525441,1,13,1,FloorTexture.Maximum};for(int i=0;i<header.Length;i++)Put(i*4,header[i]);
        for(int s=0;s<13;s++){Put(20+s*20,1);for(int v=1;v<4;v++)Put(24+s*20+v*4,-1);}FloorStrips.Validate(data);
        state.X=state.Z=80;town.SetTerrain(80,80,Terrain.Grass);town.SetTerrain(81,80,Terrain.Bridge);
        var strips=new FloorStrips();strips.Prepare(state,0,data);int cells=0,land=0;
        for(int n=0;n<strips.Count;n++)for(int x=strips.X0[n];x<strips.X1[n];x++){
            check(strips.Water[n]==WaterSurface.IsWater(town,x,strips.Z0[n]),"opaque ground and water selection never overlap even with aliased material slots");
            if(strips.Water[n])cells++;else land++;
            check(strips.Elevation[n]==0,"water marker never leaks into elevation bits");
        }
        check(cells>100&&cells<=WaterSurface.MaximumCells&&land==2,"visible open sea stays bounded and leaves bridge/land surfaces intact");
        strips.Prepare(state,1,data);check(strips.Count==1&&!strips.Water[0],"interior floors never inherit outdoor water");
    }
}
