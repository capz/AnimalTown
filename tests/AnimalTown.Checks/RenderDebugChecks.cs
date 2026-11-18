using AnimalTown;

internal static class RenderDebugChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var preview=new RenderDebugOptions();var state=new GameState(73);var before=SaveCodec.Encode(state);
        GroundMipChecks(check,preview);
        GroundMipPaletteChecks(check);
        check(UiNoiseSettings.DefaultPercent==10&&RenderDebugOptions.DefaultRimPercent==100,"UI grain and villager rim strength are fixed");
        check(preview.SeasonOverride==-1&&!preview.ForceAttention,"render previews start automatic with attention off");
        for(int month=-24;month<36;month++){
            check(preview.EffectiveSeason(month)==TerrainTiles.Season(month),"automatic preview follows the existing calendar and negative-month wrapping");
            for(int season=0;season<4;season++){
                preview.CycleSeason();
                check(preview.SeasonOverride==season&&preview.EffectiveSeason(month)==season,"every forced season overrides the calendar independently");
                check(preview.EffectiveSeason(month,3-season)==season,"debug choice overrides a fixed build season too");
            }
            preview.CycleSeason();check(preview.SeasonOverride==-1,"cycling winter restores automatic season selection");
        }
        preview.ForceAttention=true;preview.CycleSeason();preview.GroundMipmaps=false;preview.AdjustGroundMipDistance(1);
        check(before.SequenceEqual(SaveCodec.Encode(state)),"render previews never change serialized player, calendar, requests or terrain state");
        check(RenderDebugOptions.AttentionHeight(1.7f)>RenderDebugOptions.AttentionHeight(1)&&RenderDebugOptions.AttentionHeight(float.NaN)==3.6f,"attention anchor follows rig height and has a finite missing-rig fallback");
        float previousRadius=360f/6.28318530718f;
        var p=CylinderWorld.Surface(12,0);
        check(Math.Abs(p.Pitch/(12/previousRadius)-1.08f)<.000001f,"world turns eight percent farther for the same logical arc distance");
        float previousY=previousRadius*((float)Math.Cos(12/previousRadius)-1);
        check(p.Y<previousY&&p.Z>0,"stronger curvature bends downward while preserving logical north/south orientation");
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int n=0;n<1000;n++){preview.CycleSeason();preview.EffectiveSeason(n);preview.AdjustGroundMipDistance(1);preview.ResetGroundMipDistance();RenderDebugOptions.GroundMipBias(256,256,n%11);}
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
            double expected=Math.Clamp(.5-Math.Log2(128/pixelsPerMetre),-4,3.99);
            check(Math.Abs(expected-bias)<.01,"the mip switch follows the projected cylinder within the GX bias range");
        }
        float auto=RenderDebugOptions.GroundMipBias(256,256,4);
        check(RenderDebugOptions.GroundMipBias(256,256,0)==auto&&RenderDebugOptions.GroundMipBias(256,256,int.MinValue)==auto&&RenderDebugOptions.GroundMipBias(256,256,int.MaxValue)==previous,"Auto and extreme reference inputs remain bounded");
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
        var resident=FloorTexture.Prepare(full);
        check(TexturePixels.Int(resident,24)==2&&resident.Length==32+40960,"only the full and half detail levels are resident");
        check(resident.Skip(32).SequenceEqual(full.Skip(32).Take(40960)),"the resident ground retains its exact cooked top and half-size pixels");
    }
    private static byte[] Package(byte[] payload,int w,int h,int format)
    {
        var result=new byte[32+payload.Length];int[] header={0x58544e44,1,format,w,h,payload.Length,0,0};
        for(int i=0;i<header.Length;i++)for(int b=0;b<4;b++)result[i*4+b]=(byte)(header[i]>>(b*8));Array.Copy(payload,0,result,32,payload.Length);return result;
    }
    private static void GroundMipPaletteChecks(Action<bool,string> check)
    {
        foreach(var shape in new[]{(256,256,2),(64,64,2),(128,64,2),(4,4,1),(256,256,1)}){
            var texture=GroundMipPalette.Texture(shape.Item1,shape.Item2,shape.Item3);
            TexturePixels.Validate(texture);
            check(TexturePixels.Width(texture)==shape.Item1&&TexturePixels.Height(texture)==shape.Item2&&TexturePixels.Int(texture,24)==shape.Item3,
                "mip bands preserve the real texture dimensions and mip count, including the preview and rectangular pages");
            uint hash=TexturePixels.Hash(texture);
            for(int level=0;level<shape.Item3;level++){
                var tail=FloorTexture.ExtractMipTail(texture,level);TexturePixels.Validate(tail);
                uint expected=GroundMipPalette.Color(level);
                bool solid=true;
                for(int y=0;y<TexturePixels.Height(tail);y++)for(int x=0;x<TexturePixels.Width(tail);x++)
                    solid&=TexturePixels.Pixel(tail,x,y)==expected;
                check(solid&&(expected&255)==255,"every diagnostic mip is one distinct opaque native colour, including GX tile edges");
                if(level>0)check(expected!=GroundMipPalette.Color(level-1),"adjacent mip levels remain distinguishable for trilinear transition bands");
            }
            check(TexturePixels.Hash(texture)==hash,"diagnostic inspection never mutates its source package");
        }
        check(GroundMipPalette.Texture(256,256,2).Length==32+40960&&GroundMipPalette.Texture(64,64,2).Length==32+2560,
            "two-level diagnostic chains retain the bounded CMPR full and preview budgets");
        foreach(var shape in new[]{(0,64,1),(2,64,1),(63,64,1),(512,64,1),(64,64,0),(64,64,6),(256,256,8)}){
            bool rejected=false;try{GroundMipPalette.Texture(shape.Item1,shape.Item2,shape.Item3);}catch(ArgumentOutOfRangeException){rejected=true;}
            check(rejected,"invalid diagnostic shapes cannot change the GX mip footprint or read beyond their chain");
        }
        foreach(int level in new[]{-1,GroundMipPalette.Levels}){
            bool rejected=false;try{GroundMipPalette.Color(level);}catch(ArgumentOutOfRangeException){rejected=true;}
            check(rejected,"legend requests remain within the native diagnostic palette");
        }
        GroundMipPalette.Color(0);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int n=0;n<1000;n++)GroundMipPalette.Color(n%GroundMipPalette.Levels);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"drawing the mip palette legend allocates no per-frame data");
    }
}
