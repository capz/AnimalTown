internal static class UiTextLayoutChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var advances=new float[96];for(int i=0;i<advances.Length;i++)advances[i]=3+i%17;
        var cache=new UiTextLayoutCache();
        foreach(bool firmware in new[]{false,true}){
            cache=new UiTextLayoutCache();
            float height=firmware?24:64;
            foreach(string text in new[]{"","AnimalTown"," A\tB\r\nC\n\nD ","\u00e9\u007f?",new string('W',128),new string('\n',128)}){
                int slot=cache.Find(text,advances,height,firmware,out _);
                check(slot>=0,"bounded integral font layouts retain their cache entries");
                var expected=Reference(text,advances,height,firmware);
                for(int i=0;i<text.Length;i++)check(cache.X(slot,i)==expected.X[i]&&cache.Y(slot,i)==expected.Y[i],"packed layout preserves exact glyph offsets, multiline spacing, tabs and unsupported characters");
                check(cache.Width(slot)==expected.Width,"packed layout preserves exact measured width and rotation pivot");
                cache.Find(text,advances,height,firmware,out bool changed);
                check(!changed,"an unchanged layout keeps its cached quad ordering");
            }
        }
        check(cache.Find(new string('W',129),advances,24,true,out _)==-1,"long text retains the uncached rendering path");

        // Exercise direct-map replacement with two different strings that share
        // a hash slot, including invalidation of the old page-order cache.
        const string first="First label";string collision="";
        for(int i=0;i<10000;i++){string value="Second "+i;if(Hash(value)==Hash(first)){collision=value;break;}}
        check(collision.Length>0,"fixture includes a real direct-map collision");
        int before=cache.Find(first,advances,24,true,out _),after=cache.Find(collision,advances,24,true,out bool replaced);
        check(before==after&&replaced,"colliding text replaces only its slot and invalidates cached quad order");
        int restored=cache.Find(first,advances,24,true,out replaced);
        check(restored==before&&replaced&&cache.Width(restored)==Reference(first,advances,24,true).Width,"evicted text rebuilds its exact prior layout");

        var unusual=(float[])advances.Clone();
        foreach(float value in new[]{40000f,1.5f,-1f,float.NaN,float.PositiveInfinity}){
            unusual['a'-32]=value;
            check(cache.Find("aaa",unusual,24,true,out _)==-1,"large, fractional or invalid offsets fall back instead of truncating geometry");
            cache.Find("aaa",advances,24,true,out bool changed);
            check(changed,"a rejected partial replacement cannot create a stale cache hit");
            // Force a different text into the slot before testing another metric.
            cache=new UiTextLayoutCache();
        }
        check(cache.Find("a\na\na",advances,40000,true,out _)==-1,"oversized multiline offsets use uncached drawing");
        check(UiTextLayoutCache.CoordinatePayloadBytes==32768,"two font coordinate streams retain 32 KiB instead of 64 KiB");

        cache.Find("Warm cache label",advances,24,true,out _);
        for(int i=0;i<100;i++)cache.Find("Warm cache label",advances,24,true,out _);
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int i=0;i<1000;i++)cache.Find("Warm cache label",advances,24,true,out _);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"warmed layout hits allocate no frame-time managed memory");

        // Atlas placement determines region count; headings can cross both
        // axes while IPL glyphs use their single 32-pixel cell.
        var metrics=new byte[784];
        Set(metrics,16,127);Set(metrics,18,127);Set(metrics,20,2);
        check(UiTextLayoutCache.GlyphRegionStride(metrics)==4,"a glyph crossing both page axes retains all four slices");
        Set(metrics,16,0);Set(metrics,18,0);Set(metrics,20,32);
        check(UiTextLayoutCache.GlyphRegionStride(metrics)==1,"single-page glyphs do not reserve eight slices");
        Set(metrics,18,127);Set(metrics,20,1024);
        bool rejected=false;try{UiTextLayoutCache.GlyphRegionStride(metrics);}catch(System.IO.IOException){rejected=true;}
        check(rejected,"atlas glyphs exceeding the original eight-region limit remain rejected");
    }
    private static (float[] X,float[] Y,float Width) Reference(string text,float[] advances,float height,bool firmware)
    {
        // Original float layout equations, independent of the compact cache.
        var x=new float[text.Length];var y=new float[text.Length];float pen=0,line=0,width=0;
        for(int i=0;i<text.Length;i++){
            x[i]=pen;y[i]=line;char c=text[i];
            if(c=='\r')continue;
            if(c=='\n'){width=Math.Max(width,pen);pen=0;line+=height;continue;}
            int glyph=c>=32&&c<128?c-32:'?'-32;pen+=firmware&&c=='\t'?advances[0]*4:advances[glyph];
        }
        return (x,y,Math.Max(width,pen));
    }
    private static uint Hash(string text){uint value=2166136261;foreach(char c in text)value=unchecked((value^c)*16777619);return value&63;}
    private static void Set(byte[] data,int at,int value){data[at]=(byte)value;data[at+1]=(byte)(value>>8);}
}
