namespace AnimalTown;

// Shared by the package writer and runtime validator, without a storage SDK
// dependency in the portable domain. Readers must bound allocation first.
public static class ContentIdentity
{
    public const int MaximumLength=512;
    public static string Create(string commit,string version,string sourceDigest,int runtimeProfile)
        =>"ANIMALTOWN-CONTENT-1\n"+commit+"\n"+version+"\n"+sourceDigest+"\n"+runtimeProfile+"\n";
    public static bool Matches(byte[] bytes,string expected)
    {
        if(expected.Length>MaximumLength||bytes.Length!=expected.Length)return false;
        for(int i=0;i<bytes.Length;i++)if(bytes[i]!=expected[i])return false;
        return true;
    }
}
