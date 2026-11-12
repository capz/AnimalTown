using System.IO;
using AnimalTown;
using Dolphin.Graphics;

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
        var glints=WaterMaterial.SunshineDefinition();var program=glints.GetPackage();
        check(program.Length==224&&program[4]==4&&program[8]==3&&program[12]==3,
            "Sunshine uses the SDK's three-stage independent-alpha program without a legacy opacity epilogue");
        check(program[21]==1&&program[24]==(byte)AlphaComparison.LessOrEqual&&
            program[25]==SunshineWater.GlintLowReference&&program[26]==(byte)AlphaCompareOperation.Or&&
            program[27]==(byte)AlphaComparison.GreaterOrEqual&&program[28]==SunshineWater.GlintHighReference,
            "Sunshine GX cutout keeps both outside-band branches with exact references");
        for(int primary=0;primary<256;primary++)for(int secondary=0;secondary<256;secondary++){
            int alpha=Alpha(program,primary,secondary);
            check(alpha==(primary+secondary)/2,"Both scrolling samples contribute to the SDK water opacity");
            bool pass=alpha<=program[25]||alpha>=program[28];
            check(pass==(alpha<=33||alpha>=235),"Cutout classifies the complete alpha-pair domain");
        }
        check(Alpha(program,0,0)==0&&Alpha(program,255,255)==255&&Alpha(program,0,255)==127,
            "Clear endpoints stay invisible, strong glints survive and conflicting samples reject");
        foreach(bool close in new[]{false,true}){
            var material=new Material();WaterMaterial.ConfigureSunshine(material,close);
            check(material.Color.A==255&&material.AlphaCutoff==0,
                "Main and near waves leave raster alpha opaque for the effect's high comparison");
            check(material.Blend==BlendMode.Alpha&&material.DepthTest&&!material.DepthWrite&&material.Cull==CullMode.Back,
                "Water blends over the seabed with depth testing and no depth writes");
            foreach(var sampler in new[]{material.Sampler,material.SecondarySampler}){
                check(sampler.U==TextureAddressMode.Repeat&&sampler.V==TextureAddressMode.Repeat&&
                    sampler.MagFilter==TextureMagFilter.Linear,"Both scrolling texture bindings use repeated filtered authored art");
                check(close?sampler.MinLod==1&&sampler.MaxLod==1&&sampler.MinFilter==TextureMinFilter.LinearMipmapNearest:
                    sampler.MinLod==0&&sampler.MaxLod==4&&sampler.MinFilter==TextureMinFilter.LinearMipmapLinear&&sampler.LodBias==SunshineWater.LodBias,
                    "Near waves force authored L1 while the main surface traverses the full trilinear chain");
            }
        }

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

    // Interpret serialized alpha records rather than the game/preset source.
    // The preset uses raster alpha 255; each stage shares its RGB texture order.
    private static int Alpha(byte[] package,int primary,int secondary)
    {
        var registers=new int[4];
        for(int stage=0;stage<package[8];stage++){
            int rgb=176+stage*16,at=rgb+8,control=package[at+5];
            if((control&128)==0)throw new Exception("Legacy hard-wired alpha in Sunshine material");
            int Read(int selector)=>selector switch {
                <=3=>registers[selector],4=>package[rgb+7]==1?secondary:primary,
                5 or 6=>255,7=>0,_=>throw new Exception("Unexpected alpha selector")
            };
            int a=Read(package[at]),b=Read(package[at+1]),c=Read(package[at+2]),d=Read(package[at+3]);
            int value=d+a+((b-a)*(c+(c>>7))>>8);
            if(((control>>4)&3)==3)value>>=1;
            registers[control&3]=Math.Clamp(value,0,255);
        }
        return registers[0];
    }

}
