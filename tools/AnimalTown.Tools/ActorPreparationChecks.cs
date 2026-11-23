using System.Text.Json.Nodes;

internal static class ActorPreparationChecks
{
    private static byte[] Element(JsonNode model,byte[] glb,JsonNode accessor,int index,int width)
    {
        var view=model["bufferViews"]![(int)accessor["bufferView"]!]!;
        int binary=28+BitConverter.ToInt32(glb,12);
        int offset=((int?)view["byteOffset"]??0)+((int?)accessor["byteOffset"]??0)+index*((int?)view["byteStride"]??width);
        return glb.AsSpan(binary+offset,width).ToArray();
    }
    internal static void Run(string root)
    {
        string output=Path.Combine(root,"out/actor-preparation-checks");Directory.CreateDirectory(output);
        string[] names={"Meshy_AI_Rigged_Fox_All_Animations","villager_bunny","villager_giraffe","villager_mainecoon","villager_hamster","villager_cockerspaniel"};
        JsonNode Read(byte[] bytes)=>JsonNode.Parse(bytes.AsSpan(20,BitConverter.ToInt32(bytes,12)))!;
        void Check(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
        foreach(string name in names){
            string input=Path.Combine(root,"assets/models/villagers",name+".glb");byte[] original=File.ReadAllBytes(input);var source=Read(original);
            string normal=Path.Combine(output,name+"-all.glb"),game=Path.Combine(output,name+"-game.glb");
            Program.Prepare(input,normal);Program.Prepare(input,game,true);
            var full=Read(File.ReadAllBytes(normal));byte[] cooked=File.ReadAllBytes(game);var selected=Read(cooked);
            Check(JsonNode.DeepEquals(source["animations"],full["animations"]),name+": default preparation retains every supplied clip");
            var expected=(source["animations"] as JsonArray??new JsonArray()).Where(a=>(string?)a!["name"]=="Walking"||(string?)a!["name"]=="Running").ToArray();
            var actual=selected["animations"] as JsonArray;
            Check((actual?.Count??0)==expected.Length,name+": gameplay retains precisely the movement clips");
            for(int i=0;i<expected.Length;i++){
                var before=expected[i]!;var after=actual![i]!;
                Check((string?)before["name"]==(string?)after["name"]&&JsonNode.DeepEquals(before["channels"],after["channels"]),name+": movement channels and ordering are exact");
                var samplers=before["samplers"]!.AsArray();var packed=after["samplers"]!.AsArray();
                Check(samplers.Count==packed.Count,name+": all movement samplers survive");
                for(int j=0;j<samplers.Count;j++){
                    var a=samplers[j]!;var b=packed[j]!;
                    Check((string?)a["interpolation"]==(string?)b["interpolation"],name+": interpolation is unchanged");
                    if(JsonNode.DeepEquals(a,b))continue;
                    var times=full["accessors"]![(int)a["input"]!]!;var values=full["accessors"]![(int)a["output"]!]!;
                    var pt=selected["accessors"]![(int)b["input"]!]!;var pv=selected["accessors"]![(int)b["output"]!]!;
                    int keys=(int)times["count"]!,width=(string?)values["type"]=="VEC4"?16:12;
                    Check((int)pt["count"]! == 1&&(int)pv["count"]! == 1,name+": only constant tracks collapse");
                    byte[] sample=Element(selected,cooked,pv,0,width);
                    for(int key=0;key<keys;key++)Check(Element(full,original,values,key,width).SequenceEqual(sample),name+": every collapsed value is bit-exact");
                    Check(Element(full,original,times,keys-1,4).SequenceEqual(Element(selected,cooked,pt,0,4)),name+": constant track preserves its final key time and clip duration");
                }
            }
            var originalAccessors=full["accessors"]!.AsArray();
            for(int i=0;i<originalAccessors.Count;i++)Check(JsonNode.DeepEquals(originalAccessors[i],selected["accessors"]![i]),name+": original accessors remain exact");
            foreach(var texture in selected["textures"]!.AsArray())Check(full["textures"]!.AsArray().Any(t=>JsonNode.DeepEquals(t,texture)),name+": retained GX texture references are exact");
            foreach(string field in new[]{"meshes","skins","nodes","bufferViews","buffers","images"})
                Check(JsonNode.DeepEquals(full[field],selected[field]),name+": gameplay filtering preserves "+field);
            Check(original.AsSpan(20+BitConverter.ToInt32(original,12)).SequenceEqual(cooked.AsSpan(20+BitConverter.ToInt32(cooked,12))),name+": embedded geometry, texture and animation bytes are preserved");
            Check(original.SequenceEqual(File.ReadAllBytes(input)),name+": authoring GLB is unchanged");
        }
        Console.WriteLine("Actor preparation: six sources, exact movement tracks, full default clips, source/rig/UV/texture/binary preservation passed");
    }
}
