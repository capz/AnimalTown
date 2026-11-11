internal static class WaterEffectChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var indirect=WaterMaterial.WindWakerDefinition().GetPackage();
        check(indirect.Length==208&&indirect[0]=='D'&&indirect[1]=='N'&&indirect[2]=='E'&&indirect[3]=='F',
            "water program has bounded native DNEF envelope");
        check(indirect[4]==3&&indirect[8]==4&&indirect[12]==3&&indirect[16]==2&&indirect[20]==76,
            "v3 program supplies two bound textures, two colors and one warp stage");
        check(indirect[176+15]==0&&indirect[176+23]==2&&indirect[176+31]==0,
            "second foam sample uses alternate UV while epilogue returns to base alpha");
        check(indirect[64]==196&&indirect[65]==228&&indirect[66]==222&&indirect[100]==229,
            "water pattern colors are independent configurable TEV constants");
    }
}
