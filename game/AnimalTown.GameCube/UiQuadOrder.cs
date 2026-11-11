using System;

// Reorder only disjoint quads within one text draw. Every overlap retains its
// painter order, including antialiased/rotated glyphs and multipage headings.
// Fixed caller-owned scratch avoids allocations and cross-widget batching.
internal static class UiQuadOrder
{
    internal static void Build(float[] vertices,int[] pages,int count,int[] order)
    {
        for(int i=0;i<count;i++)order[i]=i;
        for(int i=1;i<count;i++){
            int page=pages[order[i-1]];if(pages[order[i]]==page)continue;
            for(int j=i+1;j<count;j++){
                if(pages[order[j]]!=page)continue;
                int candidate=order[j];bool safe=true;
                for(int k=i;k<j;k++)if(Overlap(vertices,candidate,order[k])){safe=false;break;}
                if(!safe)continue;
                for(int k=j;k>i;k--)order[k]=order[k-1];order[i]=candidate;break;
            }
        }
    }
    // Apply a destination-to-source permutation in place. Warm identity runs
    // copy no vertices; cycles need only one quad of scratch and consume order.
    internal static void Apply(float[] vertices,int[] pages,int count,int[] order,float[] scratch)
    {
        for(int start=0;start<count;start++){
            if(order[start]==start)continue;
            int savedPage=pages[start],current=start;
            for(int n=0;n<24;n++)scratch[n]=vertices[start*24+n];
            while(order[current]!=start){
                int next=order[current];pages[current]=pages[next];
                for(int n=0;n<24;n++)vertices[current*24+n]=vertices[next*24+n];
                order[current]=current;current=next;
            }
            pages[current]=savedPage;for(int n=0;n<24;n++)vertices[current*24+n]=scratch[n];order[current]=current;
        }
    }
    private static bool Overlap(float[] v,int a,int b)
    {
        float al=float.MaxValue,at=float.MaxValue,ar=float.MinValue,ab=float.MinValue,bl=al,bt=at,br=ar,bb=ab;
        for(int n=0;n<6;n++){
            int x=a*24+n*4,y=b*24+n*4;
            al=Math.Min(al,v[x]);ar=Math.Max(ar,v[x]);at=Math.Min(at,v[x+1]);ab=Math.Max(ab,v[x+1]);
            bl=Math.Min(bl,v[y]);br=Math.Max(br,v[y]);bt=Math.Min(bt,v[y+1]);bb=Math.Max(bb,v[y+1]);
        }
        return al<=br&&bl<=ar&&at<=bb&&bt<=ab;
    }
}
