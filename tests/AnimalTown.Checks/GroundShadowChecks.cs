using AnimalTown;

internal static class GroundShadowChecks
{
    internal static void Run(Action<bool,string> check)
    {
        CliffShadowChecks.Run(check);
        VillagerShadowChecks.Run(check);
        ShadowCoverageChecks.Run(check);
        var t=new Town(73);Array.Fill(t.Tiles,(byte)Terrain.Grass);Array.Clear(t.Elevation);
        var g=new GroundShadow();
        void Build(ShadowCaster caster){g.Begin(t,caster);int jobs=0;while(!g.Advance()){jobs++;check(jobs<16,"shadow build has a bounded row count");}check(!g.Overflow&&g.Vertices<=GroundShadow.MaximumTriangles*3,"shadow proxy stays within its fixed vertex budget");}
        for(int profile=0;profile<4;profile++){
            var caster=new ShadowCaster(80,80,3,profile,0);Build(caster);check(g.Vertices>12,"each caster profile has receiver geometry");
            for(int i=0;i<g.Vertices;i++){
                float x=g.Positions[i*3],y=g.Positions[i*3+1],z=g.Positions[i*3+2];
                check(x>=caster.Left-.0001f&&x<=caster.Right+.0001f,"shadow clipped to projection bounds");
                int row=(int)Math.Floor(caster.Top);float error=float.MaxValue;
                for(int r=row;r<=Math.Ceiling(caster.Bottom);r++){
                    var a=CylinderWorld.Surface(r,0,.012f);var b=CylinderWorld.Surface(r+1,0,.012f);float f=(z-a.Z)/(b.Z-a.Z);
                    if(f>=-.0001f&&f<=1.0001f)error=Math.Min(error,Math.Abs(y-a.Y-(b.Y-a.Y)*f));
                }
                check(error<.0001f,"clipped shadow uses the actual receiver row chord, not an independently curved surface");
                check(g.Uvs[i*2]>=profile%2*.5f+2f/128-.0001f&&g.Uvs[i*2]<=profile%2*.5f+62f/128+.0001f,"UVs stay inside the profile gutter");
            }
        }
        for(int mode=6;mode<=9;mode++){
            Array.Clear(t.Elevation);for(int z=78;z<84;z++)for(int x=78;x<84;x++)t.Elevation[z*Town.Width+x]=(byte)mode;
            // Within one 2m section, test both support surfaces independently.
            foreach(float lx in new[]{.25f,1.75f})foreach(float lz in new[]{.25f,1.75f}){
                float x=80+lx,z=80+lz,h=TerrainElevation.At(t,x,z);Build(new ShadowCaster(x,z,.75f,0,h));
                for(int i=0;i<g.Vertices;i+=3){
                    float cx=(g.Positions[i*3]+g.Positions[(i+1)*3]+g.Positions[(i+2)*3])/3;
                    // Recover logical Z by checking the emitted horizontal cap radius.
                    for(int j=0;j<3;j++){
                        float py=g.Positions[(i+j)*3+1]+CylinderWorld.Radius,pz=g.Positions[(i+j)*3+2];
                        float radius=(float)Math.Sqrt(py*py+pz*pz);
                        check(Math.Abs(radius-(CylinderWorld.Radius+h+.012f))<.005f,"diagonal shadows never bridge the upper and lower caps");
                    }
                    check(float.IsFinite(cx),"diagonal clipping emits finite triangles");
                }
            }
        }
        Array.Clear(t.Elevation);Array.Fill(t.Tiles,(byte)Terrain.Water);Build(new ShadowCaster(80,80,3,0,WaterTerrain.RiverHeight));check(g.Vertices==0,"no shadow polygons on water or sloped wet banks");
        Array.Fill(t.Tiles,(byte)Terrain.Building);Build(new ShadowCaster(80,80,3,3,0));check(g.Vertices==0,"no ground shadow painted on building footprints");
        Array.Fill(t.Tiles,(byte)Terrain.Grass);for(int z=79;z<82;z++)for(int x=79;x<82;x++)t.Tiles[z*Town.Width+x]=(byte)Terrain.Building;
        var house=new ShadowCaster(80.5f,80.5f,3,3,0);Build(house);bool front=false;
        for(int i=0;i<g.Vertices;i++)if(g.Positions[i*3]>82)front=true;
        check(front&&house.Bottom>83,"house projection extends beyond its camera-facing footprint");
        Array.Fill(t.Tiles,(byte)Terrain.Grass);for(int z=78;z<84;z++)for(int x=78;x<84;x++)t.Elevation[z*Town.Width+x]=TerrainElevation.RampNorth;
        Build(new ShadowCaster(80.5f,80.5f,.75f,0,1.5f));check(g.Vertices>0,"continuous natural ramp receives a slanted shadow");
        Array.Clear(t.Elevation);for(int z=76;z<86;z++){t.Tiles[z*Town.Width+78]=(byte)Terrain.Sea;t.Tiles[z*Town.Width+79]=(byte)Terrain.Beach;}
        check(GroundSurface.HeightAt(t,79,80,79,80)==WaterSurface.Height&&GroundSurface.HeightAt(t,79,80,80,80)==0,"floor and overlay share the slanted beach-edge receiver heights");
        Build(new ShadowCaster(79.5f,80.5f,3,1,TerrainElevation.At(t,79.5f,80.5f)));bool shoreVertex=false;
        for(int i=0;i<g.Vertices;i++)if(Math.Abs(g.Positions[i*3]-79)<.0001f){
            float py=g.Positions[i*3+1]+CylinderWorld.Radius,pz=g.Positions[i*3+2];
            check(Math.Abs((float)Math.Sqrt(py*py+pz*pz)-(CylinderWorld.Radius+WaterSurface.Height+.012f))<.006f,"shadow meets the actual wet beach toe instead of floating over its slope");shoreVertex=true;
        }
        check(shoreVertex,"shoreline receiver fixture exercises a clipped beach edge");
        Array.Fill(t.Tiles,(byte)Terrain.Grass);
        byte[] atlas=GroundShadow.Atlas();check(atlas.Length==32768,"all four shared IA8 masks use 32KiB");
        for(int kind=0;kind<4;kind++){
            int total=0;for(int y=0;y<64;y++)for(int x=0;x<64;x++){
                int px=x+kind%2*64,py=y+kind/2*64,at=(((py/4)*32+px/4)*16+(py%4)*4+px%4)*2;
                check(atlas[at+1]==255,"GX IA8 intensity preserves the material tint");total+=atlas[at];
                if(x<2||x>61||y<2||y>61)check(atlas[at]==0,"atlas masks have transparent filtering gutters");
            }
            check(total>50000,"each shadow silhouette has a visible soft core");
        }
        Array.Clear(t.Elevation);int version=t.ShadowVersion;
        check(t.AddGround(1,80,80,2,0)&&t.ShadowVersion==version+1,"planting invalidates caster selection without rebuilding terrain");
        t.RemoveGround(t.GroundCount-1);check(t.ShadowVersion==version+2,"removing or chopping a tree invalidates its shadow");
    }
}
