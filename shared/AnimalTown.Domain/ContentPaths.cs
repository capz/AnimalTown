namespace AnimalTown;

// The SDK validates and canonicalizes the installation root before selection.
// Worker reads and cache keys retain one prefix for the entire application;
// device discovery never runs from a load or rendering loop.
public static class ContentPaths
{
    private static string root="dvd:/";
    private static bool selected;
    public static string Root=>root;
    public static void Select(string canonicalRoot)
    {
        if(selected)throw new InvalidOperationException("Content installation is already selected");
        if(string.IsNullOrEmpty(canonicalRoot)||canonicalRoot.IndexOf(":/",StringComparison.Ordinal)<1||
           !canonicalRoot.EndsWith("/",StringComparison.Ordinal))throw new ArgumentException("Invalid content root");
        root=canonicalRoot;selected=true;
    }
    public static string Asset(string filename)=>root+filename;
}
