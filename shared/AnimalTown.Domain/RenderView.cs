namespace AnimalTown;

// Same fixed pitch as the original camera, with its eye/target offsets scaled 2/3.
public static class RenderView
{
    public const float ForwardY=-.49026124f,ForwardZ=-.87157554f;
    public static readonly float TanY=(float)Math.Tan(CylinderWorld.CameraFieldOfView*Math.PI/360);
    public static readonly float TanX=TanY*4/3;
    private static readonly float SideNorm=(float)Math.Sqrt(1+TanX*TanX),TopNorm=(float)Math.Sqrt(1+TanY*TanY);
    public static bool Sphere(float x,float y,float z,float radius,float focusX,float focusZ)
    {
        float dx=x-focusX,dy=y-CylinderWorld.CameraHeight,dz=z-focusZ-CylinderWorld.CameraBack;
        float depth=dy*ForwardY+dz*ForwardZ,up=-dy*ForwardZ+dz*ForwardY;
        return depth+radius>=CylinderWorld.Near&&depth-radius<=CylinderWorld.Far&&
            Math.Abs(dx)<=depth*TanX+radius*SideNorm&&Math.Abs(up)<=depth*TanY+radius*TopNorm;
    }
    public static bool SurfaceFront(float z,float focusZ)
    {var p=CylinderWorld.Surface(z,focusZ);return p.UpY*(CylinderWorld.CameraHeight-p.Y)+p.UpZ*(focusZ+CylinderWorld.CameraBack-p.Z)>=0;}
}
