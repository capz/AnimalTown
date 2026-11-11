using System.Buffers.Binary;

// The SDK supplies code, BSS and the IPL header as one verified image.
// AnimalTown owns only the surrounding disc layout and game-specific assets.
internal static class SdkApploader
{
    internal const int DiscOffset=0x2440,DolOffset=0x10000;
    internal static void Validate(byte[] image)
    {
        if(image.Length<64||image.Length>DolOffset-DiscOffset)throw new InvalidDataException("SDK apploader image size is invalid");
        uint Read(int at)=>BinaryPrimitives.ReadUInt32BigEndian(image.AsSpan(at,4));
        uint entry=Read(16),code=Read(20),trailer=Read(24);
        if(entry!=0x81200000||code==0||(code&31)!=0||trailer!=0||Read(28)!=0||32L+code!=image.Length)
            throw new InvalidDataException("SDK apploader IPL header is invalid");
        if(image[4]!='/'||image[7]!='/'||image[10]!=0)throw new InvalidDataException("SDK apploader date header is invalid");
    }
    internal static void Check()
    {
        var image=new byte[96];System.Text.Encoding.ASCII.GetBytes("2026/11/11").CopyTo(image,0);
        BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(16),0x81200000);BinaryPrimitives.WriteUInt32BigEndian(image.AsSpan(20),64);Validate(image);
        foreach(int at in new[]{4,7,10,16,20,23,24,28}){
            var bad=(byte[])image.Clone();bad[at]^=1;Reject(bad);
        }
        Reject(image.AsSpan(0,95).ToArray());Reject(new byte[32]);Reject(new byte[DolOffset-DiscOffset+1]);
        Console.WriteLine("SDK apploader: complete IPL header, fixed load/disc bounds, DMA size, exact payload length and malformed-image rejection passed");
    }
    private static void Reject(byte[] bytes)
    {try{Validate(bytes);}catch(InvalidDataException){return;}throw new InvalidDataException("Invalid SDK apploader image was accepted");}
}
