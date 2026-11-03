namespace AnimalTown;

// Same fixed pitch as the original camera, with its eye/target offsets scaled 2/3.
public static class RenderView
{
    public const float ForwardY=-.49026124f,ForwardZ=-.87157554f;
    public static readonly float TanY=(float)Math.Tan(CylinderWorld.CameraFieldOfView*Math.PI/360);
    public static readonly float TanX=TanY*4/3;
    private static readonly float SideNorm=(float)Math.Sqrt(1+TanX*TanX),TopNorm=(float)Math.Sqrt(1+TanY*TanY);
    private static readonly float EyeRadius=(float)Math.Sqrt((CylinderWorld.Radius+CylinderWorld.CameraHeight)*(CylinderWorld.Radius+CylinderWorld.CameraHeight)+CylinderWorld.CameraBack*CylinderWorld.CameraBack);
    private static readonly float EyeY=(CylinderWorld.Radius+CylinderWorld.CameraHeight)/EyeRadius,EyeZ=CylinderWorld.CameraBack/EyeRadius;
    private static readonly float HorizonChord=CylinderWorld.Radius*CylinderWorld.Radius/EyeRadius,HorizonSin=CylinderWorld.Radius/EyeRadius;
    private static readonly float HorizonCos=(float)Math.Sqrt(1-HorizonSin*HorizonSin);
    public static bool Sphere(float x,float y,float z,float radius,float focusX,float focusZ)
    {
        float dx=x-focusX,dy=y-CylinderWorld.CameraHeight,dz=z-focusZ-CylinderWorld.CameraBack;
        float depth=dy*ForwardY+dz*ForwardZ,up=-dy*ForwardZ+dz*ForwardY;
        return depth+radius>=CylinderWorld.Near&&depth-radius<=CylinderWorld.Far&&
            Math.Abs(dx)<=depth*TanX+radius*SideNorm&&Math.Abs(up)<=depth*TanY+radius*TopNorm;
    }
    public static bool SurfaceFront(float z,float focusZ)
    {var p=CylinderWorld.Surface(z,focusZ);return p.UpY*(CylinderWorld.CameraHeight-p.Y)+p.UpZ*(focusZ+CylinderWorld.CameraBack-p.Z)>=0;}
    // Project the object sphere onto the cylinder's YZ cross-section. Cull only
    // when the entire disk is behind the tangent chord AND inside both tangent
    // rays' shadow. No per-object trig, allocation or GPU occlusion query.
    public static bool AboveHorizon(float y,float z,float radius,float focusZ)
    {
        float cy=y+CylinderWorld.Radius,cz=z-focusZ;
        float along=cy*EyeY+cz*EyeZ,margin=radius+.01f;
        if(HorizonChord-along<=margin)return true;
        float across=-cy*EyeZ+cz*EyeY;
        return (EyeRadius-along)*HorizonSin-Math.Abs(across)*HorizonCos<=margin;
    }
}
