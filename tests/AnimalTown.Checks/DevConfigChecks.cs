using AnimalTown;
internal static class DevConfigChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(new DevConfig().Values[DevConfig.FullResolutionAaIndex]==0,"full-resolution AA defaults off");
        var oldV3=new DevConfig();oldV3.Values[DevConfig.FullResolutionAaIndex]=0;
        byte[] v3=oldV3.Encode();v3[3]=3;uint checksum=2166136261;
        for(int i=0;i<36;i++)checksum=unchecked((checksum^v3[i])*16777619);
        for(int i=0;i<4;i++)v3[36+i]=(byte)(checksum>>(i*8));
        check(DevConfig.Decode(v3).Values[DevConfig.FullResolutionAaIndex]==0,"version-three settings migrate with AA off");
        var on=new DevConfig();on.Values[DevConfig.FullResolutionAaIndex]=1;
        check(DevConfig.Decode(on.Encode()).Values[DevConfig.FullResolutionAaIndex]==1,"AA preference survives restart without changing the record size");
        var defaults=new DevConfig(7);check(defaults.RuntimeMode==7,"dev defaults preserve disc runtime profile");
        check(defaults.Values[DevConfig.GroundMipBandsIndex]==0,"terrain mip bands are opt-in");
        check(defaults.Values[DevConfig.DiscardTerrainCacheWhenIndoorsIndex]==0,"indoor terrain cache discard is opt-in");
        // A record written before the terrain option existed. Keep accepting
        // the original checksum and preserve the previous startup options.
        var legacy=DevConfig.Decode(Convert.FromHexString("4154440100010000010101010200000000000000000000006400010000000000BFA20D87"));
        check(legacy.Values[1]==1&&legacy.Values[8]==2&&legacy.Values[20]==100&&legacy.Values[22]==1,"version-one dev config remains readable");
        check(legacy.Values[DevConfig.DiscardTerrainCacheWhenIndoorsIndex]==0,"legacy saves keep terrain cache retention enabled");
        var previous=DevConfig.Decode(Convert.FromHexString("4154440200010000010101010200000000000000000000006400010000000016070000003360CDB9"));
        check(previous.Values[DevConfig.TreeNoiseStrength]==22&&previous.Values[DevConfig.TreeNoiseScale]==7,"published version-two tree settings remain readable");
        check(previous.Values[DevConfig.DiscardTerrainCacheWhenIndoorsIndex]==0,"version-two files leave the new terrain option off");
        for(int row=0;row<DevConfig.Count;row++)if(DevConfig.IsSetting(row))defaults.Adjust(row,1);
        var encoded=defaults.Encode();var restored=DevConfig.Decode(encoded);
        check(encoded[3]==5&&restored.Values[DevConfig.DiscardTerrainCacheWhenIndoorsIndex]==1&&restored.Values[DevConfig.GroundMipBandsIndex]==1,"terrain debug toggles persist in version-five dev config");
        check(defaults.Values.SequenceEqual(restored.Values),"all dev options survive their own save record");
        check(defaults.SdkPerformanceOverlay&&!restored.SdkPerformanceOverlay&&encoded[4+DevConfig.SdkPerformanceIndex]==0,"SDK overlay never enters a newly saved record");
        check(!SplitSaveCodec.IsFile("dev_config"),"dev config cannot be counted as a damaged town save");
        check(!DevConfig.IsSetting(14)&&!DevConfig.IsSetting(15),"teleport actions are not startup configuration");
        check(Enumerable.Range(0,DevConfig.Count).Count(DevConfig.IsSetting)==DevConfig.SettingCount,"title debug rows contain every remaining setting exactly once");
        encoded[10]^=1;bool rejected=false;try{DevConfig.Decode(encoded);}catch(IOException){rejected=true;}check(rejected,"damaged dev config rejected before application");
        var sliders=new DevConfig();for(int i=0;i<100;i++){sliders.Adjust(20,-1);sliders.Adjust(23,1);}
        check(sliders.Values[20]==100&&sliders.Values[23]==RenderDebugOptions.MaximumGroundMipDistance,"dev sliders remain bounded");
        sliders.Adjust(8,-1);sliders.Adjust(7,-1);check(sliders.Values[8]==2&&sliders.Values[7]==1&&!DevConfig.IsSetting(7)&&!DevConfig.IsSetting(8),"retired shadow toggles cannot be selected or adjusted");
        sliders.Values[20]=1;rejected=false;try{sliders.Encode();}catch(InvalidOperationException){rejected=true;}check(rejected,"invalid rim increments cannot create an un-restorable config");
        var original=DevConfig.Decode(Convert.FromBase64String("QVREAQEBAAABAQEBAgAAAAAAAAAAAAIAfQABBAAAAADlXYtQ"));
        check(original.Values[0]==1&&original.Values[20]==125&&original.Values[23]==4&&original.Values[18]==2,"v1 debug file keeps existing options during migration");
        var fixedDefaults=new DevConfig();
        check(fixedDefaults.RuntimeMode==4,"productive waits default off and trust frame clear defaults on");
        foreach(int option in new[]{1,4,5,6,9,10,14,15,20,27,28}){
            int beforeOption=fixedDefaults.Values[option];
            fixedDefaults.Adjust(option,1);fixedDefaults.Reset(option);
            check(!DevConfig.IsSetting(option)&&fixedDefaults.Values[option]==beforeOption,"retired settings cannot be selected or changed");
        }
        byte[] off=LowPolyTree.DetailTexture(0),normal=LowPolyTree.DetailTexture(),strong=LowPolyTree.DetailTexture(300);
        TexturePixels.Validate(off);TexturePixels.Validate(strong);
        check(off.Skip(32).All(v=>v==255),"zero strength is an identity modulation at every mip level");
        check(normal.Length==strong.Length&&strong.Length==1472&&TexturePixels.Int(strong,24)==6,"stronger noise keeps the same tiny native mip budget");
        long ordinary=0,pronounced=0;for(int y=0;y<32;y++)for(int x=0;x<32;x++){ordinary+=TexturePixels.Pixel(normal,x,y)>>24;pronounced+=TexturePixels.Pixel(strong,x,y)>>24;}
        check(pronounced<ordinary&&strong.Skip(32).Min()>0,"higher strength increases detail without clipping to black");
        check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(normal))=="68E634A7F48038AEA7F92E05FBAFEF4E4B62C09D058C5A29629A3CB9DFE43EE5","default tree detail stays byte-identical to the approved texture");
        rejected=false;try{LowPolyTree.DetailTexture(301);}catch(ArgumentOutOfRangeException){rejected=true;}check(rejected,"unsupported runtime noise strength is rejected");
        UniformUiGrain(check);
        // Valid checksums cannot turn reserved legacy bytes into new settings.
        void Rehash(byte[] data,int end){uint h=2166136261;for(int i=0;i<end;i++)h=unchecked((h^data[i])*16777619);for(int i=0;i<4;i++)data[end+i]=(byte)(h>>(i*8));}
        var sdkOn=new DevConfig().Encode();sdkOn[4+DevConfig.SdkPerformanceIndex]=1;Rehash(sdkOn,36);
        check(!DevConfig.Decode(sdkOn).SdkPerformanceOverlay,"old cards cannot enable the SDK overlay at process start");
        var reserved=Convert.FromHexString("4154440200010000010101010200000000000000000000006400010000000016070000003360CDB9");reserved[33]=1;Rehash(reserved,36);
        rejected=false;try{DevConfig.Decode(reserved);}catch(IOException){rejected=true;}check(rejected,"v2 padding cannot enable the new terrain option");
        var current=new DevConfig().Encode();current[3]=3;current[34]=1;Rehash(current,36);
        rejected=false;try{DevConfig.Decode(current);}catch(IOException){rejected=true;}check(rejected,"v3 retains strict zero padding");
        var v4=new DevConfig().Encode();v4[3]=4;v4[35]=1;Rehash(v4,36);
        rejected=false;try{DevConfig.Decode(v4);}catch(IOException){rejected=true;}check(rejected,"v4 retains strict zero padding after the AA byte");
        v4[35]=0;v4[34]=1;Rehash(v4,36);
        var fromV4=DevConfig.Decode(v4);
        check(fromV4.Values[DevConfig.FullResolutionAaIndex]==1&&fromV4.Values[DevConfig.GroundMipBandsIndex]==0,"v4 preserves AA and defaults the new mip diagnostic off");
        v4[35]=0;v4[34]=2;Rehash(v4,36);
        rejected=false;try{DevConfig.Decode(v4);}catch(IOException){rejected=true;}check(rejected,"AA setting rejects unsupported modes even with a valid checksum");
        var v5=new DevConfig().Encode();v5[35]=2;Rehash(v5,36);
        rejected=false;try{DevConfig.Decode(v5);}catch(IOException){rejected=true;}check(rejected,"mip band toggle rejects unsupported modes with a valid checksum");
        rejected=false;try{DevConfig.Decode(new byte[8]);}catch(IOException){rejected=true;}check(rejected,"truncated dev config rejected");
    }
    private static void UniformUiGrain(Action<bool,string> check)
    {
        byte[] texture=LowPolyTree.DetailTexture(UiNoiseSettings.DefaultPercent,uniform:true);
        TexturePixels.Validate(texture);
        check(texture.Length==1472&&TexturePixels.Int(texture,24)==6&&TexturePixels.Int(texture,8)==1,"uniform UI grain retains the 1440-byte opaque I8 mip budget");
        check(texture.SequenceEqual(LowPolyTree.DetailTexture(UiNoiseSettings.DefaultPercent,uniform:true)),"UI grain remains fixed across preparations");
        var samples=new int[1024];var histogram=new int[11];double mean=0;
        for(int y=0;y<32;y++)for(int x=0;x<32;x++){
            uint pixel=TexturePixels.Pixel(texture,x,y);int dark=255-(int)(pixel>>24);
            check((pixel&255)==255&&dark>=0&&dark<=10,"ten-percent UI grain preserves alpha and limits darkening to ten levels");
            samples[y*32+x]=dark;histogram[dark]++;mean+=dark/1024.0;
        }
        double expected=1024.0/11,chiSquared=0,variance=0;
        foreach(int count in histogram)chiSquared+=(count-expected)*(count-expected)/expected;
        foreach(int value in samples)variance+=(value-mean)*(value-mean)/1024;
        check(histogram.All(count=>count>0)&&Math.Abs(mean-5)<.5&&chiSquared<35,"UI texels fill the uniform intensity range without the old central concentration");
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
        foreach(int strength in new[]{5,30,35,100,300}){
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
