using AnimalTown;
using Dolphin.Graphics;

internal static class ShallowWaterMaterialChecks
{
    internal static void Run(Action<bool,string> check)
    {
        byte[] water=WaterMaterial.ShallowDefinition().GetPackage();
        check(water.Length==268&&water[4]==5&&water[8]==4&&water[12]==3&&water[16]==2,
            "shallow water uses four explicit stages and two colors, leaving native-footer resources free");
        check(water[20]==24&&water[23]==1&&water[31]==(byte)TevIndirectBias.TU,
            "shallow water declares restrained custom indirect warp with signed blue/green axes");
        int tail=176+water[8]*16;
        check(ReadFloat(water,tail)==0&&ReadFloat(water,tail+4)==.035f&&ReadFloat(water,tail+8)==0&&
              ReadFloat(water,tail+12)==0&&ReadFloat(water,tail+16)==0&&ReadFloat(water,tail+20)==.035f&&water[tail+24]==255,
            "indirect matrix ignores RGB565's constant alpha and uses both varying channels");
        for(int a=72;a<=144;a+=36)for(int first=0;first<=255;first+=51)for(int second=0;second<=255;second+=51){
            int[] output=Evaluate(water,new[]{99,181,196,a},Gray(first),Gray(second));
            check(output[3]==a,"ripple intensity never changes inland water opacity");
        }
        int[] clear=Evaluate(water,new[]{99,181,196,72},Gray(0),Gray(0));
        int[] dark=Evaluate(water,new[]{99,181,196,72},Gray(255),Gray(0));
        int[] glint=Evaluate(water,new[]{99,181,196,72},Gray(255),Gray(255));
        check(clear.SequenceEqual(new[]{99,181,196,72})&&dark[0]<clear[0]&&dark[1]<clear[1]&&dark[2]<clear[2],
            "a quiet blue surface receives a restrained darker ripple underneath");
        check(glint.SequenceEqual(new[]{211,242,236,72}),"the other UV sample supplies pale cyan glints while preserving transparency");
        check(ShallowWaterPattern.Opacity(-1)==72&&ShallowWaterPattern.Opacity(.5f)==108&&
              ShallowWaterPattern.Opacity(2)==144,"inland depth tint remains translucent and clamps outside its supported range");

        byte[] bed=WaterMaterial.BedDefinition().GetPackage(),rock=WaterMaterial.RockDefinition().GetPackage();
        check(bed[8]==3&&bed[12]==3&&rock[8]==4&&rock[12]==1,"bed and rocks stay within small independent TEV budgets");
        int[] bedDark=Evaluate(bed,new[]{210,210,210,255},new[]{178,180,158,255},Gray(0));
        int[] bedLight=Evaluate(bed,new[]{210,210,210,255},new[]{178,180,158,255},Gray(255));
        check(bedLight[0]>bedDark[0]&&bedLight[1]>bedDark[1]&&bedLight[2]>bedDark[2]&&bedLight[3]==255,
            "caustics brighten the opaque textured bed without changing its opacity");
        int[] dry=Evaluate(rock,new[]{145,157,151,0},Gray(255),Gray(0));
        int[] wet=Evaluate(rock,new[]{145,157,151,255},Gray(255),Gray(0));
        int[] fading=Evaluate(rock,new[]{145,157,151,128},Gray(255),Gray(0));
        check(dry.SequenceEqual(new[]{145,157,151,255})&&wet[0]>fading[0]&&fading[0]>dry[0]&&wet[3]==255&&fading[3]==255,
            "rock alpha fades underwater caustics while every rock pixel stays opaque");
        for(int shade=0;shade<=255;shade+=51)foreach(int intensity in new[]{0,128,255}){
            int[] unlitRim=Evaluate(rock,new[]{shade,shade,shade,1},Gray(intensity),Gray(0));
            check(unlitRim.SequenceEqual(new[]{shade,shade,shade,255}),
                "alpha-one wet capture coverage rounds to zero caustic light and preserves ordinary rock opacity");
        }
        int[] black=Evaluate(bed,new[]{0,0,0,255},new[]{178,180,158,255},Gray(255));
        check(black.SequenceEqual(new[]{0,0,0,255}),"caustics follow receiver shading rather than emitting through black tint");
        CheckPatterns(check);
    }

    private static void CheckPatterns(Action<bool,string> check)
    {
        byte[] ripples=ShallowWaterPattern.RippleTexture(),caustics=ShallowWaterPattern.CausticTexture(),
            distortion=ShallowWaterPattern.DistortionTexture(),bed=ShallowWaterPattern.BedTexture();
        check(ripples.Length==4096&&caustics.Length==4096&&distortion.Length==8192&&bed.Length==32768,
            "procedural fallbacks have bounded, format-correct native payload sizes");
        int visible=0,bright=0,light=0,minBlue=31,maxBlue=0,minGreen=63,maxGreen=0;
        for(int y=0;y<64;y++)for(int x=0;x<64;x++){
            int at=((y/4)*8+x/8)*32+(y%4)*8+x%8;
            byte ripple=ShallowWaterPattern.RippleSample(x,y),caustic=ShallowWaterPattern.CausticSample(x,y);
            check(ripples[at]==ripple&&caustics[at]==caustic,"I8 fallback texels retain GX 8x4 tile order");
            check(ripple==ShallowWaterPattern.RippleSample(x-64,y+64)&&caustic==ShallowWaterPattern.CausticSample(x+64,y-64),
                "ripple and caustic maps wrap exactly in either direction");
            if(ripple>8)visible++;if(ripple>128)bright++;if(caustic>64)light++;
            ushort warp=ShallowWaterPattern.DistortionSample(x,y);
            check(ReadRgb565(distortion,64,x,y)==warp&&warp==ShallowWaterPattern.DistortionSample(x-64,y+64),
                "distortion is periodic big-endian GX RGB565");
            int blue=warp&31,green=(warp>>5)&63;
            minBlue=Math.Min(minBlue,blue);maxBlue=Math.Max(maxBlue,blue);minGreen=Math.Min(minGreen,green);maxGreen=Math.Max(maxGreen,green);
            check((warp>>11)==16,"the unused distortion red channel remains neutral");
        }
        check(visible>100&&visible<800&&bright>12&&bright<visible/2,"broken surface glints leave broad quiet water between them");
        check(light>80&&light<1600,"soft caustics occupy only part of the floor");
        check(minBlue<16&&maxBlue>16&&minGreen<32&&maxGreen>32&&maxBlue-minBlue<=10&&maxGreen-minGreen<=20,
            "both sampled indirect axes vary positively and negatively within a restrained range");
        for(int y=0;y<128;y++)for(int x=0;x<128;x++){
            ushort sample=ShallowWaterPattern.BedSample(x,y);
            check(ReadRgb565(bed,128,x,y)==sample&&sample==ShallowWaterPattern.BedSample(x+128,y-128),
                "the stationary pebble bed is seamless in native 4x4 tile order");
        }
    }

    private static ushort ReadRgb565(byte[] texture,int size,int x,int y)
    {
        int at=(((y/4)*(size/4)+x/4)*16+(y%4)*4+x%4)*2;
        return (ushort)((texture[at]<<8)|texture[at+1]);
    }
    private static float ReadFloat(byte[] source,int at)=>BitConverter.ToSingle(source,at);
    private static int[] Gray(int value)=>new[]{value,value,value,value};

    // Evaluate the emitted TEV equations, including the hardware's 8-bit
    // interpolation convention. This catches mask/opacity and register mistakes
    // without attempting to create native GameCube resources on the host.
    private static int[] Evaluate(byte[] program,int[] raster,int[] first,int[] second)
    {
        var registers=new int[4,4];
        for(int stage=0;stage<program[8];stage++){
            int at=176+stage*16;int[] sample=program[at+7]==0?first:second;
            var rgb=new int[3];
            for(int channel=0;channel<3;channel++){
                int a=ColorOperand(program,at,program[at],channel,raster,sample,registers);
                int b=ColorOperand(program,at,program[at+1],channel,raster,sample,registers);
                int c=ColorOperand(program,at,program[at+2],channel,raster,sample,registers);
                int d=ColorOperand(program,at,program[at+3],channel,raster,sample,registers);
                rgb[channel]=Combine(a,b,c,d);
            }
            int alphaAt=at+8;
            if((program[alphaAt+5]&128)==0)throw new Exception("Shallow materials must explicitly define alpha at every stage");
            int alpha=Combine(AlphaOperand(program[alphaAt],raster,sample,registers),AlphaOperand(program[alphaAt+1],raster,sample,registers),
                AlphaOperand(program[alphaAt+2],raster,sample,registers),AlphaOperand(program[alphaAt+3],raster,sample,registers));
            for(int channel=0;channel<3;channel++)registers[program[at+5]&3,channel]=rgb[channel];
            registers[program[alphaAt+5]&3,3]=alpha;
        }
        return new[]{registers[0,0],registers[0,1],registers[0,2],registers[0,3]};
    }
    private static int ColorOperand(byte[] program,int at,int input,int channel,int[] raster,int[] sample,int[,] registers)
    {
        if(input<=7)return registers[input/2,(input&1)==0?channel:3];
        return input switch{
            8=>sample[channel],9=>sample[3],10=>raster[channel],11=>raster[3],12=>255,13=>128,
            14=>program[64+(program[at+6]-12)*36+channel],15=>0,_=>throw new Exception("Unsupported color operand")};
    }
    private static int AlphaOperand(int input,int[] raster,int[] sample,int[,] registers)
    {
        if(input<4)return registers[input,3];
        return input switch{4=>sample[3],5=>raster[3],6=>255,7=>0,_=>throw new Exception("Unsupported alpha operand")};
    }
    private static int Combine(int a,int b,int c,int d)
    {
        int weight=c+(c>>7);
        return Math.Clamp(d+((a*(256-weight)+b*weight+128)>>8),0,255);
    }
}
