using AnimalTown;
internal static class CloudShadowChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var mask=CloudShadowPattern.Texture();check(mask.Length==4096,"shared cloud I8 mask is four KiB");
        int at=0,min=255,max=0;
        for(int z=0;z<64;z+=4)for(int x=0;x<64;x+=8)for(int dz=0;dz<4;dz++)for(int dx=0;dx<8;dx++){
            int px=x+dx,pz=z+dz,v=mask[at++];min=Math.Min(min,v);max=Math.Max(max,v);
            check(v==CloudShadowPattern.Sample(px,pz)&&v==CloudShadowPattern.Sample(px-64,pz+128),"GX packing and repeating cloud coordinates agree");
            check(Math.Abs(v-CloudShadowPattern.Sample(px+1,pz))<65&&Math.Abs(v-CloudShadowPattern.Sample(px,pz+1))<65,"cloud edges remain soft including repeat seams");
        }
        check(min<10&&max>245,"cloud mask has both sunlit and shaded patches");
        check(CloudShadowPattern.Shade(0)==255&&CloudShadowPattern.Shade(1)==255&&CloudShadowPattern.Shade(.5f)<230,"clear and overcast skies suppress patch contrast");
        check(CloudShadowPattern.Ambient(0)==255&&CloudShadowPattern.Ambient(1)<CloudShadowPattern.Ambient(.5f),"overcast ambient tint darkens without cumulative multiplication");
        check(CloudShadowPattern.Shade(-1)==CloudShadowPattern.Shade(0)&&CloudShadowPattern.Shade(2)==CloudShadowPattern.Shade(1),"cover controls clamp to valid range");
        foreach(double seconds in new[]{-1e8,-1,0,1,86400,1e8})foreach(float speed in new[]{CloudShadowPattern.SpeedX,CloudShadowPattern.SpeedZ}){
            float a=CloudShadowPattern.Offset(seconds,speed),b=CloudShadowPattern.Offset(seconds+.01,speed);float delta=b-a;delta-=(float)Math.Round(delta);
            check(a>=0&&a<1&&Math.Abs(delta-speed*.01/128)<.000001,"cloud scrolling is bounded and continuous across long/negative sessions");
        }
    }
}
