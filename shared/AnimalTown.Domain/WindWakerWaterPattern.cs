using System;

namespace AnimalTown;

// Periodic, deterministic cellular foam with connected, irregular Voronoi
// boundaries. Generated once into a shared GX I8 mask; never evaluated on the
// render thread. Sites wrap around both edges and remain stable across seasons.
public static class WindWakerWaterPattern
{
    private const int Size=WaterSurface.TextureSize;
    private const int Sites=4;
    private const double Tau=6.283185307179586;

    // Low-discrepancy jitter gives broad, smoothly connected crests instead of
    // isolated repeating arcs or a recognizable regular Voronoi grid.
    private static double Hash(int x,int y,int axis)
    {
        uint n=unchecked((uint)(x*198491317+y*6542989+axis*357239));
        n^=n>>16;n*=0x7feb352du;n^=n>>15;n*=0x846ca68bu;n^=n>>16;
        return (n&65535)/65535.0;
    }

    public static byte Foam(int x,int y)
    {
        x=Mod(x);y=Mod(y);
        // The warp is itself periodic, so x/y wrapping never creates seams.
        double ux=x+2.2*Math.Sin(Tau*y/Size)+1.1*Math.Sin(Tau*(x+y)/Size);
        double uy=y+2.0*Math.Sin(Tau*x/Size)-1.3*Math.Cos(Tau*(y-x)/Size);
        double d0=double.MaxValue,d1=double.MaxValue;
        const double spacing=Size/(double)Sites;
        for(int row=0;row<Sites;row++)for(int col=0;col<Sites;col++)
        {
            double px=(col+.5+(Hash(col,row,0)-.5)*.52)*spacing;
            double py=(row+.5+(Hash(col,row,1)-.5)*.52)*spacing;
            double dx=WrapDistance(ux-px),dy=WrapDistance(uy-py);
            double dist=dx*dx+dy*dy;
            if(dist<d0){d1=d0;d0=dist;}
            else if(dist<d1)d1=dist;
        }
        double gap=Math.Sqrt(d1)-Math.Sqrt(d0);
        double edge=Math.Max(0,1-gap/2.2);
        // Asymmetry along a contour resembles separated whitecaps, while the
        // network topology stays continuous at lower intensity.
        double strength=.74+.26*Math.Sin(Tau*(2*x+y)/Size+1.4);
        return (byte)Math.Min(255,Math.Max(0,210*edge*edge*strength));
    }

    // Shore foam needs its alpha, not a dark/bright RGB mask. The pattern moves
    // inside fixed bank geometry; a positive floor avoids sharply flickering
    // coverage where alpha is modulated by the existing global shoreline pulse.
    public static byte ShoreAlpha(int x,int y)
    {
        x=Mod(x);y=Mod(y);
        double bands=.5+.5*Math.Cos(Tau*(y*3.0/Size+0.15*Math.Sin(Tau*x/Size)));
        double granular=.5+.5*Math.Sin(Tau*(x*5.0+y*2.0)/Size);
        return (byte)(80+175*bands*(.65+.35*granular));
    }
    private static int Mod(int v){v%=Size;return v<0?v+Size:v;}
    private static double WrapDistance(double d)
    {
        d-=Size*Math.Floor((d+Size*.5)/Size);
        return d;
    }
}
