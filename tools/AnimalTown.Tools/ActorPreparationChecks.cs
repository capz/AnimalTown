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
        string[] names={"Meshy_AI_Rigged_Fox_All_Animations","villager_bunny","villager_giraffe","villager_mainecoon","villager_hamster","villager_cockerspaniel","villager_penguin","villager_whitetiger","villager_lion","villager_robin","villager_raccoon"};
        JsonNode Read(byte[] bytes)=>JsonNode.Parse(bytes.AsSpan(20,BitConverter.ToInt32(bytes,12)))!;
        void Check(bool value,string reason){if(!value)throw new InvalidDataException(reason);}
        foreach(string name in names){
            string input=Path.Combine(root,"assets/models/villagers",name+".glb");byte[] original=File.ReadAllBytes(input);var source=Read(original);
            string normal=Path.Combine(output,name+"-all.glb"),game=Path.Combine(output,name+"-game.glb");
            Program.Prepare(input,normal);Program.Prepare(input,game,true);
            var full=Read(File.ReadAllBytes(normal));byte[] cooked=File.ReadAllBytes(game);var selected=Read(cooked);
            Check(JsonNode.DeepEquals(source["animations"],full["animations"]),name+": default preparation retains every supplied clip");
            var animations=source["animations"] as JsonArray??new JsonArray();
            var idle=animations.FirstOrDefault(a=>(string?)a?["name"]=="Idle")??animations.FirstOrDefault(a=>((string?)a?["name"])?.StartsWith("Idle_",StringComparison.Ordinal)==true);
            var expected=animations.Where(a=>ReferenceEquals(a,idle)||(string?)a!["name"]=="Walking"||(string?)a!["name"]=="Running").ToArray();
            var actual=selected["animations"] as JsonArray;
            Check((actual?.Count??0)==expected.Length,name+": gameplay retains precisely the movement and selected idle clips");
            for(int i=0;i<expected.Length;i++){
                var before=expected[i]!;var after=actual![i]!;
                Check((ReferenceEquals(before,idle)?"Idle":(string?)before["name"])==(string?)after["name"]&&JsonNode.DeepEquals(before["channels"],after["channels"]),name+": movement channels and ordering are exact");
                var samplers=before["samplers"]!.AsArray();var packed=after["samplers"]!.AsArray();
                Check(samplers.Count==packed.Count,name+": all movement samplers survive");
                for(int j=0;j<samplers.Count;j++){
                    var a=samplers[j]!;var b=packed[j]!;
                    Check((string?)a["interpolation"]==(string?)b["interpolation"],name+": interpolation is unchanged");
                    if(JsonNode.DeepEquals(a,b))continue;
                    var times=full["accessors"]![(int)a["input"]!]!;var values=full["accessors"]![(int)a["output"]!]!;
                    var pt=selected["accessors"]![(int)b["input"]!]!;var pv=selected["accessors"]![(int)b["output"]!]!;
                    int keys=(int)times["count"]!,width=(string?)values["type"]=="VEC4"?16:12;
                    int kept=(int)pt["count"]!;
                    Check(kept>0&&kept<=keys&&(int)pv["count"]! == kept,name+": compressed samplers remain bounded and paired");
                    Check(Element(full,original,times,keys-1,4).SequenceEqual(Element(selected,cooked,pt,kept-1,4)),name+": final key time and clip duration survive");
                    if(kept>1)Check(Element(full,original,times,0,4).SequenceEqual(Element(selected,cooked,pt,0,4)),name+": animated track starts at its original first key");
                    int sourceKey=0;
                    for(int key=0;key<kept;key++){
                        byte[] time=Element(selected,cooked,pt,key,4);
                        while(sourceKey<keys&&!Element(full,original,times,sourceKey,4).SequenceEqual(time))sourceKey++;
                        Check(sourceKey<keys,name+": retained times are original ordered samples");
                        Check(Element(full,original,values,sourceKey,width).SequenceEqual(Element(selected,cooked,pv,key,width)),name+": retained sample values are bit-exact");
                    }
                }
            }
            var originalAccessors=full["accessors"]!.AsArray();
            for(int i=0;i<originalAccessors.Count;i++)Check(JsonNode.DeepEquals(originalAccessors[i],selected["accessors"]![i]),name+": original accessors remain exact");
            foreach(var texture in selected["textures"]!.AsArray())Check(full["textures"]!.AsArray().Any(t=>JsonNode.DeepEquals(t,texture)),name+": retained GX texture references are exact");
            var originalViews=full["bufferViews"]!.AsArray();
            for(int i=0;i<originalViews.Count;i++)Check(JsonNode.DeepEquals(originalViews[i],selected["bufferViews"]![i]),name+": original buffer views remain exact");
            foreach(string field in new[]{"meshes","skins","nodes","images"})
                Check(JsonNode.DeepEquals(full[field],selected[field]),name+": gameplay filtering preserves "+field);
            int originalChunk=20+BitConverter.ToInt32(original,12),cookedChunk=20+BitConverter.ToInt32(cooked,12),binaryBytes=BitConverter.ToInt32(original,originalChunk);
            Check(original.AsSpan(originalChunk+8,binaryBytes).SequenceEqual(cooked.AsSpan(cookedChunk+8,binaryBytes)),name+": original geometry, texture and animation bytes remain exact before appended runtime samples");
            Check(original.SequenceEqual(File.ReadAllBytes(input)),name+": authoring GLB is unchanged");
        }
        Console.WriteLine("Actor preparation: eleven sources, movement/idle clips, retained samples and durations, source/rig/UV/texture/binary preservation passed");
    }
}
