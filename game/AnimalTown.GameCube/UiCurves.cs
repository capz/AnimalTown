using System;

// Identical ten-segment corners and twenty-segment side bulges, evaluated once
// before frames begin instead of thousands of double-precision trig calls.
internal sealed class UiCurves
{
    internal readonly float[] Corners=new float[160],Bulge=new float[21];
    internal UiCurves()
    {
        for(int corner=0;corner<4;corner++){
            float start=corner==0?3.1415927f:corner==1?4.712389f:corner==2?0:1.5707964f;
            for(int i=0;i<10;i++){
                float a=start+i*.15707964f,b=a+.15707964f;int at=(corner*10+i)*4;
                Corners[at]=(float)Math.Cos(a);Corners[at+1]=(float)Math.Sin(a);
                Corners[at+2]=(float)Math.Cos(b);Corners[at+3]=(float)Math.Sin(b);
            }
        }
        for(int i=0;i<=20;i++)Bulge[i]=(float)Math.Sin(i/20f*3.1415927f);
    }
}
