namespace AnimalTown;

// Maximum edge length; rectangular sources retain their aspect ratio.
public enum TextureResolution { Size128=128, Size256=256, Size512=512, Size1024=1024 }
public static class TexturePolicy
{
    public const TextureResolution Default=TextureResolution.Size256;
    public static int Edge(TextureResolution resolution)
    {
        int edge=(int)resolution;
        if(edge!=128&&edge!=256&&edge!=512&&edge!=1024)throw new ArgumentOutOfRangeException("resolution");
        return edge;
    }
    public static string ModelPath(string name,TextureResolution resolution=Default)
    {int edge=Edge(resolution);return "dvd:/"+name+(edge==256?"":"-tex"+edge)+".dndmodel";}
}
