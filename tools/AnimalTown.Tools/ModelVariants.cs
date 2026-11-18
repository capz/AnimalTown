using AnimalTown;

// Clean only generated disc staging. Run even when cooking is skipped, and
// again at ISO assembly so old large variants cannot survive an incremental build.
internal static class ModelVariants
{
    internal static int Prune(string root)
    {
        string disc=Path.Combine(root,"out/disc");int removed=0;long bytes=0;
        if(!Directory.Exists(disc))return 0;
        foreach(string path in Directory.GetFiles(disc,"*.dndmodel")){
            string name=Path.GetFileNameWithoutExtension(path);int marker=name.LastIndexOf("-tex",StringComparison.Ordinal);
            if(marker<0||!TexturePolicy.IsVillagerModel(name.Substring(0,marker))||name.Substring(marker+4) is not ("512" or "1024"))continue;
            bytes+=new FileInfo(path).Length;File.Delete(path);File.Delete(path+".deps.json");removed++;
        }
        Console.WriteLine($"Pruned {removed} oversized villager model variants ({bytes} bytes) from generated disc staging");
        return removed;
    }
}
