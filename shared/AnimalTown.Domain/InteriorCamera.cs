namespace AnimalTown;

// Fixed pitch, four cardinal views. Rendering and movement consume this same
// interpolated basis; the outdoor cylinder camera is deliberately independent.
public sealed class InteriorCamera
{
    public int Quarter {get;private set;}
    public float Angle {get;private set;}
    private float target;private bool armed=true;
    private BuildingDefinition definition=BuildingCatalog.Cottage;
    private BuildingDefinition? cachedDefinition;
    private float cachedAngle=float.NaN,cachedSize;
    private readonly Hypercube.CameraFrustum frustum=new Hypercube.CameraFrustum(2,5.6f,6.8f,2,.7f,2,0,1,0,CylinderWorld.CameraFieldOfView,4f/3,.1f,64);
    public void Configure(BuildingDefinition value){definition=value;}
    public void Reset(){Quarter=0;Angle=target=0;armed=true;}
    public void Tick(float cstick,bool recenter)
    {
        if(recenter){Quarter=0;target=0;}
        if(Math.Abs(cstick)<.25f)armed=true;
        if(armed&&Math.Abs(cstick)>.65f){Quarter+=(cstick>0?1:-1);target=Quarter*(float)Math.PI*.5f;armed=false;}
        float delta=target-Angle;Angle+=Math.Abs(delta)<.005f?delta:delta*.25f;
        if(Math.Abs(Angle)>100){float wrap=(float)Math.PI*2*(int)(Angle/((float)Math.PI*2));Angle-=wrap;target-=wrap;Quarter=(int)Math.Round(target/((float)Math.PI*.5f));}
    }
    public void Movement(float x,float y,out float worldX,out float worldY)
    {float sin=(float)Math.Sin(Angle),cos=(float)Math.Cos(Angle);worldX=x*cos-y*sin;worldY=x*sin+y*cos;}
    public float EyeX(float size)=>size*.5f+(float)Math.Sin(Angle)*(size*definition.CameraBackScale+definition.CameraBackOffset);
    public float EyeZ(float size)=>size*.5f+(float)Math.Cos(Angle)*(size*definition.CameraBackScale+definition.CameraBackOffset);
    public float EyeY(float size)=>size*definition.CameraHeightScale+definition.CameraHeightOffset;
    public bool WallVisible(int wall)
    {
        float dot=wall==0?-(float)Math.Cos(Angle):wall==1?(float)Math.Sin(Angle):wall==2?(float)Math.Cos(Angle):-(float)Math.Sin(Angle);
        return dot<-.05f;
    }
    public bool Sphere(float x,float y,float z,float radius,float size)
    {
        if(cachedAngle!=Angle||cachedSize!=size||cachedDefinition!=definition){
            frustum.Set(EyeX(size),EyeY(size),EyeZ(size),size*.5f,.7f,size*.5f,0,1,0,CylinderWorld.CameraFieldOfView,4f/3,.1f,64);
            cachedAngle=Angle;cachedSize=size;cachedDefinition=definition;
        }
        return frustum.Sphere(x,y,z,radius+.025f);
    }
}
