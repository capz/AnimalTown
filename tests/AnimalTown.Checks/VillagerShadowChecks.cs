using AnimalTown;

internal static class VillagerShadowChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var t=new Town(73);Array.Fill(t.Tiles,(byte)Terrain.Grass);Array.Clear(t.Elevation);
        var first=new VillagerShadowPatch(t,0,80.01f,80.01f);
        var g=new GroundShadow();g.Begin(t,first.Receiver,true);int stages=0;
        while(!g.Advance())check(++stages<10,"moving receiver work stays within a bounded number of fine rows");
        check(!g.Overflow&&g.Vertices>0&&g.Vertices<=GroundShadow.MaximumTriangles*3,"moving receiver uses bounded geometry");
        var original=(float[])g.Positions.Clone();
        for(int frame=0;frame<120;frame++){
            var next=new VillagerShadowPatch(t,0,80.01f+frame/120f*1.97f,80.01f+frame/120f*1.97f);
            check(first.SameRegion(next)&&first.Covers(t,next),"sub-bucket walking reuses the same supported receiver");
            check(Math.Abs((next.X+VillagerShadowPatch.OffsetX)*VillagerShadowPatch.UvScale+next.UOffset-.5f)<.00002f&&Math.Abs((next.Z+VillagerShadowPatch.OffsetZ)*VillagerShadowPatch.UvScale+next.VOffset-.5f)<.00002f,"GPU UV translation keeps the circle centered on moving feet");
        }
        check(original.SequenceEqual(g.Positions),"moving the mask never rewrites receiver vertices");
        for(int i=0;i<g.Vertices;i++){
            var p=CylinderWorld.Surface(g.Uvs[i*2+1],0,.012f);
            check(g.Uvs[i*2]==g.Positions[i*3]&&Math.Abs(p.Y-g.Positions[i*3+1])<.00001f&&Math.Abs(p.Z-g.Positions[i*3+2])<.00001f,"moving mask UVs retain logical world coordinates rather than bent depths");
        }
        var boundary=new VillagerShadowPatch(t,0,82.01f,80.5f);
        check(!first.SameRegion(boundary)&&first.Covers(t,boundary),"overlap retains the old receiver while the next bucket is generated");
        check(!first.Covers(t,new VillagerShadowPatch(t,0,90,90)),"teleports suppress an obsolete receiver immediately");
        t.NotifyTerrainChanged();check(!first.Covers(t,new VillagerShadowPatch(t,0,80.5f,80.5f)),"edited receiver heights invalidate actor shadows");
        for(int z=80;z<82;z++)for(int x=80;x<82;x++)t.Elevation[z*Town.Width+x]=TerrainElevation.CornerUpLeft;
        var low=new VillagerShadowPatch(t,0,80.2f,80.2f);var high=new VillagerShadowPatch(t,0,81.8f,81.8f);
        check(!low.SameRegion(high)&&!low.Covers(t,high),"diagonal upper/lower changes never reuse the opposite support surface");
        for(int z=80;z<82;z++)for(int x=80;x<82;x++)t.Elevation[z*Town.Width+x]=TerrainElevation.RampNorth;
        var ramp=new VillagerShadowPatch(t,0,80.5f,80.2f);var downhill=new VillagerShadowPatch(t,0,80.5f,81.8f);
        check(ramp.SameRegion(downhill)&&ramp.Covers(t,downhill),"moving down a natural ramp changes height without rebuilding each frame");
        var room=new VillagerShadowPatch(t,16,2,3.35f,4,true);
        check(!ramp.Covers(t,room)&&!room.SameRegion(new VillagerShadowPatch(t,17,2,3.35f,4,true)),"entry, exit and NPC room changes invalidate receivers by space");
        g.BeginIndoor(room.Receiver,4,true);stages=0;while(!g.Advance())check(++stages<10,"indoor receivers are staged under the same bounded work model");
        bool mat=false;check(!g.Overflow&&g.Vertices>0,"indoor clipped receiver fits its budget");
        for(int i=0;i<g.Vertices;i++){
            float x=g.Positions[i*3],y=g.Positions[i*3+1],z=g.Positions[i*3+2];
            check(x>=0&&x<=4&&z>=0&&z<=4,"cutaway walls never reveal shadows extending outside the room floor");
            check(x==g.Uvs[i*2]&&z==g.Uvs[i*2+1],"indoor mask uses the same world UV translation");
            if(y>.02f){check(x>=1.35f-.0001f&&x<=2.65f+.0001f&&z>=2.85f-.0001f&&z<=3.95f+.0001f,"entry-mat receiver is clipped to the authored raised floor overlay");mat=true;}
        }
        check(mat,"indoor contact shadow also renders above the entry mat");
        byte[] mask=VillagerShadowPatch.Texture();check(mask.Length==2048,"all seven actor shadows share a 2KiB IA8 mask");
        int Alpha(int x,int y)=>mask[(((y/4)*8+x/4)*16+y%4*4+x%4)*2];
        check(Alpha(15,15)==255&&Alpha(0,15)==0&&Alpha(15,0)==0&&Alpha(31,31)==0,"circle has an opaque core and transparent clamping border");
        for(int y=0;y<32;y++)for(int x=0;x<32;x++)check(Alpha(x,y)==Alpha(31-x,y)&&Alpha(x,y)==Alpha(y,x),"circular shadow is symmetric across walking directions");
    }
}
