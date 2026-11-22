using System.Text.Json.Nodes;

internal static class ActorPreparationChecks
{
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
            for(int i=0;i<expected.Length;i++)Check(JsonNode.DeepEquals(expected[i],actual![i]),name+": movement track data and ordering are exact");
            foreach(string field in new[]{"meshes","skins","nodes","materials","accessors","bufferViews","buffers","images","textures"})
                Check(JsonNode.DeepEquals(full[field],selected[field]),name+": gameplay filtering preserves "+field);
            Check(original.AsSpan(20+BitConverter.ToInt32(original,12)).SequenceEqual(cooked.AsSpan(20+BitConverter.ToInt32(cooked,12))),name+": embedded geometry, texture and animation bytes are preserved");
            Check(original.SequenceEqual(File.ReadAllBytes(input)),name+": authoring GLB is unchanged");
        }
        Console.WriteLine("Actor preparation: six sources, exact movement tracks, full default clips, source/rig/UV/texture/binary preservation passed");
    }
}
