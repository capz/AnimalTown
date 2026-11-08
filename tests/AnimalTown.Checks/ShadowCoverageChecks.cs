using AnimalTown;

internal static class ShadowCoverageChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var town=new Town(31);Array.Fill(town.Tiles,(byte)Terrain.Grass);Array.Clear(town.Elevation);
        var legacy=new GroundShadow();var coverage=new GroundShadow();
        void Build(GroundShadow g,bool optimized,ShadowCaster caster){if(optimized)g.BeginCoverage(town,caster);else g.Begin(town,caster,true);int stages=0;while(!g.Advance())check(++stages<32,"coverage work is staged and bounded");check(!g.Overflow,"coverage fixture fits receiver capacity");}
        bool Inside(GroundShadow g,float x,float z){
            for(int i=0;i<g.Vertices;i+=3){int at=i*2;float ax=g.Uvs[at],az=g.Uvs[at+1],bx=g.Uvs[at+2],bz=g.Uvs[at+3],cx=g.Uvs[at+4],cz=g.Uvs[at+5];
                float a=(bx-ax)*(z-az)-(bz-az)*(x-ax),b=(cx-bx)*(z-bz)-(cz-bz)*(x-bx),c=(ax-cx)*(z-cz)-(az-cz)*(x-cx);
                if(a>=-.000001f&&b>=-.000001f&&c>=-.000001f||a<=.000001f&&b<=.000001f&&c<=.000001f)return true;
            }return false;
        }
        foreach(byte mode in new byte[]{0,1})foreach(int profile in new[]{0,1,2,3}){
            Array.Fill(town.Elevation,mode);var caster=new ShadowCaster(80.25f,80.75f,3,profile,mode==0?0:2);
            Build(legacy,false,caster);coverage.Positions[0]=12345;Build(coverage,true,caster);
            check(coverage.FlatCoverage&&coverage.Vertices==6,"verified level ground uses two coverage triangles at both terrace levels");
            check(coverage.Positions[0]==12345,"coverage-only builds do not write unused projected positions");
            for(int z=0;z<31;z++)for(int x=0;x<31;x++){
                float px=caster.Left-.1f+(caster.Right-caster.Left+.2f)*x/30,pz=caster.Top-.1f+(caster.Bottom-caster.Top+.2f)*z/30;
                check(Inside(coverage,px,pz)==Inside(legacy,px,pz),"flat shortcut preserves the exact receiver footprint");
            }
        }
        Array.Clear(town.Elevation);
        foreach(int kind in new[]{(int)Terrain.Water,(int)Terrain.Beach,(int)Terrain.Building,(int)Terrain.Cliff,(int)Terrain.CoastNorth}){
            town.Tiles[80*Town.Width+80]=(byte)kind;var caster=new VillagerShadowPatch(town,0,80.5f,80.5f).Receiver;
            Build(legacy,false,caster);Build(coverage,true,caster);
            check(!coverage.FlatCoverage&&legacy.Vertices==coverage.Vertices,"water, beach, buildings and coastal footprints keep full clipping");
            check(legacy.Uvs.Take(legacy.Vertices*2).SequenceEqual(coverage.Uvs.Take(coverage.Vertices*2)),"coverage-only fallback preserves clipped UV triangles");
            town.Tiles[80*Town.Width+80]=(byte)Terrain.Grass;
        }
        for(byte mode=2;mode<=TerrainElevation.MaximumMode;mode++){
            for(int z=80;z<82;z++)for(int x=80;x<82;x++)town.Elevation[z*Town.Width+x]=mode;
            var caster=new VillagerShadowPatch(town,0,80.5f,80.5f).Receiver;Build(legacy,false,caster);Build(coverage,true,caster);
            check(!coverage.FlatCoverage&&legacy.Vertices==coverage.Vertices,"ramps and diagonal corners use the established topology");
            check(legacy.Uvs.Take(legacy.Vertices*2).SequenceEqual(coverage.Uvs.Take(coverage.Vertices*2)),"ramp/corner support coverage is identical to the original path");
        }
        Array.Clear(town.Elevation);var cache=new ShadowReceiverCache();var uv=new float[GroundShadow.MaximumTriangles*6];var original=new ShadowCaster(80.5f,80.5f,3,0,0);Build(coverage,true,original);
        cache.Store(town,original,coverage.Uvs,coverage.Vertices,true);
        check(cache.Restore(town,original,uv,out int count,out bool flat)&&count==6&&flat,"a revisited static footprint restores without reclipping");
        check(uv.Take(count*2).SequenceEqual(coverage.Uvs.Take(count*2)),"cached UVs restore exactly");
        town.SetTerrain(0,0,Terrain.Water);check(cache.Valid(town,original),"distant terrain changes preserve cached receiver coverage");
        town.SetTerrain(80,80,Terrain.Water);check(!cache.Valid(town,original),"local terrain changes invalidate the affected receiver");town.SetTerrain(80,80,Terrain.Grass);
        for(int i=0;i<ShadowReceiverCache.Capacity;i++)cache.Store(town,new ShadowCaster(10+i*2,40,1,0,0),uv,6,true);
        var first=new ShadowCaster(10,40,1,0,0);check(cache.Valid(town,first),"warm cache hit updates recency");
        cache.Store(town,new ShadowCaster(150,40,1,0,0),uv,6,true);
        check(cache.Valid(town,first)&&!cache.Valid(town,new ShadowCaster(12,40,1,0,0)),"bounded receiver cache evicts the least recently used entry");
        check(!cache.Valid(new Town(31),first),"another town cannot reuse old terrain snapshots");
        check(ShadowReceiverCache.MaximumPayloadBytes<192*1024,"retained receiver payload has a fixed memory ceiling");
    }
}
