using System;
using System.IO;
using AnimalTown;
using Dolphin.Graphics;

// Runs only while constructing shared materials, never in the frame loop.
// The caller owns a successful texture; absence/corruption keeps its fallback.
internal static class WaterTextureAssets
{
    internal static Texture2D? TryLoad(string role)
    {
        byte[]? package=TryLoadPackage(role);
        if(package==null)return null;
        try{return Texture2D.FromPackage(package,true);}
        catch(Exception e){
            Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN WATER ART FALLBACK "+role+": "+e.Message);
            return null;
        }
    }
    internal static byte[]? TryLoadNativeData(string role)
    {
        byte[]? package=TryLoadPackage(role);
        if(package==null)return null;
        var pixels=new byte[package.Length-32];
        // The pinned console AOT subset does not implement Buffer.BlockCopy.
        for(int i=0;i<pixels.Length;i++)pixels[i]=package[i+32];
        return pixels;
    }
    private static byte[]? TryLoadPackage(string role)
    {
        int index=WaterTextureCatalog.IndexOfRole(role);
        if(index<0)throw new ArgumentException("Unknown water texture role: "+role);
        string path=ContentPaths.Asset(WaterTextureCatalog.DiscName(index));
        try{
            if(!File.Exists(path))return null;
            using var stream=File.OpenRead(path);
            int bytes=32+WaterTextureCatalog.NativeBytes(index);
            if(stream.Length!=bytes)throw new InvalidOperationException("Unexpected package length");
            var package=new byte[bytes];int read=0;
            while(read<bytes){int count=stream.Read(package,read,bytes-read);if(count<=0)throw new InvalidOperationException("Truncated water texture");read+=count;}
            WaterTextureCatalog.Validate(package,index);
            return package;
        }catch(Exception e){
            Dolphin.Diagnostics.DebugOverlay.WriteLine("ANIMALTOWN WATER ART FALLBACK "+role+": "+e.Message);
            return null;
        }
    }
}
