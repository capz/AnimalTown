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
        for(int z=14;z<=32;z++)for(int x=20;x<38;x++)if(TownPaths.Surface(original.Town,x,z))original.Town.Traffic.Visits[z*Town.Width+x]=(byte)(x<24?200:10);
        original.Town.Traffic.Refresh(original.Town);
        var snapshot=SaveCodec.Encode(original);
        foreach(string scene in new[]{"village","plaza","remote","door","quiet"})foreach(bool roads in new[]{false,true}){
            var s=SaveCodec.Decode(snapshot);s.Space=0;
            s.X=(scene=="village"||scene=="quiet")?23.5f:scene=="plaza"?39.5f:scene=="remote"?96.5f:15.5f;s.Z=(scene=="village"||scene=="quiet")?16.5f:scene=="plaza"?32.5f:scene=="remote"?98.5f:26.5f;
            if(scene=="remote")Remote(s);
            if(scene=="quiet")for(int i=0;i<6;i++){
                var v=s.Town.Residents[i];v.Space=16+i;v.PosX=1.5f;v.PosZ=2.5f;v.JourneyBuilding=i+1;v.JourneyPhase=3;v.Dwell=36000;
            }
            if(!roads)s.Town.Paths.Clear();
            string dir=Path.Combine(output,scene+(roads?"-roads":"-baseline"));Directory.CreateDirectory(dir);
            var disk=new Storage();new SaveRepository(disk).Save(s);
            foreach(var pair in disk.Files)File.WriteAllBytes(Path.Combine(dir,pair.Key),pair.Value);
            File.WriteAllText(Path.Combine(dir,"fixture.json"),JsonSerializer.Serialize(new{seed=s.Town.Seed,roads,x=s.X,z=s.Z,cells=s.Town.Paths.Count,unconnected=s.Town.Paths.Unconnected,terrainSha=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s.Town.Tiles)),pathsSha=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(s.Town.Paths.Packed))},new JsonSerializerOptions{WriteIndented=true}));
        }
    }
    private static void Remote(GameState s)
    {
        var painted=new bool[TerrainArchivePlan.Count];var rows=new CylinderRows();var view=new TerrainArchiveView();
        for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++)if(TownPaths.Painted(s.Town,x,z))painted[TerrainArchivePlan.Index(x/8*8,z/8*8)]=true;
        for(int z=Town.Height-4;z>=4;z-=2)for(int x=4;x<Town.Width-4;x+=2){
            if(!s.CanWalk(x+.5f,z+.5f,0))continue;view.Prepare(x+.5f,z+.5f,rows);bool demand=false;
            for(int i=0;i<view.Count;i++)if(painted[view.Sections[i]]){demand=true;break;}
            if(!demand){s.X=x+.5f;s.Z=z+.5f;return;}
        }
        throw new Exception("Fixture has no safe view without path demand");
    }

}
