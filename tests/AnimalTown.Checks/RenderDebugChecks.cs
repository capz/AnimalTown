using AnimalTown;

internal static class RenderDebugChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var preview=new RenderDebugOptions();var state=new GameState(73);var before=SaveCodec.Encode(state);
        GroundMipChecks(check,preview);
        var config=new DevConfig();var configBefore=config.Encode();
        UiNoiseSettings.Reset();check(UiNoiseSettings.Percent==30,"UI grain starts with restrained session-only intensity");
        for(int n=0;n<100;n++)UiNoiseSettings.Adjust(-1);
        check(UiNoiseSettings.Percent==0,"UI grain can be disabled without wrapping");
        for(int n=0;n<100;n++)UiNoiseSettings.Adjust(1);
        check(UiNoiseSettings.Percent==300,"UI grain intensity remains bounded at 300 percent");
        check(configBefore.SequenceEqual(config.Encode())&&before.SequenceEqual(SaveCodec.Encode(state)),"UI grain intensity changes neither debug settings nor town save bytes");
        UiNoiseSettings.Reset();check(UiNoiseSettings.Percent==30,"Z/reset returns UI grain to its default");
        check(preview.WaterStyle==WaterRenderStyle.WindWaker,
            "render debug defaults to the original Wind Waker path");
        preview.CycleWaterStyle();
        check(preview.WaterStyle==WaterRenderStyle.Sunshine,
            "water debug selector enables Sunshine without a terrain rebuild");
        preview.CycleWaterStyle();
        check(preview.WaterStyle==WaterRenderStyle.WindWaker,
            "water debug selector returns to Wind Waker without changing saves");
        preview.CycleWaterStyle();preview.ResetWaterStyle();
        check(preview.WaterStyle==WaterRenderStyle.WindWaker,
            "debug reset restores original water rendering");
        check(preview.SeasonOverride==-1&&!preview.ForceAttention&&preview.RimPercent==100,"render previews start automatic, with attention off and original rim strength");
        for(int month=-24;month<36;month++){
            check(preview.EffectiveSeason(month)==TerrainTiles.Season(month),"automatic preview follows the existing calendar and negative-month wrapping");
            for(int season=0;season<4;season++){
                preview.CycleSeason();
                check(preview.SeasonOverride==season&&preview.EffectiveSeason(month)==season,"every forced season overrides the calendar independently");
                check(preview.EffectiveSeason(month,3-season)==season,"debug choice overrides a fixed build season too");
            }
            preview.CycleSeason();check(preview.SeasonOverride==-1,"cycling winter restores automatic season selection");
        }
        for(int n=0;n<100;n++)preview.AdjustRim(-1);
        check(preview.RimPercent==0,"repeated decreases clamp at zero rather than wrapping");
        for(int n=0;n<40;n++){preview.AdjustRim(1);check(preview.RimPercent==(n+1)*5,"rim intensity supports precise five-percent steps");}
        preview.AdjustRim(1);check(preview.RimPercent==200,"maximum intensity remains bounded");
        preview.ResetRim();check(preview.RimPercent==100,"reset restores the original TEV contribution");
        preview.ForceAttention=true;preview.CycleSeason();preview.AdjustRim(1);preview.GroundMipmaps=false;preview.AdjustGroundMipDistance(1);
        check(before.SequenceEqual(SaveCodec.Encode(state)),"render previews never change serialized player, calendar, requests or terrain state");
        check(RenderDebugOptions.AttentionHeight(1.7f)>RenderDebugOptions.AttentionHeight(1)&&RenderDebugOptions.AttentionHeight(float.NaN)==3.6f,"attention anchor follows rig height and has a finite missing-rig fallback");
        float previousRadius=360f/6.28318530718f;
        var p=CylinderWorld.Surface(12,0);
        check(Math.Abs(p.Pitch/(12/previousRadius)-1.08f)<.000001f,"world turns eight percent farther for the same logical arc distance");
        float previousY=previousRadius*((float)Math.Cos(12/previousRadius)-1);
        check(p.Y<previousY&&p.Z>0,"stronger curvature bends downward while preserving logical north/south orientation");
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int n=0;n<1000;n++){preview.AdjustRim(1);preview.AdjustRim(-1);preview.CycleSeason();preview.EffectiveSeason(n);preview.AdjustGroundMipDistance(1);preview.ResetGroundMipDistance();preview.CycleWaterStyle();preview.ResetWaterStyle();RenderDebugOptions.GroundMipBias(256,256,n%11);}
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"preview adjustments allocate no managed frame data");
    }
    private static void GroundMipChecks(Action<bool,string> check,RenderDebugOptions preview)
    {
        check(preview.GroundMipmaps&&preview.GroundMipDistance==0,"ground mipmaps default to enabled with automatic footprint selection");
        for(int n=0;n<100;n++)preview.AdjustGroundMipDistance(-1);
        check(preview.GroundMipDistance==0,"ground mip distance cannot become negative");
        for(int n=0;n<100;n++)preview.AdjustGroundMipDistance(1);
        check(preview.GroundMipDistance==10,"reference distance clamps before the grazing horizon and GX bias limit");
        preview.ResetGroundMipDistance();check(preview.GroundMipDistance==0,"ground mip distance reset returns to Auto");
        float previous=1;
        for(int d=1;d<=10;d++){
            float bias=RenderDebugOptions.GroundMipBias(256,256,d);
            check(float.IsFinite(bias)&&bias>=-4&&bias<=3.99f&&bias<previous,"farther mip references postpone detail reduction with valid monotonic GX bias");previous=bias;
            // Independent finite difference of vertical screen projection tests
            // the centreline calibration, rather than repeating its Jacobian.
            double Project(float z){var p=CylinderWorld.Surface(z,0);double dy=p.Y-CylinderWorld.CameraHeight,dz=p.Z-CylinderWorld.CameraBack;return 240/RenderView.TanY*(dy*-RenderView.ForwardZ+dz*RenderView.ForwardY)/(dy*RenderView.ForwardY+dz*RenderView.ForwardZ);}
            double pixelsPerMetre=Math.Abs((Project(-d+.01f)-Project(-d-.01f))/.02);
            check(Math.Abs(Math.Log2(128/pixelsPerMetre)+bias-1)<.01,"mip 1 reference follows the projected cylinder at the selected ground-square distance");
        }
        check(RenderDebugOptions.GroundMipBias(256,256,0)==0&&RenderDebugOptions.GroundMipBias(256,256,int.MinValue)==0&&RenderDebugOptions.GroundMipBias(256,256,int.MaxValue)==previous,"Auto and extreme reference inputs remain bounded");
        var rgba=new byte[256*256*4];for(int i=0;i<256*256;i++){byte v=(byte)(i%2*255);rgba[i*4]=rgba[i*4+1]=rgba[i*4+2]=v;rgba[i*4+3]=255;}
        var source=Package(TexturePixels.Encode(rgba,256,256,4),256,256,4);uint hash=TexturePixels.Hash(source);
        var full=FloorTexture.Prepare(source,fullMipChain:true);
        check(TexturePixels.Int(full,24)==7&&full.Length==32+43712,"256px CMPR ground has a complete seven-level chain within 43 KiB");
        check(TexturePixels.Hash(source)==hash,"ground mip creation preserves the source bytes");
        int offset=32;
        for(int size=256,level=0;level<7;level++,size/=2){
            int bytes=(size+7)/8*((size+7)/8)*32;var payload=new byte[bytes];Array.Copy(full,offset,payload,0,bytes);offset+=bytes;
            var mip=Package(payload,size,size,14);TexturePixels.Validate(mip);uint color=TexturePixels.Pixel(mip,0,0);
            check((color&255)==255,"every mip including padded 4px CMPR blocks remains opaque");
            if(level>0)check((color>>24)>=115&&(color>>24)<=135,"box-filtered mips average alternating black/white texels instead of aliasing");
        }
        check(offset==full.Length,"full mip payload has no truncated or extra blocks");
        var narrow=FloorTexture.Prepare(Package(TexturePixels.Encode(rgba,256,64,4),256,64,4),fullMipChain:true);
        check(TexturePixels.Int(narrow,24)==5,"rectangular ground chain stops when its shorter axis reaches four pixels");
        var tiny=new byte[4*4*4];for(int i=3;i<tiny.Length;i+=4)tiny[i]=255;
        check(TexturePixels.Int(FloorTexture.Prepare(Package(TexturePixels.Encode(tiny,4,4,4),4,4,4),fullMipChain:true),24)==1,"minimum ground texture retains a single valid level");
        check(TexturePixels.Int(FloorTexture.Prepare(source),24)==2,"soil and other existing two-level callers retain their policy");
    }
    private static byte[] Package(byte[] payload,int w,int h,int format)
    {
        var result=new byte[32+payload.Length];int[] header={0x58544e44,1,format,w,h,payload.Length,0,0};
        for(int i=0;i<header.Length;i++)for(int b=0;b<4;b++)result[i*4+b]=(byte)(header[i]>>(b*8));Array.Copy(payload,0,result,32,payload.Length);return result;
    }
}
