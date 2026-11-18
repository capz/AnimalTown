namespace AnimalTown;

// Maximum edge length; rectangular sources retain their aspect ratio.
public enum TextureResolution { Size128=128, Size256=256, Size512=512, Size1024=1024 }
public static class TexturePolicy
{
    public const TextureResolution Default=TextureResolution.Size256;
    public const TextureResolution VillagerDefault=TextureResolution.Size128;
    public static bool IsVillagerModel(string name)=>name is "fox" or "bunny" or "giraffe" or "cat" or "hamster" or "spaniel";
    public static TextureResolution DefaultForModel(string name)=>IsVillagerModel(name)?VillagerDefault:Default;
    public static int Edge(TextureResolution resolution)
    {
        int edge=(int)resolution;
        if(edge!=128&&edge!=256&&edge!=512&&edge!=1024)throw new ArgumentOutOfRangeException("resolution");
        return edge;
    }
    public static int ModelEdge(string name,TextureResolution resolution)
    {
        int edge=Edge(resolution);
        if(IsVillagerModel(name)&&edge>256)throw new ArgumentOutOfRangeException("resolution","Villager models support only 128px and 256px textures");
        return edge;
    }
    public static string ModelPath(string name)=>ModelPath(name,DefaultForModel(name));
    public static string ModelPath(string name,TextureResolution resolution)
    {int edge=ModelEdge(name,resolution);return ContentPaths.Root+name+(edge==256?"":"-tex"+edge)+".dndmodel";}
}
