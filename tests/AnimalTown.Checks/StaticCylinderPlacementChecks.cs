using AnimalTown;
internal static class StaticCylinderPlacementChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(RenderView.ProjectedPixels(1,50,1.8f,50)>RenderView.ProjectedPixels(1,30,1.8f,50),"texture detail follows projected depth");
        check(RenderView.ProjectedPixels(1,50,3.6f,50)>RenderView.ProjectedPixels(1,50,1.8f,50),"larger projected coverage requests sharper detail");
        var indoor=new InteriorCamera();int pixels=indoor.ProjectedPixels(2,.9f,2,1.8f,4);check(pixels>0&&pixels<=1024,"room projected texture coverage is bounded");
        var cache=new StaticCylinderPlacements(8);var owner=new object();float maximum=0;
        for(int focus=0;focus<Town.Height;focus+=3)for(int z=0;z<Town.Height;z+=2)foreach(float fraction in new[]{0f,.17f,.53f,.99f})foreach(float height in new[]{0f,2f}){
            cache.Begin(owner,0,focus+fraction);var p=cache.Get(z%8,17,z,height);
            foreach(float extra in new[]{0f,.375f,1.5f,3f}){var got=cache.Project(p,extra);var expected=CylinderWorld.Surface(z,focus+fraction,height+extra);float error=Math.Max(Math.Abs(got.Y-expected.Y),Math.Abs(got.Z-expected.Z));maximum=Math.Max(maximum,error);check(error<=.0001f,"cached static placement follows exact common cylinder scroll");}
        }
        cache.Begin(owner,1,50);cache.Get(0,3,4,2);int builds=cache.Builds;
        for(int n=0;n<100;n++){cache.Begin(owner,1,50+n*.1f);cache.Get(0,3,4,2);}
        check(cache.Builds==builds,"scrolling never rebuilds fixed local placement");
        cache.Get(0,4,4,2);check(cache.Builds==builds+1,"moving/replacing an object updates its placement");
        cache.Begin(owner,2,50);cache.Get(0,4,4,2);check(cache.Builds==builds+2,"terrain revision invalidates local placement");
        cache.Begin(new object(),2,50);cache.Get(0,4,4,2);check(cache.Builds==builds+3,"loading another town resets placement ownership");
        Console.WriteLine("Static instance scroll: max error "+maximum+"m; cached placement, height, owner/revision and movement invalidation passed");
    }
}
