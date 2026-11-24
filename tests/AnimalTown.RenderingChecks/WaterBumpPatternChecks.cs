using AnimalTown;

internal static class WaterBumpPatternChecks
{
    internal static void Run(Action<bool,string> check)
    {
        byte[] texture=WaterBumpPattern.Texture();
        check(texture.Length==8192,"two 8-bit water slopes use exactly 8 KiB");
        check(WaterBumpPattern.Sample(0,0)==0x8080,"a flat derivative is exact signed zero on both GX axes");
        long xSum=0,zSum=0;int xMin=255,xMax=0,zMin=255,zMax=0;
        for(int z=0;z<64;z++)for(int x=0;x<64;x++){
            ushort value=WaterBumpPattern.Sample(x,z);
            int sx=value>>8,sz=value&255,at=((z/4)*16+x/4)*32+((z%4)*4+x%4)*2;
            check(texture[at]==sx&&texture[at+1]==sz,"GX alpha/intensity channel ordering preserves the independent derivatives");
            check(value==WaterBumpPattern.Sample(x-64,z+64),"slopes repeat without a seam in both directions");
            xSum+=sx-128;zSum+=sz-128;
            xMin=Math.Min(xMin,sx);xMax=Math.Max(xMax,sx);zMin=Math.Min(zMin,sz);zMax=Math.Max(zMax,sz);
            // Check the analytic vector against a separate finite-difference
            // height function. This catches swapped axes and reversed ridges.
            const double step=.00001;
            double u=x/64.0,v=z/64.0;
            double dx=-(Height(u+step,v)-Height(u-step,v))/(2*step*2*Math.PI);
            double dz=-(Height(u,v+step)-Height(u,v-step))/(2*step*2*Math.PI);
            check(Math.Abs(sx-(128+100*dx))<=.501&&Math.Abs(sz-(128+100*dz))<=.501,
                "both slope components are derivatives of one continuous water height field");
        }
        check(xSum==0&&zSum==0,"the periodic field has no average directional tilt");
        check(xMin<80&&xMax>176&&zMin<80&&zMax>176&&xMin>0&&xMax<255&&zMin>0&&zMax<255,
            "default waves use both signs without clipping either derivative");
        check(WaterTextureCatalog.NativeFormat(5)==3&&WaterTextureCatalog.NativeBytes(5)==texture.Length,
            "procedural and authored slopes share the same runtime representation");
    }
    private static double Height(double u,double v)=>.25*Math.Cos(2*Math.PI*(2*u+v))+
        .25*Math.Cos(2*Math.PI*(u-2*v))+.05*Math.Cos(2*Math.PI*(3*u+2*v));
}
