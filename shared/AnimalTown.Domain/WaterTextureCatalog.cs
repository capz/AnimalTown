namespace AnimalTown;

// The authoring tool and native loader share one bounded, optional asset contract.
// Missing artwork always leaves the procedural material available.
public static class WaterTextureCatalog
{
    public const int Count=6,TotalNativeBytes=64*1024;
    public const string SourceDirectory="assets/textures/environment/water";
    public static string Role(int index)=>index switch {
        0=>"riverbed-color",1=>"surface-ripples",2=>"bed-caustics",
        3=>"surface-distortion",4=>"shoreline-foam",5=>"surface-slopes",
        _=>throw new ArgumentOutOfRangeException("index")
    };
    public static int IndexOfRole(string role)
    {for(int i=0;i<Count;i++)if(Role(i)==role)return i;return -1;}
    public static string Source(int index)=>SourceDirectory+"/"+Role(index)+".png";
    // The ISO and Depot payload deliberately use flat, identical runtime names.
    public static string DiscName(int index)=>"water-"+Role(index)+".dndtex";
    public static int IndexOfDiscName(string name)
    {for(int i=0;i<Count;i++)if(DiscName(i)==name)return i;return -1;}
    public static int Edge(int index){Role(index);return index==0?128:64;}
    // GX I8=1, IA8=3, RGB565=4; never promote masks to RGBA textures.
    public static int NativeFormat(int index)=>index switch {
        0 or 3=>4,1 or 2=>1,4 or 5=>3,_=>throw new ArgumentOutOfRangeException("index")
    };
    public static int NativeBytes(int index)=>Edge(index)*Edge(index)*(NativeFormat(index)==1?1:2);
    public static void Validate(byte[] package,int index)
    {
        TexturePixels.Validate(package);
        if(TexturePixels.Int(package,4)!=2||TexturePixels.Width(package)!=Edge(index)||
           TexturePixels.Height(package)!=Edge(index)||TexturePixels.Int(package,8)!=NativeFormat(index)||
           TexturePixels.Int(package,24)!=1||package.Length!=32+NativeBytes(index))
            throw new InvalidOperationException("Water texture does not match its role: "+Role(index));
    }
}
