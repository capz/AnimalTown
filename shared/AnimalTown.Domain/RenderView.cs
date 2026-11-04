namespace AnimalTown;

// Derive the view direction from the same eye/aim offsets used by the renderer.
public static class RenderView
{
    private static readonly float DirectionLength=(float)Math.Sqrt(CylinderWorld.CameraHeight*CylinderWorld.CameraHeight+(CylinderWorld.CameraBack+CylinderWorld.CameraAimAhead)*(CylinderWorld.CameraBack+CylinderWorld.CameraAimAhead));
    public static readonly float ForwardY=-CylinderWorld.CameraHeight/DirectionLength,ForwardZ=-(CylinderWorld.CameraBack+CylinderWorld.CameraAimAhead)/DirectionLength;
    public static readonly float TanY=(float)Math.Tan(CylinderWorld.CameraFieldOfView*Math.PI/360);
    public static readonly float TanX=TanY*4/3;
    private static readonly float SideNorm=(float)Math.Sqrt(1+TanX*TanX),TopNorm=(float)Math.Sqrt(1+TanY*TanY);
    private static readonly float EyeRadius=(float)Math.Sqrt((CylinderWorld.Radius+CylinderWorld.CameraHeight)*(CylinderWorld.Radius+CylinderWorld.CameraHeight)+CylinderWorld.CameraBack*CylinderWorld.CameraBack);
    private static readonly float EyeY=(CylinderWorld.Radius+CylinderWorld.CameraHeight)/EyeRadius,EyeZ=CylinderWorld.CameraBack/EyeRadius;
    private static readonly float HorizonChord=CylinderWorld.Radius*CylinderWorld.Radius/EyeRadius,HorizonSin=CylinderWorld.Radius/EyeRadius;
    private static readonly float HorizonCos=(float)Math.Sqrt(1-HorizonSin*HorizonSin);
    public static bool Sphere(float x,float y,float z,float radius,float focusX,float focusZ,float focusHeight=0)
    {
        // Retain a small guard band at plane boundaries; GX clips crossing
        // triangles. CPU visibility must reject only an entirely outside bound.
        radius+=.025f;
        float dx=x-focusX,dy=y-CylinderWorld.CameraHeight-focusHeight,dz=z-focusZ-CylinderWorld.CameraBack;
        float depth=dy*ForwardY+dz*ForwardZ,up=-dy*ForwardZ+dz*ForwardY;
        return depth+radius>=CylinderWorld.Near&&depth-radius<=CylinderWorld.Far&&
            Math.Abs(dx)<=depth*TanX+radius*SideNorm&&Math.Abs(up)<=depth*TanY+radius*TopNorm;
    }
    public static bool SurfaceFront(float z,float focusZ,float focusHeight=0)
    {var p=CylinderWorld.Surface(z,focusZ);return p.UpY*(CylinderWorld.CameraHeight+focusHeight-p.Y)+p.UpZ*(focusZ+CylinderWorld.CameraBack-p.Z)>=-2;}
    // Project the object sphere onto the cylinder's YZ cross-section. Cull only
    // when the entire disk is behind the tangent chord AND inside both tangent
    // rays' shadow. No per-object trig, allocation or GPU occlusion query.
    public static bool AboveHorizon(float y,float z,float radius,float focusZ,float focusHeight=0)
    {
        float eyeRadius=focusHeight==0?EyeRadius:(float)Math.Sqrt((CylinderWorld.Radius+CylinderWorld.CameraHeight+focusHeight)*(CylinderWorld.Radius+CylinderWorld.CameraHeight+focusHeight)+CylinderWorld.CameraBack*CylinderWorld.CameraBack);
        float eyeY=(CylinderWorld.Radius+CylinderWorld.CameraHeight+focusHeight)/eyeRadius,eyeZ=CylinderWorld.CameraBack/eyeRadius,sin=CylinderWorld.Radius/eyeRadius,cos=(float)Math.Sqrt(1-sin*sin);
        float cy=y+CylinderWorld.Radius,cz=z-focusZ;
        float along=cy*eyeY+cz*eyeZ,margin=radius+.4f;
        if(CylinderWorld.Radius*CylinderWorld.Radius/eyeRadius-along<=margin)return true;
        float across=-cy*eyeZ+cz*eyeY;
        return (eyeRadius-along)*sin-Math.Abs(across)*cos<=margin;
    }
}
