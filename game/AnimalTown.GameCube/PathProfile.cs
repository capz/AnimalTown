using System;
using Dolphin.Diagnostics;
using Dolphin.Graphics;
using AnimalTown;

// Reuses the existing opt-in 240-frame research capture. No per-frame logging,
// arrays or memory queries during normal play; dumps follow its END timestamp.
internal static class PathProfile
{
    private const int Fields=5;
    private static int[]? rows;
    internal static void Record(int index,long[] time,int[] native)
    {
        if(index<0)return;
        if(rows==null)rows=new int[240*Fields];
        long cpu=time[15];for(int i=0;i<=9;i++)cpu+=time[i];cpu=Math.Max(0,cpu-time[10]-time[11]);
        int at=index*Fields;rows[at]=(int)cpu;rows[at+1]=(int)time[2];rows[at+2]=(int)time[5];rows[at+3]=native[0];rows[at+4]=native[7];
    }
    internal static void Flush(int index)
    {
        if(index!=239)return;
        DebugOverlay.WriteLine("PATH PROFILE MEMORY native "+GraphicsDevice.Default.NativeBytes+" texture "+TextureCache.ResidentBytes+" path "+TextureCache.SourceBytesContaining("path-dirt")+" aram "+NativeTerrain.Stat(5)+" cache_peak "+NativeTerrain.Stat(7)+" cache_reserved "+NativeTerrain.Stat(8)+" dma_reads "+NativeTerrain.Stat(6)+" bindings "+NativeTerrain.Stat(4));
        for(int i=0;i<240;i++){int at=i*Fields;DebugOverlay.WriteLine("PATH PROFILE ROW "+i+" "+rows![at]+" "+rows[at+1]+" "+rows[at+2]+" "+rows[at+3]+" "+rows[at+4]);}
        DebugOverlay.WriteLine("PATH PROFILE DONE");
    }
}
