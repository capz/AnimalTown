using System;

namespace AnimalTown;

// Four inexpensive travelling waves in logical X/Z metres. Geometry uses
// positive displacement so a small optional patch remains above the immutable
// ocean and cannot depth-fight with its archived flat background.
public static class WindWakerWaveField
{
    public const int Cells=16;
    public const float CellSize=.5f,BaseLift=.012f;
    public const float Extent=Cells*CellSize;
    private const double Tau=6.283185307179586;

    public static float EdgeWeight(int x,int z)
    {
        if((uint)x>Cells||(uint)z>Cells)return 0;
        int d=Math.Min(Math.Min(x,Cells-x),Math.Min(z,Cells-z));
        return Math.Min(1f,d/3f);
    }
    public static float Height(float x,float z,double seconds,float edgeWeight)
    {
        // Periods chosen for visual motion rather than physical scale; the
        // directional ratios and multi-component superposition are drawn from
        // the original GameCube sea-height architecture.
        double w0=Math.Cos(1.20*(.98*x+.20*z)-seconds*.72);
        double w1=Math.Cos(1.48*(.20*x+.98*z)-seconds*.65);
        double w2=Math.Cos(1.82*(-.98*x+.20*z)-seconds*.77);
        double w3=Math.Cos(2.13*(.20*x-.98*z)-seconds*.70);
        double height=.11+.022*(w0+w1+w2+w3);
        return BaseLift+Math.Max(0,Math.Min(1,edgeWeight))*(float)height;
    }
}
