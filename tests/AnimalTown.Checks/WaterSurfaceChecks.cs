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
        // Sunshine's 5 native IA8 levels intentionally have different RGB and
        // alpha content; a conventional downsampling mip generator would lose
        // the distance-dependent shimmering/reflection band.
        var sunshine=SunshineWater.TexturePackage();
        TexturePixels.Validate(sunshine);
        check(TexturePixels.Int(sunshine,0)==0x58544e44&&TexturePixels.Int(sunshine,4)==2&&
              TexturePixels.Int(sunshine,8)==3&&TexturePixels.Int(sunshine,24)==SunshineWater.MipLevels,
              "Sunshine builds GX IA8 DNTX v2 with explicit authored mip levels");
        check(TexturePixels.Int(sunshine,20)==SunshineWater.PayloadBytes&&
              sunshine.Length==SunshineWater.PayloadBytes+32,"Sunshine texture takes 10912 GX bytes across all mips");
        int sunshineAt=32;int[] meanBrightness=new int[SunshineWater.MipLevels],meanAlpha=new int[SunshineWater.MipLevels];
        for(int level=0;level<SunshineWater.MipLevels;level++){
            int edge=SunshineWater.TextureSize>>level;
            for(int by=0;by<edge;by+=4)for(int bx=0;bx<edge;bx+=4)
                for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){
                    int alpha=sunshine[sunshineAt++],brightness=sunshine[sunshineAt++];
                    check(alpha==SunshineWater.Alpha(level,x,y)&&brightness==SunshineWater.Brightness(level,x,y),
                        "Sunshine IA8 texels use native 4x4 GX tile order");
                    check(alpha==SunshineWater.Alpha(level,x+edge,y-edge)&&
                          brightness==SunshineWater.Brightness(level,x+edge,y-edge),
                        "Sunshine authored mip texels repeat seamlessly at every level");
                    meanBrightness[level]+=brightness;meanAlpha[level]+=alpha;
                }
            meanBrightness[level]/=edge*edge;meanAlpha[level]/=edge*edge;
        }
        check(sunshineAt==sunshine.Length,"mip chain is contiguous and contains no extra bytes");
        check(meanBrightness[0]<meanBrightness[1]&&meanBrightness[1]<meanBrightness[2]&&
              meanBrightness[2]>meanBrightness[3]&&meanBrightness[3]>meanBrightness[4],
              "Sunshine mip level 2 creates the bright reflection band instead of simple downsampling");
        check(meanAlpha[0]<meanAlpha[1]&&meanAlpha[1]<meanAlpha[2]&&
              meanAlpha[2]>meanAlpha[3]&&meanAlpha[3]>meanAlpha[4],
              "near water is clear, the reflection band less transparent, and distant water softer");
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
        // Separate authored texture on TEX1; the earlier single-image
        // implementation had no distinct reflection content at all.
        var glints=SunshineWater.GlintTexturePackage();
        TexturePixels.Validate(glints);
        check(glints.Length==sunshine.Length&&TexturePixels.Int(glints,24)==SunshineWater.MipLevels,
            "second Sunshine layer has a complete independent IA8 mip chain");
        check(!glints.SequenceEqual(sunshine),"Sunshine TEV texture units have different image data");
        int glintAt=32;int[] glintMean=new int[SunshineWater.MipLevels];
        for(int level=0;level<SunshineWater.MipLevels;level++){
            int size=SunshineWater.TextureSize>>level;
            for(int by=0;by<size;by+=4)for(int bx=0;bx<size;bx+=4)
                for(int y=by;y<by+4;y++)for(int x=bx;x<bx+4;x++){
                    byte alpha=glints[glintAt++],brightness=glints[glintAt++];
                    check(alpha==SunshineWater.GlintAlpha(level,x,y)&&
                          brightness==SunshineWater.GlintBrightness(level,x,y),
                        "independent glint IA8 texture uses GX 4x4 packing");
                    check(alpha==SunshineWater.GlintAlpha(level,x+size,y-size)&&
                          brightness==SunshineWater.GlintBrightness(level,x+size,y-size),
                        "second scrolling glint texture wraps in every mip");
                    glintMean[level]+=brightness;
                }
            glintMean[level]/=size*size;
        }
        check(glintAt==glints.Length&&glintMean[0]<glintMean[1]&&
              glintMean[1]<glintMean[2]&&glintMean[2]>glintMean[3],
            "secondary authored levels create a bright mid-distance reflection band");
        check(SunshineWater.RippleLodBias>=0&&SunshineWater.RippleLodBias<=3.99f&&
              SunshineWater.ReflectionLodBias>=0&&SunshineWater.ReflectionLodBias<=3.99f,
            "two texture samplers have valid GX LOD biases");
        int nearCrests=0,nearClears=0,mediumCrests=0,mediumClears=0;
        for(int level=0;level<3;level++){
            int size=SunshineWater.TextureSize>>level;
            for(int y=0;y<size;y++)for(int x=0;x<size;x++){
                // Native DNEF v2 uses primary TEX0 alpha for the overlay's
                // one-sided cutout; low alpha remains visible through the
                // independently translucent film below.
                bool passes=SunshineWater.GlintAlpha(level,x,y)/255f>SunshineWater.GlintAlphaCutoff;
                if(level==0){if(passes)nearCrests++;else nearClears++;}
                if(level==2){if(passes)mediumCrests++;else mediumClears++;}
            }
        }
        check(nearClears>2000&&nearCrests>150&&mediumCrests>12&&mediumClears>100,
            "GX glint cutoff preserves transparent film and strong near/mid-distance crest fragments");
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
