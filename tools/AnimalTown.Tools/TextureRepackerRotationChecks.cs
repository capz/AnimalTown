using System.Text.Json.Nodes;
using DolphinDotNet.Imaging;

internal static class TextureRepackerRotationChecks
{
    internal static int Run(string folder)
    {
        int checks=0;
        foreach(int turn in new[]{0,1,2,3}){
            using var bin=new MemoryStream();var views=new JsonArray();var accessors=new JsonArray();
            int View(byte[] bytes){while(bin.Length%4!=0)bin.WriteByte(0);int id=views.Count;views.Add(new JsonObject{["buffer"]=0,["byteOffset"]=(int)bin.Length,["byteLength"]=bytes.Length});bin.Write(bytes);return id;}
            int Access(float[] values,string type,int count){byte[] bytes=new byte[values.Length*4];for(int i=0;i<values.Length;i++)BitConverter.GetBytes(values[i]).CopyTo(bytes,i*4);int id=accessors.Count;accessors.Add(new JsonObject{["bufferView"]=View(bytes),["componentType"]=5126,["type"]=type,["count"]=count});return id;}
            int width=turn==0?16:32,height=turn==0?32:16,atlasWidth=turn==0?64:128,bx=turn==0?40:72,by=turn==0?70:8;
            using var image=new RasterImage(atlasWidth,128,new Rgba32(255,255,255,255));
            for(int y=0;y<height;y++)for(int x=0;x<width;x++){
                var p=new Rgba32((byte)(x*7),(byte)(y*13),(byte)((x+y)*5),255);image[8+x,8+y]=p;
                int rx=turn switch{0=>x,1=>height-1-y,2=>width-1-x,_=>y},ry=turn switch{0=>y,1=>x,2=>height-1-y,_=>width-1-x};image[bx+rx,by+ry]=p;
            }
            using var png=new MemoryStream();image.SaveAsPng(png);int imageView=View(png.ToArray());
            var uv=new List<float>();foreach(var p in new[]{(x:.5f,y:.5f),(x:width-.5f,y:.5f),(x:.5f,y:height-.5f)}){uv.Add((8+p.x)/atlasWidth);uv.Add((8+p.y)/128);}
            foreach(var p in new[]{(x:.5f,y:.5f),(x:width-.5f,y:.5f),(x:.5f,y:height-.5f)}){float x=turn switch{0=>p.x,1=>height-p.y,2=>width-p.x,_=>p.y},y=turn switch{0=>p.y,1=>p.x,2=>height-p.y,_=>width-p.x};uv.Add((bx+x)/atlasWidth);uv.Add((by+y)/128);}
            int pos=Access(new float[]{0,0,0,1,0,0,0,1,0,2,0,0,3,0,0,2,1,0},"VEC3",6),tex=Access(uv.ToArray(),"VEC2",6);
            var json=JsonNode.Parse("""{"asset":{"version":"2.0"},"buffers":[{"byteLength":_BIN_}],"images":[{"bufferView":_IMAGE_,"mimeType":"image/png"}],"textures":[{"source":0,"sampler":0}],"samplers":[{"wrapS":33071,"wrapT":33071}],"materials":[{"pbrMetallicRoughness":{"baseColorTexture":{"index":0}}}],"meshes":[{"primitives":[{"attributes":{"POSITION":_POS_,"TEXCOORD_0":_TEX_},"material":0}]}],"nodes":[{"mesh":0}],"scenes":[{"nodes":[0]}],"scene":0}""".Replace("_BIN_",bin.Length.ToString()).Replace("_IMAGE_",imageView.ToString()).Replace("_POS_",pos.ToString()).Replace("_TEX_",tex.ToString()))!.AsObject();json["bufferViews"]=views;json["accessors"]=accessors;
            string source=Path.Combine(folder,$"rotation-{turn}.glb"),output=Path.Combine(folder,$"rotation-{turn}-packed.glb");File.WriteAllBytes(source,TextureRepacker.EncodeGlb(json,bin.ToArray()));TextureRepacker.Repack(source,output,turn==0?null:128,.01);
            var report=JsonNode.Parse(File.ReadAllText(output+".repack.json"))!["images"]![0]!;
            if((int)report["sharedCharts"]! !=1||(int)report["rotatedStacks"]! !=(turn==0?0:1)||(double)report["maxTriangleRmse"]! >=.01)throw new Exception("Rotated UV stacking failed for "+turn*90+" degrees");checks+=3;if(turn==0){if((int)report["packedRotations"]! <1)throw new Exception("Rectangle packer did not test quarter-turn placement");checks++;}
        }
        return checks;
    }
}
