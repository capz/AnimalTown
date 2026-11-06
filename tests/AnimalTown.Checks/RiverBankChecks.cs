using AnimalTown;

internal static class RiverBankChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(7319);var t=s.Town;const int x=80,z=80;
        Array.Fill(t.Tiles,(byte)Terrain.Water);Array.Clear(t.Elevation);t.GroundCount=0;
        check(RiverBankGeometry.Width>0&&2*(RiverBankGeometry.Width+WaterSurface.FoamWidth)<1,"banks and foam leave open water in a one-cell channel");
        for(int side=0;side<4;side++){
            int nx=RiverBankGeometry.NeighborX(x,side),nz=RiverBankGeometry.NeighborZ(z,side);t.SetTerrain(nx,nz,Terrain.Grass);
            check(RiverBankGeometry.Edge(t,x,z,side,out var a,out var b,out var c,out var d),"every cardinal river bank has a slope");
            check(a.Height==0&&b.Height==0&&c.Height==-.5f&&d.Height==-.5f,"slope joins the exact land plane to river water");
            check(Distance(a,d)>.34f&&Distance(b,c)>.34f,"bank face has horizontal run rather than a vertical wall");
            check(Area(a,b,c)>0&&Area(a,c,d)>0,"bank triangles face upward in GX winding");
            var lip=RiverBankGeometry.Lip(a,d);check(Math.Abs(a.Height-lip.Height-TerrainCliffGeometry.LipDepth)<.00001f,"ground spillover stays a narrow border");
            check(RiverBankGeometry.Foam(t,x,z,side,out var f0,out var f1,out var f2,out var f3)&&Same(f0,d)&&Same(f1,c),"foam starts at the actual bank toe");
            foreach(var p in new[]{c,d,f0,f1,f2,f3})check(Inside(p)&&!s.CanWalk(p.X,p.Z,0),"slope and foam occupy blocked river space");
            check(!TerrainElevation.Traverse(t,nx+.5f,nz+.5f,x+.5f,z+.5f),"shared player/NPC traversal rejects the sloped bank");
            t.SetTerrain(nx,nz,Terrain.Bridge);check(!RiverBankGeometry.HasEdge(t,x,z,side),"bridge approach emits no sloped obstruction");t.SetTerrain(nx,nz,Terrain.Water);
        }
        for(int corner=0;corner<4;corner++){
            int sx=corner==0||corner==3?-1:1,sz=corner<2?-1:1;t.SetTerrain(x+sx,z+sz,Terrain.Grass);
            check(RiverBankGeometry.Corner(t,x,z,corner,out var a,out var b,out var c)&&Area(a,b,c)>0,"convex corner bevel closes the two adjacent slopes with upward winding");
            check(a.Height==0&&b.Height==-.5f&&c.Height==-.5f&&Inside(b)&&Inside(c),"convex corner stays inside water and meets its plane");
            var ob=RiverBankGeometry.FoamOuter(a,b);var oc=RiverBankGeometry.FoamOuter(a,c);
            check(Inside(ob)&&Inside(oc)&&Area(c,b,ob)>0&&Area(c,ob,oc)>0,"convex foam joins remain bounded and face upward");
            int horizontalSide=sz<0?0:2,verticalSide=sx<0?3:1;
            bool horizontal=RiverBankGeometry.Edge(t,x+sx,z,horizontalSide,out var _,out var _,out var hc,out var hd),vertical=RiverBankGeometry.Edge(t,x,z+sz,verticalSide,out var _,out var _,out var vc,out var vd);
            check(horizontal&&vertical,"adjacent cardinal slopes exist beside a convex corner");
            check((Same(b,hc)||Same(b,hd)||Same(b,vc)||Same(b,vd))&&(Same(c,hc)||Same(c,hd)||Same(c,vc)||Same(c,vd)),"corner toe vertices exactly match neighbouring bank edges");
            t.SetTerrain(x+sx,z+sz,Terrain.Water);
        }
        t.SetTerrain(x,z-1,Terrain.Grass);t.SetTerrain(x-1,z,Terrain.Grass);
        RiverBankGeometry.Edge(t,x,z,0,out var na,out var _,out var _,out var nd);RiverBankGeometry.Edge(t,x,z,3,out var _,out var wb,out var wc,out var _);
        check(Same(na,wb)&&Same(nd,wc),"concave banks share their complete miter edge without overlaps or cracks");
        t.Elevation[(z-1)*Town.Width+x]=TerrainElevation.Block;
        check(RiverBankGeometry.Join(t,x,z,0,out var ja,out var jb,out var jc)&&ja.X==jb.X&&ja.Z==jb.Z&&ja.Height!=jb.Height&&Same(jc,nd),"different-height banks close their miter with a vertical junction");
        Array.Fill(t.Tiles,(byte)Terrain.Grass);Array.Clear(t.Elevation);t.SetTerrain(x,z,Terrain.Water);
        check(!TerrainElevation.Traverse(t,x-.01f,z+.02f,x+.02f,z-.01f),"tiny diagonal movement cannot tunnel through a river corner");
        check(TerrainElevation.Traverse(t,x-.2f,z-.1f,x+.2f,z-.1f),"movement on dry ground beside a bank is retained");
        t.SetTerrain(x,z,Terrain.Bridge);check(TerrainElevation.Traverse(t,x-.01f,z+.02f,x+.02f,z-.01f),"exact water clipping leaves bridge traversal intact");
        t.SetTerrain(x,z,Terrain.Sea);check(!RiverBankGeometry.HasEdge(t,x,z,0)&&!RiverBankGeometry.Corner(t,x,z,0,out var _,out var _,out var _),"sea/coastal profiles retain their existing geometry");
        // Every local shore topology, including thin channels and islands.
        // Projected area checks catch overlapping rectangular slopes at corners.
        for(int mask=0;mask<256;mask++){
            Array.Fill(t.Tiles,(byte)Terrain.Water);
            for(int side=0;side<4;side++)if((mask&(1<<side))!=0)t.SetTerrain(RiverBankGeometry.NeighborX(x,side),RiverBankGeometry.NeighborZ(z,side),Terrain.Grass);
            for(int corner=0;corner<4;corner++)if((mask&(16<<corner))!=0)t.SetTerrain(x+(corner==0||corner==3?-1:1),z+(corner<2?-1:1),Terrain.Grass);
            float area=0,expected=0;int vertices=0;var foam=new List<RiverBankPoint[]>();
            for(int side=0;side<4;side++){
                if((mask&(1<<side))!=0)expected+=RiverBankGeometry.Width;
                if((mask&(1<<side))!=0&&(mask&(1<<((side+1)%4)))!=0)expected-=RiverBankGeometry.Width*RiverBankGeometry.Width;
                if(RiverBankGeometry.Edge(t,x,z,side,out var a,out var b,out var c,out var d)){area+=(Area(a,b,c)+Area(a,c,d))*.5f;vertices+=12;}
                if(RiverBankGeometry.Foam(t,x,z,side,out a,out b,out c,out d))foam.Add(new[]{a,b,c,d});
            }
            for(int corner=0;corner<4;corner++){
                if((mask&(16<<corner))!=0&&(mask&(1<<corner))==0&&(mask&(1<<((corner+3)%4)))==0)expected+=RiverBankGeometry.Width*RiverBankGeometry.Width*.5f;
                if(RiverBankGeometry.Corner(t,x,z,corner,out var a,out var b,out var c)){
                    area+=Area(a,b,c)*.5f;vertices+=9;foam.Add(new[]{c,b,RiverBankGeometry.FoamOuter(a,b),RiverBankGeometry.FoamOuter(a,c)});
                }
            }
            check(Math.Abs(area-expected)<.00001f&&vertices<=48,"all shore topologies use disjoint, bounded sloped geometry");
            foreach(var q in foam)check(q.All(Inside)&&Area(q[0],q[1],q[2])>0&&Area(q[0],q[2],q[3])>0,"all foam bands stay inside water with valid winding");
            for(int iz=0;iz<12;iz++)for(int ix=0;ix<12;ix++){
                var sample=new RiverBankPoint(x+(ix+.37f)/12,z+(iz+.61f)/12,0);int layers=0;
                foreach(var q in foam)if(InTriangle(q[0],q[1],q[2],sample)||InTriangle(q[0],q[2],q[3],sample))layers++;
                check(layers<=1,"shore foam never doubles its transparent blend at a corner");
            }
        }
        Console.WriteLine("Sloped river banks: cardinal/convex/concave joins, foam, spillover, bridges and exact water collision passed");
        static bool Inside(RiverBankPoint p)=>p.X>=x&&p.X<=x+1&&p.Z>=z&&p.Z<=z+1;
        static bool Same(RiverBankPoint a,RiverBankPoint b)=>Math.Abs(a.X-b.X)<.00001f&&Math.Abs(a.Z-b.Z)<.00001f&&Math.Abs(a.Height-b.Height)<.00001f;
        static float Distance(RiverBankPoint a,RiverBankPoint b)=>(float)Math.Sqrt((a.X-b.X)*(a.X-b.X)+(a.Z-b.Z)*(a.Z-b.Z));
        static float Area(RiverBankPoint a,RiverBankPoint b,RiverBankPoint c)=>(b.X-a.X)*(c.Z-a.Z)-(b.Z-a.Z)*(c.X-a.X);
        static bool InTriangle(RiverBankPoint a,RiverBankPoint b,RiverBankPoint c,RiverBankPoint p)=>Area(a,b,p)>.000001f&&Area(b,c,p)>.000001f&&Area(c,a,p)>.000001f;
    }
}
