internal static class UiQuadOrderChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var vertices=new float[4*24];var pages=new[]{0,1,0,1};var order=new int[4];
        void Rect(int i,float left,float right){for(int n=0;n<6;n++){vertices[i*24+n*4]=n%2==0?left:right;vertices[i*24+n*4+1]=n<3?0:10;}}
        for(int i=0;i<4;i++)Rect(i,i*12,i*12+10);
        UiQuadOrder.Build(vertices,pages,4,order);
        check(order.SequenceEqual(new[]{0,2,1,3}),"disjoint text groups by atlas page without dropping glyphs");
        Rect(2,15,34);UiQuadOrder.Build(vertices,pages,4,order);
        check(Array.IndexOf(order,1)<Array.IndexOf(order,2)&&order.OrderBy(n=>n).SequenceEqual(new[]{0,1,2,3}),"overlapping translucent glyphs keep their painter order");
        Rect(2,22,34);UiQuadOrder.Build(vertices,pages,4,order);
        check(Array.IndexOf(order,1)<Array.IndexOf(order,2)&&order.OrderBy(n=>n).SequenceEqual(new[]{0,1,2,3}),"touching filtered glyph bounds also preserve painter order");
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<1000;i++)UiQuadOrder.Build(vertices,pages,4,order);
        check(GC.GetAllocatedBytesForCurrentThread()==before,"text ordering uses only fixed scratch memory");
    }
}
