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
    private static readonly float terrainRadial=CylinderWorld.Radius+CylinderWorld.CameraHeight+2;
    private static readonly float terrainEyeRadius=(float)Math.Sqrt(terrainRadial*terrainRadial+CylinderWorld.CameraBack*CylinderWorld.CameraBack);
    private static readonly float terrainEyeY=terrainRadial/terrainEyeRadius,terrainEyeZ=CylinderWorld.CameraBack/terrainEyeRadius,terrainSin=CylinderWorld.Radius/terrainEyeRadius;
    private static readonly float terrainCos=(float)Math.Sqrt(1-terrainSin*terrainSin),terrainChord=CylinderWorld.Radius*CylinderWorld.Radius/terrainEyeRadius;
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
    public static bool Box(float x0,float y0,float z0,float x1,float y1,float z1,float focusX,float focusZ)
    {Prepare(focusX,focusZ,1);return frustum.Box(x0,y0,z0,x1,y1,z1);}
    // Terrain columns share Y/Z bounds and the outdoor camera has no X tilt.
    // Test the vertical slice once, then retain an interval for every column.
    // Return the maximum horizontal distance from focusX, or -1 for a culled row.
    // One millimetre covers float rounding between fixed and translated bases;
    // it may retain an edge section, never remove visible geometry.
    public static float SphereRowWidth(float y,float z,float radius,float focusX,float focusZ)
    {
        if(!Sphere(focusX,y,z,radius,focusX,focusZ,1))return -1;
        float depth=(y-(CylinderWorld.CameraHeight+1))*ForwardY+(z-(focusZ+CylinderWorld.CameraBack))*ForwardZ;
        return depth*TanX+(radius+.025f)*SideNorm+.001f;
    }
    public static float BoxRowWidth(float y0,float z0,float y1,float z1,float halfWidth,float focusX,float focusZ)
    {
        if(!(halfWidth>=0)||float.IsInfinity(halfWidth))throw new ArgumentOutOfRangeException();
        if(!Box(focusX,y0,z0,focusX,y1,z1,focusX,focusZ))return -1;
        float y=(y0+y1)*.5f-(CylinderWorld.CameraHeight+1),z=(z0+z1)*.5f-(focusZ+CylinderWorld.CameraBack);
        float depth=y*ForwardY+z*ForwardZ,extent=Math.Abs(ForwardY)*(y1-y0)*.5f+Math.Abs(ForwardZ)*(z1-z0)*.5f;
        return (depth+extent)*TanX+halfWidth+.001f;
    }
    // The highest allowed eye sees the furthest horizon. Keep this independent
    // of the frustum cache so terrain selection never rebuilds it per section.
    public static bool TerrainHorizon(float y,float z,float radius,float focusZ)
    {
        float cy=y+CylinderWorld.Radius,cz=z-focusZ,along=cy*terrainEyeY+cz*terrainEyeZ;
        if(terrainChord-along<=radius+.4f)return true;
        float across=-cy*terrainEyeZ+cz*terrainEyeY;
        return (terrainEyeRadius-along)*terrainSin-Math.Abs(across)*terrainCos<=radius+.4f;
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
