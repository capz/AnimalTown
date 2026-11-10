using AnimalTown;

internal static class PreparedFloors
{
    internal static void Build(string root)
    {
        string disc=Path.Combine(root,"out/disc");
        foreach(string source in Directory.GetFiles(disc,"*.dndtex").Order()){
            string name=Path.GetFileName(source);if(name.EndsWith("-native.dndtex",StringComparison.Ordinal)||(!name.StartsWith("floor-",StringComparison.Ordinal)&&name!="cliff-soil.dndtex"))continue;
            byte[] bytes=FloorTexture.Prepare(File.ReadAllBytes(source),fullMipChain:name!="cliff-soil.dndtex");
            File.WriteAllBytes(Path.Combine(disc,Path.GetFileNameWithoutExtension(source)+"-native.dndtex"),bytes);
        }
    }
}
