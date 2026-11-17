using System.Text;
using System.Text.RegularExpressions;

internal static class DiscVersionFile
{
    private static byte[] Bytes(string version)
    {
        if(version.Length>64||!Regex.IsMatch(version,@"\A(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)\.(?:0|[1-9][0-9]*)(?:\+[0-9a-f]{12})?\z"))
            throw new InvalidDataException("Invalid ISO release version");
        foreach(string counter in version.Split('+')[0].Split('.'))if(!int.TryParse(counter,out _))throw new InvalidDataException("Version counter overflow");
        return Encoding.ASCII.GetBytes(version);
    }
    internal static void Write(string directory,string version)
    {File.WriteAllBytes(Path.Combine(directory,"version.md"),Bytes(version));}
    internal static void Verify(byte[] actual,string expected)
    {if(!actual.AsSpan().SequenceEqual(Bytes(expected)))throw new InvalidDataException("ISO version.md differs from the frozen release identity");}
    internal static void Check()
    {
        string directory=Path.Combine(Path.GetTempPath(),"animaltown-version-file-"+Guid.NewGuid());Directory.CreateDirectory(directory);
        try{
            foreach(string version in new[]{"0.22.0","1.2.3","0.22.0+0123456789ab"}){
                Write(directory,version);byte[] bytes=File.ReadAllBytes(Path.Combine(directory,"version.md"));Verify(bytes,version);
                if(bytes.Length!=version.Length)throw new Exception("Version file contains a BOM or extra bytes");
                foreach(byte[] invalid in new[]{bytes.Concat(new byte[]{10}).ToArray(),new byte[]{239,187,191}.Concat(bytes).ToArray(),Encoding.ASCII.GetBytes("0.1.0"),Array.Empty<byte>()}){
                    bool refused=false;try{Verify(invalid,version);}catch(InvalidDataException){refused=true;}
                    if(!refused)throw new Exception("Mismatched version file accepted");
                }
            }
            foreach(string invalid in new[]{"", "v1.2.3","01.2.3","1.2.3\n","1.2.3 ","1.2.3+abcdef","2147483648.0.0"}){
                bool refused=false;try{Write(directory,invalid);}catch(InvalidDataException){refused=true;}
                if(!refused)throw new Exception("Invalid release identity accepted");
            }
            Console.WriteLine("ISO version file: frozen numeric/review identities, exact ASCII bytes, no BOM/newline and mismatch rejection passed");
        }finally{Directory.Delete(directory,true);}
    }
}
