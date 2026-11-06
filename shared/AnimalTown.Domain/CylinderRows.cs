namespace AnimalTown;

// Geometry is baked at focus Z=0. Camera motion is one rigid X rotation plus
// translation, shared by every terrain patch; no vertex needs to be rewritten.
public sealed class CylinderRows
{
    public const int Minimum=-80,Maximum=Town.Height+80;
    private readonly CylinderPose[] rows=new CylinderPose[Maximum-Minimum+1];
    public CylinderRows(){for(int z=Minimum;z<=Maximum;z++)rows[z-Minimum]=CylinderWorld.Surface(z,0);}
    public CylinderPose At(int z,float height=0)
    {
        if(z<Minimum||z>Maximum)throw new ArgumentOutOfRangeException();
        var p=rows[z-Minimum];return new CylinderPose(p.Y+height*p.UpY,p.Z+height*p.UpZ,p.UpY,p.UpZ,p.Pitch);
    }
    public CylinderPose Chord(float z,float height=0)
    {
        int row=(int)Math.Floor(z);float t=z-row;var a=At(row,height);var b=At(row+1,height);
        return new CylinderPose(a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t,a.UpY+(b.UpY-a.UpY)*t,a.UpZ+(b.UpZ-a.UpZ)*t,a.Pitch+(b.Pitch-a.Pitch)*t);
    }
}
public readonly struct CylinderMotion
{
    public readonly float Sin,Cos,Y,Z,HalfSin,HalfCos;
    public CylinderMotion(float focusZ)
    {
        float angle=-focusZ/CylinderWorld.Radius;
        Sin=(float)Math.Sin(angle);Cos=(float)Math.Cos(angle);HalfSin=(float)Math.Sin(angle*.5f);HalfCos=(float)Math.Cos(angle*.5f);
        Y=CylinderWorld.Radius*(Cos-1);Z=focusZ+CylinderWorld.Radius*Sin;
    }
    public CylinderPose Apply(CylinderPose p)=>new CylinderPose(Cos*p.Y-Sin*p.Z+Y,Sin*p.Y+Cos*p.Z+Z,Cos*p.UpY-Sin*p.UpZ,Sin*p.UpY+Cos*p.UpZ,p.Pitch+(float)Math.Atan2(Sin,Cos));
    // Culling only needs the point; avoid per-bound normal/angle work.
    public void Point(float y,float z,out float worldY,out float worldZ){worldY=Cos*y-Sin*z+Y;worldZ=Sin*y+Cos*z+Z;}
}
