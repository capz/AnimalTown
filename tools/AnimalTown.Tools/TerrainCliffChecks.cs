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
            Check(j.GetProperty("materials")[0].GetProperty("extras").GetProperty("animaltownBinding").GetString()=="surrounding-ground","ground material binding retained in export");
            int indices=0;foreach(var primitive in j.GetProperty("meshes")[0].GetProperty("primitives").EnumerateArray())indices+=j.GetProperty("accessors")[primitive.GetProperty("indices").GetInt32()].GetProperty("count").GetInt32();Check(indices==g.IndexCount,"export matches runtime geometry");
            Vector3 P(int i)=>new(g.Positions[i*3],g.Positions[i*3+1],g.Positions[i*3+2]);Vector3 N(int i)=>new(g.Normals[i*3],g.Normals[i*3+1],g.Normals[i*3+2]);
        }
        Check(total==128&&ReferenceEquals(TerrainCliffGeometry.For(0),TerrainCliffGeometry.For(3)),"eight orientations share cardinal template and total 128 triangles");
        foreach(int size in new[]{128,256}){using var image=SKBitmap.Decode(Path.Combine(root,$"assets/textures/environment/cliffs/cliff-soil-{size}.png"));Check(image!=null&&image.Width==size&&image.Height==size,"power-of-two soil delivery size");for(int y=0;y<size;y++)Check(image!.GetPixel(0,y)==image.GetPixel(size-1,y),"horizontal texture seam matched exactly");}
        using(var image=SKBitmap.Decode(Path.Combine(root,"assets/textures/environment/cliffs/cliff-soil.png"))){
            var package=FloorTexture.Prepare(RepeatFloorAssets.Encode(image,new SKRectI(0,0,image.Width,image.Height)));Check(package.Length-32<=40960&&TexturePixels.Int(package,24)==2,"small native two-level soil allocation");
        }
        Console.WriteLine("Cliff asset checks passed: eight orientations, exact footprint/heights, winding, UV stacking, shared ground binding, seamless soil and texture budget");
        static void Check(bool condition,string message){if(!condition)throw new InvalidDataException("Cliff asset check failed: "+message);}
    }
}
