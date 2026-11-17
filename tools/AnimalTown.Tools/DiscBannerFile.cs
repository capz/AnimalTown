using System.Text;

internal static class DiscBannerFile
{
    internal const string GameTitle="Smalltown Forest",Maker="Capz";
    // The pinned updater's ImageFiles.TryReadVersion matches this boot-header
    // prefix byte for byte. Keep protocol identity separate from visible art.
    internal const string BootTitlePrefix="AnimalTown / ";
    private const int ImageOffset=0x20,ImageBytes=96*32*2,TextOffset=0x1820,TextBytes=0x140;
    internal static string BootTitle(string version)=>BootTitlePrefix+version;
    internal static string BannerTitle(string version)=>GameTitle+" / "+version;
    private static string Description(string version)=>"Village life and online friends.\nVersion "+version;

    internal static byte[] Create(byte[] image,string version,bool pal=false)
    {
        if(image.Length!=ImageBytes)throw new InvalidDataException("Disc banner must be 96x32 RGB5A3");
        DiscVersionFile.Verify(Encoding.ASCII.GetBytes(version),version);
        var banner=new byte[TextOffset+TextBytes*(pal?6:1)];
        Encoding.ASCII.GetBytes(pal?"BNR2":"BNR1").CopyTo(banner,0);
        image.CopyTo(banner,ImageOffset);
        for(int language=0;language<(pal?6:1);language++){
            int at=TextOffset+language*TextBytes;
            WriteText(banner,at,32,GameTitle);WriteText(banner,at+0x20,32,Maker);
            WriteText(banner,at+0x40,64,BannerTitle(version));WriteText(banner,at+0x80,64,Maker);
            WriteText(banner,at+0xc0,128,Description(version));
        }
        Verify(banner,version,pal);
        return banner;
    }
    private static void WriteText(byte[] bytes,int at,int length,string text)
    {
        byte[] value=Encoding.ASCII.GetBytes(text);
        if(value.Length>=length)throw new InvalidDataException("Disc banner metadata too long");
        value.CopyTo(bytes,at);
    }
    internal static void Verify(byte[] banner,string version,bool pal=false)
    {
        int languages=pal?6:1;
        if(banner.Length!=TextOffset+TextBytes*languages||Encoding.ASCII.GetString(banner,0,4)!=(pal?"BNR2":"BNR1"))
            throw new InvalidDataException("Disc banner format or size invalid");
        for(int i=4;i<ImageOffset;i++)if(banner[i]!=0)throw new InvalidDataException("Disc banner reserved bytes must be zero");
        for(int language=0;language<languages;language++){
            int at=TextOffset+language*TextBytes;
            VerifyText(banner,at,32,GameTitle);VerifyText(banner,at+0x20,32,Maker);
            VerifyText(banner,at+0x40,64,BannerTitle(version));VerifyText(banner,at+0x80,64,Maker);
            VerifyText(banner,at+0xc0,128,Description(version));
        }
    }
    private static void VerifyText(byte[] banner,int at,int length,string expected)
    {
        int end=Array.IndexOf(banner,(byte)0,at,length);
        if(end<0||Encoding.ASCII.GetString(banner,at,end-at)!=expected)
            throw new InvalidDataException("Disc banner branding or build identity differs from the expected release");
        for(int i=end;i<at+length;i++)if(banner[i]!=0)throw new InvalidDataException("Disc banner text padding must be zero");
    }
    internal static void Check()
    {
        if(BootTitle("0.22.0")!="AnimalTown / 0.22.0"||BootTitle("0.22.0+0123456789ab")!="AnimalTown / 0.22.0+0123456789ab")
            throw new Exception("Boot-header identity is incompatible with the pinned AnimalTown updater");
        var image=new byte[ImageBytes];for(int i=0;i<image.Length;i+=2){image[i]=0x80;image[i+1]=(byte)(i/2);}
        foreach(bool pal in new[]{false,true})foreach(string version in new[]{"0.22.0","0.22.0+0123456789ab"}){
            byte[] banner=Create(image,version,pal);
            if(!banner.AsSpan(ImageOffset,ImageBytes).SequenceEqual(image))throw new Exception("Disc banner changed approved RGB5A3 pixels");
            // Catch stale copied release metadata, mismatched variants, malformed
            // padding and corrupt magic without generating a full-size disc.
            MustReject(banner,version+"0",pal);MustReject(banner,version,!pal);
            MustReject(banner[..^1],version,pal);
            foreach(int at in new[]{0,4,TextOffset+31,TextOffset+0x40,TextOffset+0xc0}){
                byte[] corrupt=(byte[])banner.Clone();corrupt[at]^=1;MustReject(corrupt,version,pal);
            }
            if(pal){byte[] corrupt=(byte[])banner.Clone();corrupt[TextOffset+5*TextBytes]^=1;MustReject(corrupt,version,pal);}
        }
        Console.WriteLine("Disc banners: BNR1/BNR2 size, branding, version/commit, unchanged pixels and stale metadata rejection passed");
    }
    private static void MustReject(byte[] banner,string version,bool pal)
    {
        try{Verify(banner,version,pal);}catch(InvalidDataException){return;}
        throw new Exception("Invalid disc banner accepted");
    }
}
