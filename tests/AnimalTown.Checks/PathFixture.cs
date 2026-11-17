using AnimalTown;
using System.Text.Json;

internal static class PathFixture
{
    private sealed class Storage : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        public byte[] Read(string name)=>Files[name];
        public void Write(string name,byte[] bytes)=>Files[name]=(byte[])bytes.Clone();
    }
    internal static void Write(string output)
    {
        var original=new GameState(3){Name="Ari",Tutorial=6};original.PlaceHouse(12,22,false);original.Rollover(21014,2027*12+7);
        var snapshot=SaveCodec.Encode(original);
        foreach(string scene in new[]{"village","plaza","remote","door"})foreach(bool roads in new[]{false,true}){
            var s=SaveCodec.Decode(snapshot);s.Space=0;
            s.X=scene=="village"?23.5f:scene=="plaza"?39.5f:scene=="remote"?96.5f:15.5f;s.Z=scene=="village"?16.5f:scene=="plaza"?32.5f:scene=="remote"?98.5f:26.5f;
            if(!roads)s.Town.Paths.Clear();
            string dir=Path.Combine(output,scene+(roads?"-roads":"-baseline"));Directory.CreateDirectory(dir);
            var disk=new Storage();new SaveRepository(disk).Save(s);
            foreach(var pair in disk.Files)File.WriteAllBytes(Path.Combine(dir,pair.Key),pair.Value);
            File.WriteAllText(Path.Combine(dir,"fixture.json"),JsonSerializer.Serialize(new{seed=s.Town.Seed,roads,x=s.X,z=s.Z,cells=s.Town.Paths.Count,unconnected=s.Town.Paths.Unconnected,terrainSha=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s.Town.Tiles)),pathsSha=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s.Town.Paths.Packed))},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
}
