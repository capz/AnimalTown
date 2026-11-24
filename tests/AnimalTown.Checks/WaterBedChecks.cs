using AnimalTown;

internal static class WaterBedChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var town=new Town(7319);Array.Fill(town.Tiles,(byte)Terrain.Water);Array.Clear(town.Elevation);
        const int x=80,z=80;
        check(WaterBedGeometry.HasBed(town,x,z),"inland water receives a physical opaque bed");
        town.SetTerrain(x,z,Terrain.Bridge);
        check(WaterBedGeometry.HasBed(town,x,z)&&!WaterBedGeometry.TryRock(town,x,z,out _),"bridge underpasses retain the bed without decorative rock obstructions");
        foreach(var terrain in new[]{Terrain.Grass,Terrain.Sea,Terrain.CoastSouth}){
            town.SetTerrain(x,z,terrain);check(!WaterBedGeometry.HasBed(town,x,z),"opaque sea and dry ground do not allocate hidden bed geometry");
        }
        check(!WaterBedGeometry.HasBed(town,-1,z),"the external ocean has no hidden riverbed");
        town.SetTerrain(x,z,Terrain.Water);
        for(int zz=70;zz<=90;zz++)for(int xx=70;xx<x;xx++)town.SetTerrain(xx,zz,Terrain.Grass);
        float previous=0;
        for(int dx=0;dx<8;dx++){
            float depth=WaterBedGeometry.Depth(town,x+dx,z);
            check(depth>=WaterBedGeometry.MinimumDepth&&depth<=WaterBedGeometry.MaximumDepth&&depth>=previous,"bed depth is bounded and deepens gently away from a bank");previous=depth;
        }
        for(int zz=z;zz<z+4;zz++)for(int xx=x;xx<x+8;xx++)for(int step=0;step<=8;step++){
            float t=step/8f;
            check(Near(WaterBedGeometry.HeightAt(town,xx,zz,xx+1,zz+t),WaterBedGeometry.HeightAt(town,xx+1,zz,xx+1,zz+t)),"adjacent bed cells meet exactly on east/west edges");
            check(Near(WaterBedGeometry.HeightAt(town,xx,zz,xx+t,zz+1),WaterBedGeometry.HeightAt(town,xx,zz+1,xx+t,zz+1)),"adjacent bed cells meet exactly across section row edges");
            float depth=WaterSurface.HeightAt(town,xx,zz,xx+t,zz+t)-WaterBedGeometry.HeightAt(town,xx,zz,xx+t,zz+t);
            check(depth>=WaterBedGeometry.MinimumDepth-.00001f&&depth<=WaterBedGeometry.MaximumDepth+.00001f,"every bed diagonal stays below its existing surface");
        }
        float before=WaterBedGeometry.HeightAt(town,x+2,z,x+2.5f,z+.5f),surface=WaterSurface.HeightAt(town,x+2,z,x+2.5f,z+.5f);
        town.SetTerrain(x+2,z,Terrain.Bridge);
        check(Near(before,WaterBedGeometry.HeightAt(town,x+2,z,x+2.5f,z+.5f))&&Near(surface,WaterSurface.HeightAt(town,x+2,z,x+2.5f,z+.5f)),"bed and water continue underneath a bridge at their original heights");
        check(TerrainElevation.AtCell(town,x+2,z,x+2.5f,z+.5f)==0,"render-only bed depth does not lower the bridge deck");

        // Exercise every local shoreline topology, especially a diagonal bank
        // whose toe must connect to the cardinal skirts in neighboring cells.
        for(int mask=0;mask<256;mask++){
            Array.Fill(town.Tiles,(byte)Terrain.Water);
            for(int side=0;side<4;side++)if((mask&(1<<side))!=0)town.SetTerrain(RiverBankGeometry.NeighborX(x,side),RiverBankGeometry.NeighborZ(z,side),Terrain.Grass);
            for(int corner=0;corner<4;corner++)if((mask&(16<<corner))!=0)town.SetTerrain(x+(corner==0||corner==3?-1:1),z+(corner<2?-1:1),Terrain.Grass);
            int skirtVertices=0;
            for(int side=0;side<4;side++)if(WaterBedGeometry.BankEdge(town,x,z,side,out var a,out var b)){
                skirtVertices+=6;
                check(RiverBankGeometry.Edge(town,x,z,side,out _,out _,out var expectedB,out var expectedA)&&Same(a,expectedA)&&Same(b,expectedB),"underwater cardinal skirts start at the exact existing bank toe");
                Lower(a);Lower(b);
            }
            for(int corner=0;corner<4;corner++)if(WaterBedGeometry.CornerEdge(town,x,z,corner,out var a,out var b)){
                skirtVertices+=6;
                check(RiverBankGeometry.Corner(town,x,z,corner,out _,out var expectedB,out var expectedA)&&Same(a,expectedA)&&Same(b,expectedB),"underwater corner skirts close the diagonal bank toe");
                Lower(a);Lower(b);
            }
            check(skirtVertices+6<=54,"all submerged bank topologies fit the per-cell geometry budget");
            void Lower(RiverBankPoint p){
                float bottom=WaterBedGeometry.HeightAt(town,x,z,p.X,p.Z);
                check(p.X>=x&&p.X<=x+1&&p.Z>=z&&p.Z<=z+1&&bottom<p.Height-.1f,"submerged skirt has positive depth and stays inside its water cell");
            }
        }

        Array.Fill(town.Tiles,(byte)Terrain.Water);int count=0,emerging=0,submerged=0;
        var blocks=new HashSet<int>();
        for(int zz=32;zz<96;zz++)for(int xx=32;xx<96;xx++)if(WaterBedGeometry.TryRock(town,xx,zz,out var rock)){
            count++;check(blocks.Add((zz/2)*Town.Width+xx/2),"a fixed 2x2m block emits at most one decorative stone");
            check(WaterBedGeometry.TryRock(town,xx,zz,out var repeat)&&Same(rock,repeat),"rock placement, dimensions and height repeat exactly without RNG state");
            float water=WaterSurface.HeightAt(town,xx,zz,rock.X,rock.Z);
            if(rock.Top>water){emerging++;check(rock.Top-water>=.07999f&&rock.Top-water<=.14001f,"occasional rock heads protrude only by the authored small amount");}
            else{submerged++;check(rock.Top<=water-.05999f,"ordinary stones stay visibly submerged");}
            check(rock.Bottom<rock.Top&&rock.Top-rock.Bottom<.85f,"faceted stone height is positive and bounded");
            int vertices=WaterBedGeometry.RockVertexCount(town,xx,zz,rock);
            check(vertices>=WaterBedGeometry.RockBaseVertices&&vertices<=WaterBedGeometry.MaximumRockVertices,"waterline-clipped rock triangles remain within their static mesh budget");
            var capPoint=new RiverBankPoint(rock.X,rock.Z,rock.Top);
            if(rock.Top>water){
                var foot=WaterBedGeometry.RockPoint(rock,0,false);
                float below=WaterBedGeometry.CausticDistance(town,xx,zz,foot),above=WaterBedGeometry.CausticDistance(town,xx,zz,capPoint);
                var cut=WaterBedGeometry.WaterlineIntersection(foot,capPoint,below,above);
                check(vertices>WaterBedGeometry.RockBaseVertices&&WaterBedGeometry.SubmersionMask(town,xx,zz,capPoint)==0,"protruding caps have no caustics and receive explicit waterline splits");
                check(Math.Abs(WaterBedGeometry.CausticDistance(town,xx,zz,cut))<.00001f&&WaterBedGeometry.SubmersionMask(town,xx,zz,cut)<=1,"new clipping vertices lie on the dry-caustic boundary");
            }
            for(int side=0;side<WaterBedGeometry.RockSides;side++){
                var foot=WaterBedGeometry.RockPoint(rock,side,false);var upper=WaterBedGeometry.RockPoint(rock,side,true);
                check(foot.X>xx&&foot.X<xx+1&&foot.Z>zz&&foot.Z<zz+1,"each complete stone footprint remains inside its owning water cell");
                check(foot.Height<=WaterBedGeometry.HeightAt(town,xx,zz,foot.X,foot.Z)-.02499f,"lower rock ring is buried rather than floating above a sloping bed");
                var next=WaterBedGeometry.RockPoint(rock,side+1,true);var top=new RiverBankPoint(rock.X,rock.Z,rock.Top);
                check(Area(upper,next,top)>0,"stone top facets use the same visible winding as the water surface");
            }
            check(Same(WaterBedGeometry.RockPoint(rock,0,false),WaterBedGeometry.RockPoint(rock,WaterBedGeometry.RockSides,false)),"faceted stone ring closes without a seam");
            town.SetTerrain(xx,zz,Terrain.Bridge);check(!WaterBedGeometry.TryRock(town,xx,zz,out _),"turning a water cell into a bridge removes its decorative stone");town.SetTerrain(xx,zz,Terrain.Water);
        }
        check(count>200&&count<500&&emerging>20&&submerged>100,"the bounded decorative population is sparse and includes both submerged and exposed stones");
        for(int zz=32;zz<96;zz+=TerrainStream.Height)for(int xx=32;xx<96;xx+=TerrainStream.Width){
            int section=0;for(int dz=0;dz<TerrainStream.Height;dz++)for(int dx=0;dx<TerrainStream.Width;dx++)if(WaterBedGeometry.TryRock(town,xx+dx,zz+dz,out _))section++;
            check(section<=WaterBedGeometry.MaximumRocksPerSection,"static stone meshes obey the hard terrain-section vertex budget");
        }
        // Narrow channels and sloped banks must not be covered by rock feet.
        for(int zz=32;zz<96;zz++)for(int xx=32;xx<96;xx++)town.Tiles[zz*Town.Width+xx]=(byte)(xx%4==0?Terrain.Grass:Terrain.Water);
        town.NotifyTerrainChanged();int nearBank=0;
        for(int zz=33;zz<95;zz++)for(int xx=33;xx<95;xx++)if(WaterBedGeometry.TryRock(town,xx,zz,out var rock)){
            nearBank++;
            for(int side=0;side<4;side++)if(WaterBedGeometry.BankEdge(town,xx,zz,side,out var a,out var b))for(int i=0;i<WaterBedGeometry.RockSides;i++){
                var foot=WaterBedGeometry.RockPoint(rock,i,false);check(Area(a,b,foot)>0,"every accepted rock foot stays on the open-water side of a bank");
            }
        }
        check(nearBank>0,"bank clearance retains some stones in a normal three-metre channel");
        int version=town.TerrainVersion;long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)WaterBedGeometry.TryRock(town,33+(i%60),34+(i%59),out _);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated&&town.TerrainVersion==version,"deterministic stone queries allocate nothing and never mutate terrain or save state");
        ArchiveBudgets(check);
        Console.WriteLine("Water bed: continuous shallow depth, closed bank toes, bridge continuity and deterministic sparse stones passed");
    }
    private static void ArchiveBudgets(Action<bool,string> check)
    {
        // Count only the added geometry, using the native 36-byte draw vertex
        // and conservative per-half-section 1KiB cache rounding. Existing
        // terrain still needs the normal native archive/cache validation.
        int maxArchive=0,maxVisible=0,maxPart=0,maxViews=0;var rows=new CylinderRows();var view=new TerrainArchiveView();
        foreach(int seed in new[]{0,49,7319,1986}){
            var town=new Town(seed);var cache=new int[TerrainArchivePlan.Count];var parts=new int[cache.Length];int total=0;
            for(int z=0;z<Town.Height;z+=TerrainStream.Height)for(int x=0;x<Town.Width;x+=TerrainStream.Width){
                int bed=0,rocks=0;
                for(int dz=0;dz<TerrainStream.Height;dz++)for(int dx=0;dx<TerrainStream.Width;dx++){
                    int px=x+dx,pz=z+dz;if(!WaterBedGeometry.HasBed(town,px,pz))continue;bed+=6;
                    for(int side=0;side<4;side++)if(WaterBedGeometry.BankEdge(town,px,pz,side,out _,out _))bed+=6;
                    for(int corner=0;corner<4;corner++)if(WaterBedGeometry.CornerEdge(town,px,pz,corner,out _,out _))bed+=6;
                    if(WaterBedGeometry.TryRock(town,px,pz,out var rock))rocks+=WaterBedGeometry.RockVertexCount(town,px,pz,rock);
                }
                int page=TerrainArchivePlan.Index(x,z/TerrainArchivePlan.Height*TerrainArchivePlan.Height);
                foreach(int vertices in new[]{bed,rocks})if(vertices>0){
                    int bytes=(vertices*36+31)&~31;total+=bytes;cache[page]+=(bytes+1023)&~1023;parts[page]++;maxPart=Math.Max(maxPart,bytes);
                }
            }
            maxArchive=Math.Max(maxArchive,total);
            for(int z=8;z<Town.Height;z+=8)for(int x=8;x<Town.Width;x+=8){
                view.Prepare(x,z,rows);int bytes=0,views=0;
                for(int i=0;i<view.Count;i++){bytes+=cache[view.Sections[i]];views+=parts[view.Sections[i]];}
                maxVisible=Math.Max(maxVisible,bytes);maxViews=Math.Max(maxViews,views);
            }
        }
        check(maxPart<=65504,"each new native geometry part fits the existing 64KiB archive staging block");
        check(maxArchive<2*1024*1024&&maxVisible<256*1024,"representative towns keep added bed/rock geometry below one quarter of archive/cache budgets");
        Console.WriteLine("Water bed incremental payload: max archive "+maxArchive+"B; visible cache "+maxVisible+"B; half-section part "+maxPart+"B; uncoalesced extra views "+maxViews+" across 4 town seeds");
    }
    private static bool Near(float a,float b)=>Math.Abs(a-b)<.00001f;
    private static bool Same(RiverBankPoint a,RiverBankPoint b)=>Near(a.X,b.X)&&Near(a.Z,b.Z)&&Near(a.Height,b.Height);
    private static bool Same(WaterBedRock a,WaterBedRock b)=>a.X==b.X&&a.Z==b.Z&&a.RadiusX==b.RadiusX&&a.RadiusZ==b.RadiusZ&&a.Angle==b.Angle&&a.Bottom==b.Bottom&&a.Top==b.Top&&a.Variation==b.Variation;
    private static float Area(RiverBankPoint a,RiverBankPoint b,RiverBankPoint c)=>(b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
}
