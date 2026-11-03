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
    // Curvature is independent of playable bounds. A ten-percent smaller
    // radius brings the horizon closer without changing the camera lens.
    public const float Radius=360f/6.28318530718f;
    public const int ViewHalfWidth=64;
    // Preserve the lens; scale eye and aim offsets together to keep the pitch.
    public const float CameraFieldOfView=53.54f;
    public const float CameraHeight=5.4f,CameraBack=7.2f,CameraAimAhead=2.4f;
    public const float Near=.1f,Far=60;
    public static CylinderPose Surface(float logicalZ,float focusZ,float height=0)
    {
        float angle=(logicalZ-focusZ)/Radius;
        float sine=(float)Math.Sin(angle),cosine=(float)Math.Cos(angle);
        return new CylinderPose((Radius+height)*cosine-Radius,focusZ+(Radius+height)*sine,cosine,sine,angle);
    }
}
