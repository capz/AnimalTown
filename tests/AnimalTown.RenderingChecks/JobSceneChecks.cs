using Dolphin.Graphics;

internal static class JobSceneChecks
{
    internal static void Run(Action<bool,string> check)
    {
        foreach(var vertices in new[]{JobSceneRenderer.Sphere(),JobSceneRenderer.Shirt()}){
            int faces=0;
            for(int i=0;i<vertices.Length;i+=3){
                var a=vertices[i].Position;var b=vertices[i+1].Position;var c=vertices[i+2].Position;
                float ux=b.X-a.X,uy=b.Y-a.Y,uz=b.Z-a.Z,vx=c.X-a.X,vy=c.Y-a.Y,vz=c.Z-a.Z;
                float nx=uy*vz-uz*vy,ny=uz*vx-ux*vz,nz=ux*vy-uy*vx;
                if(nx*nx+ny*ny+nz*nz<.00000001f)continue;
                var n=vertices[i].Normal;float dot=nx*n.X+ny*n.Y+nz*n.Z;
                check(float.IsFinite(dot)&&dot<0,"job meshes face outward with the native clockwise GX convention");faces++;
            }
            check(faces>=64,"shared ball and outfit geometry contain complete outward surfaces");
        }
        for(int species=0;species<6;species++){
            JobSceneRenderer.OutfitFit(species,out float bottom,out float width,out float height);
            check(bottom>=.18f&&bottom+height<=.6f&&width>=.15f&&width<=.23f,"outfit fits the lower torso after the cooked character root normalizes its height");
        }
        Console.WriteLine("Job scene: clockwise visible surfaces and all six normalized torso bounds passed");
    }
}
