namespace AnimalTown;

// Derive the view direction from the same eye/aim offsets used by the renderer.
public static class RenderView
{
    private static readonly float DirectionLength=(float)Math.Sqrt(CylinderWorld.CameraHeight*CylinderWorld.CameraHeight+(CylinderWorld.CameraBack+CylinderWorld.CameraAimAhead)*(CylinderWorld.CameraBack+CylinderWorld.CameraAimAhead));
    public static readonly float ForwardY=-CylinderWorld.CameraHeight/DirectionLength,ForwardZ=-(CylinderWorld.CameraBack+CylinderWorld.CameraAimAhead)/DirectionLength;
    public static readonly float TanY=(float)Math.Tan(CylinderWorld.CameraFieldOfView*Math.PI/360);
    public static readonly float TanX=TanY*4/3;
    private static readonly float SideNorm=(float)Math.Sqrt(1+TanX*TanX),TopNorm=(float)Math.Sqrt(1+TanY*TanY);
    private static float cachedX=float.NaN,cachedZ,cachedHeight;
    private static float eyeRadius,eyeY,eyeZ,horizonSin,horizonCos,horizonChord;
    private static readonly Hypercube.CameraFrustum frustum=new Hypercube.CameraFrustum(0,CylinderWorld.CameraHeight,CylinderWorld.CameraBack,0,0,-CylinderWorld.CameraAimAhead,0,1,0,CylinderWorld.CameraFieldOfView,4f/3,CylinderWorld.Near,CylinderWorld.Far);
    // Called once before a pass, with lazy validation for domain callers/tests.
    public static void Prepare(float focusX,float focusZ,float focusHeight=0)
    {
        if(cachedX==focusX&&cachedZ==focusZ&&cachedHeight==focusHeight)return;
        frustum.Set(focusX,CylinderWorld.CameraHeight+focusHeight,focusZ+CylinderWorld.CameraBack,focusX,focusHeight,focusZ-CylinderWorld.CameraAimAhead,0,1,0,CylinderWorld.CameraFieldOfView,4f/3,CylinderWorld.Near,CylinderWorld.Far);
        if(float.IsNaN(cachedX)||cachedHeight!=focusHeight){
            float radial=CylinderWorld.Radius+CylinderWorld.CameraHeight+focusHeight;
            eyeRadius=(float)Math.Sqrt(radial*radial+CylinderWorld.CameraBack*CylinderWorld.CameraBack);
            eyeY=radial/eyeRadius;eyeZ=CylinderWorld.CameraBack/eyeRadius;horizonSin=CylinderWorld.Radius/eyeRadius;
            horizonCos=(float)Math.Sqrt(1-horizonSin*horizonSin);horizonChord=CylinderWorld.Radius*CylinderWorld.Radius/eyeRadius;
        }
        cachedX=focusX;cachedZ=focusZ;cachedHeight=focusHeight;
    }
    public static bool Sphere(float x,float y,float z,float radius,float focusX,float focusZ,float focusHeight=0)
    {
        Prepare(focusX,focusZ,focusHeight);
        // Retain the established GX clip guard band.
        return frustum.Sphere(x,y,z,radius+.025f);
    }
    public static bool SurfaceFront(float z,float focusZ,float focusHeight=0)
    {var p=CylinderWorld.Surface(z,focusZ);return p.UpY*(CylinderWorld.CameraHeight+focusHeight-p.Y)+p.UpZ*(focusZ+CylinderWorld.CameraBack-p.Z)>=-2;}
    // Project the object sphere onto the cylinder's YZ cross-section. Cull only
    // when the entire disk is behind the tangent chord AND inside both tangent
    // rays' shadow. No per-object trig, allocation or GPU occlusion query.
    public static bool AboveHorizon(float y,float z,float radius,float focusZ,float focusHeight=0)
    {
        Prepare(float.IsNaN(cachedX)?0:cachedX,focusZ,focusHeight);
        float cy=y+CylinderWorld.Radius,cz=z-focusZ;
        float along=cy*eyeY+cz*eyeZ,margin=radius+.4f;
        if(horizonChord-along<=margin)return true;
        float across=-cy*eyeZ+cz*eyeY;
        return (eyeRadius-along)*horizonSin-Math.Abs(across)*horizonCos<=margin;
    }
}
