using AnimalTown;
using Dolphin.Graphics;

internal static class FallLeafWakeChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var town=new Town(14);
        Array.Fill(town.Tiles,(byte)Terrain.Grass);
        Array.Clear(town.Elevation);
        var wake=new FallLeafWake();
        var vertices=new VertexPositionColorTexture[FallLeafWake.Capacity*4];
        const float x=24,z=30;
        for(int i=0;i<30;i++)wake.TrackStep(town,x,z,x,z,i/30f);
        check(wake.WriteVertices(vertices,z,1)==0,"blocked or idle movement emits no fall leaves");

        // Fixed 30 Hz travel from gentle analog walking, including diagonal
        // movement and a small tilt while holding the run button.
        foreach(float step in new[]{PlayerMovement.WalkStep*.25f,PlayerMovement.RunStep*.2f}){
            for(int i=1;i<=90;i++)wake.TrackStep(town,x+(i-1)*step,z,x+i*step,z,i/30f);
            check(wake.WriteVertices(vertices,z,3)==0,"gentle movement never accumulates a leaf-emitting stride");
        }
        for(int i=1;i<=90;i++)wake.TrackStep(town,x+(i-1)*.03f,z+(i-1)*.03f,x+i*.03f,z+i*.03f,i/30f);
        check(wake.WriteVertices(vertices,z,3)==0,"slow diagonal movement also suppresses leaves");
        for(int i=1;i<=6;i++)wake.TrackStep(town,x+(i-1)*.1f,z,x+i*.1f,z,.1f);
        wake.TrackStep(town,x+.6f,z,x+.62f,z,.2f);
        wake.TrackStep(town,x+.62f,z,x+.62f+PlayerMovement.RunStep,z,.3f);
        check(wake.WriteVertices(vertices,z,.3f)==0,"slowing down resets a partial stride before running resumes");
        wake.Clear();

        for(int i=1;i<=10;i++)wake.TrackStep(town,x+(i-1)*.09f,z,x+i*.09f,z,1+i*.03f);
        check(wake.WriteVertices(vertices,z,1.31f)==3,"walking one stride kicks up three leaves");
        float before=vertices[0].Position.Y;
        wake.WriteVertices(vertices,z,1.54f);
        check(vertices[0].Position.Y>before+.04f,"the first leaves arc above the footstep");
        check(wake.WriteVertices(vertices,z,2.4f)==0,"the whole wake expires after the player stops");

        for(int i=1;i<=5;i++)wake.TrackStep(town,x+(i-1)*.16f,z,x+i*.16f,z,3+i*.03f);
        check(wake.WriteVertices(vertices,z,3.16f)==3,"running uses the same three-leaf burst as walking");
        for(int i=1;i<=45;i++)wake.TrackStep(town,x+(i-1)*.02f,z,x+i*.02f,z,3.16f+i/30f);
        check(wake.WriteVertices(vertices,z,4.66f)==0,"existing leaves fade naturally and slow movement cannot replace them");
        wake.Clear();
        check(wake.WriteVertices(vertices,z,3.2f)==0,"clearing the wake removes every live leaf");
        Array.Fill(town.Tiles,(byte)Terrain.Bridge);
        for(int i=1;i<=10;i++)wake.TrackStep(town,x+(i-1)*.16f,z,x+i*.16f,z,4+i*.03f);
        check(wake.WriteVertices(vertices,z,4.31f)==0,"bridges don't kick up leaves");

        Array.Fill(town.Tiles,(byte)Terrain.Camp);
        for(int i=1;i<=120;i++)wake.TrackStep(town,x+(i-1)*.16f,z,x+i*.16f,z,5+i*.005f);
        check(wake.WriteVertices(vertices,z,5.6f)==FallLeafWake.Capacity,"running across dirt stays within one 24-quad batch");
        bool narrow=false,lobed=false;
        for(int i=0;i<vertices.Length;i+=4){
            float left=vertices[i].TextureCoordinate.X,right=vertices[i+2].TextureCoordinate.X;
            narrow|=right<.5f;lobed|=left>.5f;
            check((left-.5f)*(right-.5f)>0,"each leaf samples just one atlas silhouette");
            Vector3 a=vertices[i].Position,b=vertices[i+1].Position,c=vertices[i+2].Position,d=vertices[i+3].Position;
            check(CameraArea(a,b,c)<0&&CameraArea(a,c,d)<0,"both leaf triangles face the camera through GX backface culling");
        }
        check(narrow&&lobed,"the wake uses both silhouettes");
        long start=GC.GetAllocatedBytesForCurrentThread();
        for(int i=1;i<=120;i++){
            wake.TrackStep(town,x+(i-1)*.16f,z,x+i*.16f,z,6+i*.03f);
            wake.WriteVertices(vertices,z,6+i*.03f);
        }
        check(GC.GetAllocatedBytesForCurrentThread()==start,"warmed leaf movement and staging allocate nothing per frame");

        town.GroundCount=2;
        town.Ground[0]=new GroundItem{X=(int)x,Z=(int)z-4,Kind=3,Item=1};
        town.Ground[1]=new GroundItem{X=(int)x+2,Z=(int)z-4,Kind=2,Item=Catalog.CherrySapling};
        wake.TrackPetals(town,x,z,0,20);
        check(wake.WriteVertices(vertices,z,20)==0,"regular trees and cherry saplings emit no petals");
        town.Ground[1].Kind=3;
        wake.TrackPetals(town,x,z,0,25);
        int burst=wake.WriteVertices(vertices,z,25);
        check(burst is 2 or 3,"a visible mature cherry emits a small burst while the player stands still");
        float startHeight=vertices[0].Position.Y;
        var pink=vertices[0].Color;
        check(pink.R>pink.B&&pink.B>pink.G,"petals are pink");
        wake.TrackPetals(town,x,z,0,26);
        check(wake.WriteVertices(vertices,z,26)==burst&&vertices[0].Position.Y<startHeight-.25f,"petals descend before another burst can start");
        check(wake.WriteVertices(vertices,z,29)==0,"petals expire");
        wake.TrackPetals(town,x,z,0,30);
        wake.SetPetalMode(false);
        check(wake.WriteVertices(vertices,z,30)==0,"season changes clear the shared effect buffer");
    }

    private static float CameraArea(Vector3 a,Vector3 b,Vector3 c)
        =>(b.X-a.X)*CameraUp(c,a)-(c.X-a.X)*CameraUp(b,a);
    private static float CameraUp(Vector3 p,Vector3 origin)=>.8f*(p.Y-origin.Y)-.6f*(p.Z-origin.Z);
}
