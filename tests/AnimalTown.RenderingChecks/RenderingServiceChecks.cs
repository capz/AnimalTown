using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

internal static class RenderingServiceChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var mesh=new DynamicMeshBuffer(192);
        var vertices=mesh.Reserve(6);
        // Simulate a rebuild prefix longer than the last committed Count (zero).
        for(int i=0;i<6;i++)vertices[i]=new VertexPositionColorTexture(new Vector3(i+1,0,0),Color32.White,Vector2.Zero);
        vertices=GeometryStaging.Reserve(mesh,vertices,6,48);
        for(int i=6;i<48;i++)vertices[i]=new VertexPositionColorTexture(new Vector3(i+1,0,0),Color32.White,Vector2.Zero);
        vertices=GeometryStaging.Reserve(mesh,vertices,48,96);
        for(int i=0;i<48;i++)check(vertices[i].Position.X==i+1,"growing rebuild retains its entire staged prefix");
        check(mesh.Capacity==96,"mesh staging remains geometrically bounded");
        bool rejected=false;try{mesh.Reserve(193);}catch(ArgumentOutOfRangeException){rejected=true;}
        check(rejected,"mesh budget rejects overflow before allocating");
        // Compare cached camera/horizon against the pre-migration equations,
        // including elevated focus and repeated switches between views.
        var random=new Random(481);
        for(int n=0;n<4000;n++){
            float fx=random.Next(-10,170),fz=random.Next(-10,170),height=n%3==0?0:n%3==1?2:-.5f;
            float x=fx+random.Next(-40,40),y=random.Next(-20,20),z=fz+random.Next(-40,40),radius=.2f+random.Next(0,30)*.1f;
            RenderView.Prepare(fx,fz,height);
            float dx=x-fx,dy=y-CylinderWorld.CameraHeight-height,dz=z-fz-CylinderWorld.CameraBack;
            float depth=dy*RenderView.ForwardY+dz*RenderView.ForwardZ,up=-dy*RenderView.ForwardZ+dz*RenderView.ForwardY,r=radius+.025f;
            bool sphere=depth+r>=CylinderWorld.Near&&depth-r<=CylinderWorld.Far&&Math.Abs(dx)<=depth*RenderView.TanX+r*(float)Math.Sqrt(1+RenderView.TanX*RenderView.TanX)&&Math.Abs(up)<=depth*RenderView.TanY+r*(float)Math.Sqrt(1+RenderView.TanY*RenderView.TanY);
            check(RenderView.Sphere(x,y,z,radius,fx,fz,height)==sphere,"shared frustum preserves established visibility");
            float radial=CylinderWorld.Radius+CylinderWorld.CameraHeight+height,er=(float)Math.Sqrt(radial*radial+CylinderWorld.CameraBack*CylinderWorld.CameraBack),ey=radial/er,ez=CylinderWorld.CameraBack/er,sin=CylinderWorld.Radius/er,cos=(float)Math.Sqrt(1-sin*sin);
            float cy=y+CylinderWorld.Radius,cz=z-fz,along=cy*ey+cz*ez,across=-cy*ez+cz*ey,margin=radius+.4f;
            bool horizon=CylinderWorld.Radius*CylinderWorld.Radius/er-along<=margin||(er-along)*sin-Math.Abs(across)*cos<=margin;
            check(RenderView.AboveHorizon(y,z,radius,fz,height)==horizon,"cached horizon preserves cylinder visibility");
        }
    }
}
