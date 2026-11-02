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
    public const float Radius=Town.Height/6.28318530718f;
    public const int ViewHalfWidth=64;
    // Matches a 20-unit horizontal span on the player's tangent plane in 4:3.
    public const float CameraFieldOfView=53.54f;
    public static CylinderPose Surface(float logicalZ,float focusZ,float height=0)
    {
        float angle=(logicalZ-focusZ)/Radius;
        float sine=(float)Math.Sin(angle),cosine=(float)Math.Cos(angle);
        return new CylinderPose((Radius+height)*cosine-Radius,focusZ+(Radius+height)*sine,cosine,sine,angle);
    }
}
