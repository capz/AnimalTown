using AnimalTown;
internal static class DevConfigChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var defaults=new DevConfig(7);check(defaults.RuntimeMode==7,"dev defaults preserve disc runtime profile");
        for(int row=0;row<DevConfig.Count;row++)if(DevConfig.IsSetting(row))defaults.Adjust(row,1);
        var encoded=defaults.Encode();var restored=DevConfig.Decode(encoded);
        check(defaults.Values.SequenceEqual(restored.Values),"all dev options survive their own save record");
        check(!SplitSaveCodec.IsFile("dev_config"),"dev config cannot be counted as a damaged town save");
        check(!DevConfig.IsSetting(14)&&!DevConfig.IsSetting(15),"teleport actions are not startup configuration");
        encoded[10]^=1;bool rejected=false;try{DevConfig.Decode(encoded);}catch(IOException){rejected=true;}check(rejected,"damaged dev config rejected before application");
        var sliders=new DevConfig();for(int i=0;i<100;i++){sliders.Adjust(20,-1);sliders.Adjust(23,1);}
        check(sliders.Values[20]==0&&sliders.Values[23]==10,"dev sliders remain bounded");
        sliders.Adjust(8,-1);check(sliders.Values[8]==1,"dev enums cycle in both directions");
        sliders.Values[20]=1;rejected=false;try{sliders.Encode();}catch(InvalidOperationException){rejected=true;}check(rejected,"invalid rim increments cannot create an un-restorable config");
        var original=DevConfig.Decode(Convert.FromBase64String("QVREAQEBAAABAQEBAgAAAAAAAAAAAAIAfQABBAAAAADlXYtQ"));
        check(original.Values[0]==1&&original.Values[20]==125&&original.Values[23]==4&&original.Values[18]==2,"v1 debug file keeps existing options during migration");
        check(original.TreeNoiseStrengthPercent==100&&original.TreeNoiseScalePercent==100,"v1 debug files adopt the current tree appearance");
        var noise=new DevConfig();check(noise.TreeNoiseStrengthPercent==100&&noise.TreeNoiseScalePercent==100,"new defaults match the original tree appearance");
        for(int i=0;i<100;i++){noise.Adjust(DevConfig.TreeNoiseStrength,-1);noise.Adjust(DevConfig.TreeNoiseScale,1);}
        check(noise.TreeNoiseStrengthPercent==0&&noise.TreeNoiseScalePercent==400,"tree strength and scale clamp rather than wrapping");
        for(int i=0;i<100;i++){noise.Adjust(DevConfig.TreeNoiseStrength,1);noise.Adjust(DevConfig.TreeNoiseScale,-1);}
        check(noise.TreeNoiseStrengthPercent==300&&noise.TreeNoiseScalePercent==25,"tree sliders cover their full supported ranges");
        var savedNoise=noise.Encode();check(savedNoise.Length==40&&savedNoise[3]==2,"extended debug settings have a versioned checksummed 40-byte record");
        var loadedNoise=DevConfig.Decode(savedNoise);check(loadedNoise.TreeNoiseStrengthPercent==300&&loadedNoise.TreeNoiseScalePercent==25,"both tree settings restore independently after a save");
        noise.Reset(DevConfig.TreeNoiseStrength);noise.Reset(DevConfig.TreeNoiseScale);check(noise.TreeNoiseStrengthPercent==100&&noise.TreeNoiseScalePercent==100,"Z/reset restores the original appearance");
        savedNoise[32]^=1;rejected=false;try{DevConfig.Decode(savedNoise);}catch(IOException){rejected=true;}check(rejected,"the new scale byte is protected by the checksum");
        noise.Values[DevConfig.TreeNoiseStrength]=31;rejected=false;try{noise.Encode();}catch(InvalidOperationException){rejected=true;}check(rejected,"unsupported strength cannot be saved");
        noise.Values[DevConfig.TreeNoiseStrength]=10;noise.Values[DevConfig.TreeNoiseScale]=16;rejected=false;try{noise.Encode();}catch(InvalidOperationException){rejected=true;}check(rejected,"unsupported scale cannot be saved");
        byte[] off=LowPolyTree.DetailTexture(0),normal=LowPolyTree.DetailTexture(),strong=LowPolyTree.DetailTexture(300);
        TexturePixels.Validate(off);TexturePixels.Validate(strong);
        check(off.Skip(32).All(v=>v==255),"zero strength is an identity modulation at every mip level");
        check(normal.Length==strong.Length&&strong.Length==1472&&TexturePixels.Int(strong,24)==6,"stronger noise keeps the same tiny native mip budget");
        long ordinary=0,pronounced=0;for(int y=0;y<32;y++)for(int x=0;x<32;x++){ordinary+=TexturePixels.Pixel(normal,x,y)>>24;pronounced+=TexturePixels.Pixel(strong,x,y)>>24;}
        check(pronounced<ordinary&&strong.Skip(32).Min()>0,"higher strength increases detail without clipping to black");
        check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(normal))=="68E634A7F48038AEA7F92E05FBAFEF4E4B62C09D058C5A29629A3CB9DFE43EE5","default tree detail stays byte-identical to the approved texture");
        rejected=false;try{LowPolyTree.DetailTexture(301);}catch(ArgumentOutOfRangeException){rejected=true;}check(rejected,"unsupported runtime noise strength is rejected");
        UniformUiGrain(check);
        rejected=false;try{DevConfig.Decode(new byte[8]);}catch(IOException){rejected=true;}check(rejected,"truncated dev config rejected");
    }
    private static void UniformUiGrain(Action<bool,string> check)
    {
        byte[] texture=LowPolyTree.DetailTexture(UiNoiseSettings.DefaultPercent,uniform:true);
        TexturePixels.Validate(texture);
        check(texture.Length==1472&&TexturePixels.Int(texture,24)==6&&TexturePixels.Int(texture,8)==1,"uniform UI grain retains the 1440-byte opaque I8 mip budget");
        check(texture.SequenceEqual(LowPolyTree.DetailTexture(UiNoiseSettings.DefaultPercent,uniform:true)),"UI grain remains fixed across preparations");
        var samples=new int[1024];var histogram=new int[34];double mean=0;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){
            uint pixel=TexturePixels.Pixel(texture,x,y);int dark=255-(int)(pixel>>24);
            check((pixel&255)==255&&dark>=0&&dark<=33,"default UI grain preserves alpha and the selected 13-percent maximum darkening");
            samples[y*32+x]=dark;histogram[dark]++;mean+=dark/1024.0;
        }
        double expected=1024.0/34,chiSquared=0,variance=0;
        foreach(int count in histogram)chiSquared+=(count-expected)*(count-expected)/expected;
        foreach(int value in samples)variance+=(value-mean)*(value-mean)/1024;
        check(histogram.All(count=>count>0)&&Math.Abs(mean-16.5)<1&&chiSquared<65,"UI texels fill the uniform intensity range without the old central concentration");
        // Include tile seams and the old coarse-grid spacing, not just a histogram:
        // spatially sorted uniform values would still produce visible clumps.
        foreach(var lag in new[]{(1,0),(0,1),(1,1),(8,0),(0,8)}){
            double covariance=0;
            for(int y=0;y<32;y++)for(int x=0;x<32;x++)covariance+=(samples[y*32+x]-mean)*(samples[((y+lag.Item2)%32)*32+(x+lag.Item1)%32]-mean)/1024;
            check(Math.Abs(covariance/variance)<.1,"uniform UI grain has no coherent neighbouring or coarse-grid mottling");
        }
        int offset=32;var previous=samples.Select(value=>255-value).ToArray();
        for(int edge=32;edge>=1;edge/=2){
            var decoded=new int[edge*edge];
            for(int y=0;y<edge;y++)for(int x=0;x<edge;x++)decoded[y*edge+x]=texture[offset+((y/4)*((edge+7)/8)+x/8)*32+y%4*8+x%8];
            check(decoded.SequenceEqual(previous),"every uniform UI mip averages its parent without changing GX tile addressing");
            offset+=(edge+7)/8*((edge+3)/4)*32;
            if(edge==1)break;
            int next=edge/2;previous=new int[next*next];
            for(int y=0;y<next;y++)for(int x=0;x<next;x++){int at=y*2*edge+x*2;previous[y*next+x]=(decoded[at]+decoded[at+1]+decoded[at+edge]+decoded[at+edge+1]+2)/4;}
        }
        check(offset==texture.Length,"uniform mip payload contains no missing or extra blocks");
        check(LowPolyTree.DetailTexture(0,uniform:true).Skip(32).All(value=>value==255),"disabled uniform grain is an identity at every mip");
        foreach(int strength in new[]{5,35,100,300}){
            byte[] level=LowPolyTree.DetailTexture(strength,uniform:true);TexturePixels.Validate(level);
            int darkest=255,brightest=0;
            for(int y=0;y<32;y++)for(int x=0;x<32;x++){int value=(int)(TexturePixels.Pixel(level,x,y)>>24);darkest=Math.Min(darkest,value);brightest=Math.Max(brightest,value);}
            check(brightest==255&&darkest==255-Math.Min(255,(95*strength+50)/100),"UI slider widens the uniform range without clipping or integer wrap");
        }
        foreach(int strength in new[]{-1,301}){
            bool rejected=false;try{LowPolyTree.DetailTexture(strength,uniform:true);}catch(ArgumentOutOfRangeException){rejected=true;}
            check(rejected,"uniform UI grain rejects unsupported strengths");
        }
    }
}
