using Dolphin.Graphics;

internal static class ModelResources
{
    internal const int DefaultBudget=AnimalTown.ModelMemoryPlan.MaximumSharedBytes;
    internal static System.Threading.Tasks.Task<Model> LoadLodAsync(string name,int lod,System.Threading.CancellationToken token=default)
    {
        if(lod==0)return Model.LoadAsync(AnimalTown.TexturePolicy.ModelPath(name,AnimalTown.TextureResolution.Size128),token,DefaultBudget);
        if(lod!=1||!AnimalTown.TexturePolicy.IsVillagerModel(name))throw new System.ArgumentOutOfRangeException("lod");
        return Model.LoadAsync(AnimalTown.ContentPaths.Root+name+"-lod1-tex128.dndmodel",token,DefaultBudget);
    }
    // Variants are compiled from the original GLB; only the requested one enters
    // RAM. Never upscale an already reduced texture to satisfy a larger option.
    internal static System.Threading.Tasks.Task<Model> LoadAsync(string name)=>LoadAsync(name,AnimalTown.TexturePolicy.DefaultForModel(name));
    internal static System.Threading.Tasks.Task<Model> LoadAsync(string name,AnimalTown.TextureResolution resolution)
    {
        int edge=AnimalTown.TexturePolicy.ModelEdge(name,resolution);
        int budget=edge<=256?DefaultBudget:edge==512?8*1024*1024:16*1024*1024;
        return Model.LoadAsync(AnimalTown.TexturePolicy.ModelPath(name,resolution),default,budget);
    }
}
