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
        rejected=false;try{DevConfig.Decode(new byte[8]);}catch(IOException){rejected=true;}check(rejected,"truncated dev config rejected");
    }
}
