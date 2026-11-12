using System;
using System.IO;
using AnimalTown;
internal static class WaterEffectChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var bed=WaterMaterial.CreateBedMaterial();
        check(bed.Texture==null&&bed.SecondaryTexture==null,"underwater bed binds no sand or other texture");
        check(bed.Color.R==18&&bed.Color.G==48&&bed.Color.B==82&&bed.Color.A==255,
            "underwater bed has a uniform opaque dark-blue tint");
        check(bed.Blend==Dolphin.Graphics.BlendMode.Opaque&&bed.DepthWrite&&bed.DepthTest&&bed.Lighting==Dolphin.Graphics.LightingMode.Unlit,
            "solid bed writes depth without lighting or translucency changing its tint");
        var indirect=WaterMaterial.WindWakerDefinition().GetPackage();
        check(indirect.Length==208&&indirect[0]=='D'&&indirect[1]=='N'&&indirect[2]=='E'&&indirect[3]=='F',
            "water program has bounded native DNEF envelope");
        check(indirect[4]==3&&indirect[8]==4&&indirect[12]==3&&indirect[16]==2&&indirect[20]==76,
            "v3 program supplies two bound textures, two colors and one warp stage");
        check(indirect[176+15]==0&&indirect[176+23]==2&&indirect[176+31]==0,
            "second foam sample uses alternate UV while epilogue returns to base alpha");
        check(indirect[64]==196&&indirect[65]==228&&indirect[66]==222&&indirect[100]==229,
            "water pattern colors are independent configurable TEV constants");
        var sunshine=WaterMaterial.SunshineGlintDefinition().GetPackage();
        check(sunshine.Length==208&&sunshine[4]==2&&sunshine[8]==4&&
              sunshine[12]==3&&sunshine[16]==2,
            "Sunshine uses native DNEF v2 two-texture RGB interference");
        check(sunshine[176+8+7]==0&&sunshine[176+16+7]==1&&
              sunshine[176+24+7]==0,
            "second scrolling reflection sample is TEX1, with TEX0 alpha restored");
        check(SunshineWater.GlintAlphaCutoff>.4f&&SunshineWater.GlintAlphaCutoff<.7f,
            "Sunshine reflection cutoff matches its authored primary glint alpha range");
        CheckIdleInteraction(check);
    }

    private static void CheckIdleInteraction(Action<bool,string> check)
    {
        // Source-level contract for the native TownGame handler. The full
        // GameCube assembly is independently compiled by the same CI workflow.
        DirectoryInfo? dir=new DirectoryInfo(AppContext.BaseDirectory);
        const string filename="game/AnimalTown.GameCube/Program.cs";
        while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,filename)))dir=dir.Parent;
        if(dir==null)throw new Exception("Could not locate TownGame interaction source");
        string source=File.ReadAllText(Path.Combine(dir.FullName,filename));
        int start=source.IndexOf("private void Interact()",StringComparison.Ordinal);
        int end=source.IndexOf("internal void Step(",start,StringComparison.Ordinal);
        check(start>=0&&end>start,"GameCube player interaction handler is still available");
        string action=source.Substring(start,end-start);
        check(!action.Contains("A quiet moment")&&!action.Contains("A at the doorway to leave"),
            "empty-space A press has no default outdoor or indoor chat bubble");
        check(action.Contains("if(tick%3==0&&state.Receive(15))Say")&&
              !action.Contains("No butterfly in reach"),
            "bug-net misses are silent without removing successful catches");
        check(action.Contains("Say(v.Name,state.Talk(i))")&&
              action.Contains("Say(\"Backpack\",\"Picked up!\")"),
            "genuine villager conversation and item-pickup feedback remain enabled");
    }
}
