namespace AnimalTown;

// The cylinder's axis is east-west (X). Gameplay coordinates measure surface
// arc distance along Z. X never curves: this is a cylinder, not a sphere.
public readonly struct CylinderPose
{
    public readonly float Y,Z,UpY,UpZ,Pitch;
    public CylinderPose(float y,float z,float upY,float upZ,float pitch)
    {Y=y;Z=z;UpY=upY;UpZ=upZ;Pitch=pitch;}
}
public static class CylinderWorld
{
    // Reuse the visible material batches throughout a two-metre scroll window.
    // Culling uses its centre and a guard for fractional motion / terrace height.
    public const int ScrollWindow=2;
    public const float ScrollGuard=2;
    public static float ScrollAnchor(float coordinate)=>(float)Math.Floor(coordinate/ScrollWindow)*ScrollWindow+ScrollWindow*.5f;
    // Curvature is independent of playable bounds. A ten-percent smaller
    // radius brings the horizon closer without changing the camera lens.
    public const float Radius=360f/6.28318530718f;
    public const int ViewHalfWidth=64;
    // Keep the lens and eye distance; aim at the player to show more ground.
    public const float CameraFieldOfView=53.54f;
    public const float CameraHeight=5.4f,CameraBack=7.2f,CameraAimAhead=0;
    public const float Near=.1f,Far=60;
    public static CylinderPose Surface(float logicalZ,float focusZ,float height=0)
    {
        float angle=(logicalZ-focusZ)/Radius;
        float sine=(float)Math.Sin(angle),cosine=(float)Math.Cos(angle);
        return new CylinderPose((Radius+height)*cosine-Radius,focusZ+(Radius+height)*sine,cosine,sine,angle);
    }
    public static CylinderScroll Scroll(float referenceZ,float focusZ)=>new CylinderScroll(referenceZ,focusZ);
}

// Exact rigid mapping of a mesh baked at referenceZ onto the current cylinder.
// Translation accounts for the axis at Y=-Radius, Z=referenceZ and camera Z.
public readonly struct CylinderScroll
{
    public readonly float Sin,Cos,HalfSin,HalfCos,Y,Z;
    public CylinderScroll(float referenceZ,float focusZ)
    {
        float angle=(referenceZ-focusZ)/CylinderWorld.Radius;
        Sin=(float)Math.Sin(angle);Cos=(float)Math.Cos(angle);
        HalfSin=(float)Math.Sin(angle*.5f);HalfCos=(float)Math.Cos(angle*.5f);
        Y=CylinderWorld.Radius*(Cos-1)+referenceZ*Sin;
        Z=focusZ-referenceZ*Cos+CylinderWorld.Radius*Sin;
    }
    public CylinderPose Apply(CylinderPose p)=>new CylinderPose(Cos*p.Y-Sin*p.Z+Y,Sin*p.Y+Cos*p.Z+Z,Cos*p.UpY-Sin*p.UpZ,Sin*p.UpY+Cos*p.UpZ,p.Pitch+(float)Math.Atan2(Sin,Cos));
}
