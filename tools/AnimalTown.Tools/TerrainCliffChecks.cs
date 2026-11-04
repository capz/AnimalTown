using System.Buffers.Binary;
using System.Numerics;
using System.Text.Json;
using AnimalTown;
using SkiaSharp;

internal static class TerrainCliffChecks
{
    internal static void Run(string root)
    {
        int total=0;
        for(int orientation=0;orientation<8;orientation++){
            var g=TerrainCliffGeometry.For(orientation);bool diagonal=orientation>=4;Check(g.Triangles==(diagonal?14:18),"bounded cliff triangle count");total+=g.Triangles;float highArea=0,lowArea=0;
            for(int i=0;i<g.VertexCount;i++){
                var p=P(i);Check(p.X>=0&&p.X<=2&&p.Z>=0&&p.Z<=2&&p.Y>=0&&p.Y<=2,"section stays inside one cube");
                Check(Math.Abs(N(i).Length()-1)<.0001f,"flat normals normalized");Check(g.Uvs[i*2]>=0&&g.Uvs[i*2]<=1&&g.Uvs[i*2+1]>=0&&g.Uvs[i*2+1]<=1,"UVs bounded, including stacked soil charts");
            }
            for(int t=0;t<g.Triangles;t++){
                int ia=g.Indices[t*3],ib=g.Indices[t*3+1],ic=g.Indices[t*3+2];var a=P(ia);var b=P(ib);var c=P(ic);var cross=Vector3.Cross(b-a,c-a);Check(Vector3.Dot(cross,N(ia))>.00001f,"outward non-degenerate triangles");
                int part=g.Parts[t];if(part==TerrainCliffGeometry.Top||part==TerrainCliffGeometry.Low){
                    float h=part==TerrainCliffGeometry.Top?2:0;Check(a.Y==h&&b.Y==h&&c.Y==h,"horizontal cap has constant height, never a ramp");
                    if(part==TerrainCliffGeometry.Top)highArea+=cross.Y*.5f;else lowArea+=cross.Y*.5f;
                    foreach(int i in new[]{ia,ib,ic})Check(g.Uvs[i*2]==g.Positions[i*3]*.5f&&g.Uvs[i*2+1]==g.Positions[i*3+2]*.5f,"ground UVs stay in original tile orientation");
                    var center=(a+b+c)/3;Check(TerrainCliffGeometry.Raised(orientation,center.X,center.Z)==(h==2),"diagonal matches named low half");
                }else{Check(N(ia).Y==0&&N(ib).Y==0&&N(ic).Y==0,"all cliff faces vertical");if(part==TerrainCliffGeometry.Lip)Check(Math.Min(a.Y,Math.Min(b.Y,c.Y))>=2-TerrainCliffGeometry.LipDepth,"lip only covers narrow upper edge");}
            }
            Check(Math.Abs(highArea-(diagonal?2:4))<.001f&&Math.Abs(lowArea-(diagonal?2:0))<.001f,"high plus low cover complete square without overlap");
            string file=Path.Combine(root,"assets/models/terrain/cliffs/cliff_"+TerrainCliffGeometry.Name(orientation)+"_2m.glb");byte[] glb=File.ReadAllBytes(file);int jsonLength=BinaryPrimitives.ReadInt32LittleEndian(glb.AsSpan(12));using var doc=JsonDocument.Parse(glb.AsMemory(20,jsonLength));var j=doc.RootElement;
            Check(j.GetProperty("images").GetArrayLength()==1&&j.GetProperty("images")[0].GetProperty("uri").GetString()=="../../../textures/environment/cliffs/cliff-soil.png","one shared external soil texture; no embedded grass copy");
            Check(j.GetProperty("materials")[0].GetProperty("extras").GetProperty("animaltownBinding").GetString()=="neighbor-upper-ground","upper neighbor material binding retained in export");
            Check(j.GetProperty("materials")[2].GetProperty("extras").GetProperty("animaltownBinding").GetString()=="neighbor-lower-ground","lower neighbor material binding independent of upper");
            Check(!j.GetProperty("materials")[0].GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture",out _)&&!j.GetProperty("materials")[2].GetProperty("pbrMetallicRoughness").TryGetProperty("baseColorTexture",out _),"ground bindings allocate no textures");
            int lowerIndices=0;foreach(var primitive in j.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())if(primitive.GetProperty("material").GetInt32()==2)lowerIndices+=j.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32();
            Check(lowerIndices==(diagonal?3:0),"only low triangle uses lower binding");
            int indices=0;foreach(var primitive in j.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())indices+=j.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32();Check(indices==g.IndexCount,"export matches runtime geometry");
            Vector3 P(int i)=>new(g.Positions[i*3],g.Positions[i*3+1],g.Positions[i*3+2]);Vector3 N(int i)=>new(g.Normals[i*3],g.Normals[i*3+1],g.Normals[i*3+2]);
        }
        Check(total==128&&ReferenceEquals(TerrainCliffGeometry.For(0),TerrainCliffGeometry.For(3)),"eight orientations share cardinal template and total 128 triangles");
        var town=new Town(7319);Array.Clear(town.Elevation);int ox=40,oz=40;
        for(int orientation=4;orientation<8;orientation++){
            int upper=-1,lower=-1;
            for(int edge=0;edge<4;edge++){
                float px=edge==1?1.99f:edge==3?.01f:1,pz=edge==0?.01f:edge==2?1.99f:1;
                int x=ox+(edge==1?2:edge==3?-1:1),z=oz+(edge==0?-1:edge==2?2:1),cell=z*Town.Width+x;
                bool raised=TerrainCliffGeometry.Raised(orientation,px,pz);town.Elevation[cell]=raised?TerrainElevation.Block:TerrainElevation.Flat;
                town.Tiles[cell]=(byte)(raised?Terrain.Grass:Terrain.Beach);
                if(raised&&upper<0)upper=cell;if(!raised&&lower<0)lower=cell;
            }
            Check(TerrainCliffBinding.Neighbor(town,orientation,ox,oz,true)==upper&&TerrainCliffBinding.Neighbor(town,orientation,ox,oz,false)==lower,"all diagonal orientations bind direct neighbors at their own elevation");
            Check(town.At(upper%Town.Width,upper/Town.Width)!=(int)Terrain.Beach&&town.At(lower%Town.Width,lower/Town.Width)==(int)Terrain.Beach,"grass top and sand low remain independent");
            foreach(bool raised in new[]{false,true}){
                int chosen=TerrainCliffBinding.Neighbor(town,orientation,ox,oz,raised);
                town.Elevation[chosen]=raised?TerrainElevation.Flat:TerrainElevation.Block;
                int next=TerrainCliffBinding.Neighbor(town,orientation,ox,oz,raised);
                Check(next!=chosen,"wrong elevation neighbor skipped");town.Elevation[chosen]=raised?TerrainElevation.Block:TerrainElevation.Flat;
            }
            TerrainCliffBinding.PreviewOrigin(60,60,orientation,out int gx,out int gz);
            Check(TerrainCliffBinding.PreviewLowNeighbor(town,60,60,gx,gz)==TerrainCliffBinding.Neighbor(town,orientation,gx,gz,false),"ordinary low floor uses same neighbor binding as exported low cap");
        }
        Check(TerrainCliffBinding.PreviewLowNeighbor(town,60,60,0,0)==-1,"preview leaves unrelated ground untouched");
        int border=TerrainCliffBinding.Neighbor(town,4,-2,-2,true);Check(border>=0&&border<Town.Cells,"world-edge fallback remains bounded");
        var state=new GameState(7319){X=60,Z=60};Array.Clear(state.Town.Elevation);Array.Clear(state.Town.Tiles);
        TerrainCliffBinding.PreviewOrigin(60,60,4,out int previewX,out int previewZ);
        state.Town.Tiles[(previewZ-1)*Town.Width+previewX+1]=(byte)Terrain.Beach;
        byte[] metadata=new byte[FloorStrips.Records];Put(0,0x32525441);Put(4,1);Put(8,TerrainTiles.SurfaceCount);Put(12,TerrainTiles.SurfaceCount);Put(16,FloorTexture.Maximum);
        for(int s=0;s<TerrainTiles.SurfaceCount;s++){Put(20+s*20,1);Put(24+s*20,s);for(int v=1;v<4;v++)Put(24+s*20+v*4,-1);}FloorStrips.Validate(metadata);
        var strips=new FloorStrips();strips.Prepare(state,0,metadata,true);Check(SlotAt(previewX,previewZ)==TerrainTiles.Surface((int)Terrain.Beach),"low gallery floor inherits adjacent sand instead of original grass");
        strips.Prepare(state,0,metadata,false);Check(SlotAt(previewX,previewZ)==TerrainTiles.Surface((int)Terrain.Grass),"closing gallery restores ordinary floor materials");
        void Put(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(metadata.AsSpan(at),value);
        int SlotAt(int x,int z){for(int i=0;i<strips.Count;i++)if(x>=strips.X0[i]&&x<strips.X1[i]&&z>=strips.Z0[i]&&z<strips.Z1[i])return strips.Material[i];return -1;}
        foreach(int size in new[]{128,256}){using var image=SKBitmap.Decode(Path.Combine(root,$"assets/textures/environment/cliffs/cliff-soil-{size}.png"));Check(image!=null&&image.Width==size&&image.Height==size,"power-of-two soil delivery size");for(int y=0;y<size;y++)Check(image!.GetPixel(0,y)==image.GetPixel(size-1,y),"horizontal texture seam matched exactly");}
        using(var image=SKBitmap.Decode(Path.Combine(root,"assets/textures/environment/cliffs/cliff-soil.png"))){
            var package=FloorTexture.Prepare(RepeatFloorAssets.Encode(image,new SKRectI(0,0,image.Width,image.Height)));Check(package.Length-32<=40960&&TexturePixels.Int(package,24)==2,"small native two-level soil allocation");
        }
        Console.WriteLine("Cliff asset checks passed: eight orientations, exact footprint/heights, winding, UV stacking, shared ground binding, seamless soil and texture budget");
        static void Check(bool condition,string message){if(!condition)throw new InvalidDataException("Cliff asset check failed: "+message);}
    }
}
