using Dolphin.Graphics;

internal static class ModelResources
{
    // Variants are compiled from the original GLB; only the requested one enters
    // RAM. Never upscale an already reduced texture to satisfy a larger option.
    internal static System.Threading.Tasks.Task<Model> LoadAsync(string name,AnimalTown.TextureResolution resolution=AnimalTown.TexturePolicy.Default)
    {
        int edge=AnimalTown.TexturePolicy.Edge(resolution);
        int budget=edge<=256?3*1024*1024:edge==512?8*1024*1024:16*1024*1024;
        return Model.LoadAsync(AnimalTown.TexturePolicy.ModelPath(name,resolution),default,budget);
    }
}
