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
        check(CloudShadowPattern.Period(3)==CloudShadowPattern.Period(1)*2&&CloudShadowPattern.Strength(3)==CloudShadowPattern.Strength(1)*.5f,"winter stretches the shared mask twice as wide at half summer strength");
        check(CloudShadowPattern.EdgeWidth(1)<CloudShadowPattern.EdgeWidth(0)&&CloudShadowPattern.EdgeWidth(0)==CloudShadowPattern.EdgeWidth(2),"summer has the hardest edges while spring and autumn retain the original profile");
        foreach(int season in new[]{0,1,2,3})foreach(double seconds in new[]{-1e8,-1,0,1,86400,1e8})foreach(float speed in new[]{CloudShadowPattern.SpeedX,CloudShadowPattern.SpeedZ}){
            float period=CloudShadowPattern.Period(season),a=CloudShadowPattern.Offset(seconds,speed,period),b=CloudShadowPattern.Offset(seconds+.01,speed,period);float delta=b-a;delta-=(float)Math.Round(delta);
            check(a>=0&&a<1&&Math.Abs(delta*period-speed*.01)<.00003,"seasonal cloud scaling preserves bounded scrolling and world-space wind speed");
        }
    }
}
