namespace AnimalTown;

// Fixed pitch, four cardinal views. Rendering and movement consume this same
// interpolated basis; the outdoor cylinder camera is deliberately independent.
public sealed class InteriorCamera
{
    public int Quarter {get;private set;}
    public float Angle {get;private set;}
    private float target;private bool armed=true;
    private BuildingDefinition definition=BuildingCatalog.Cottage;
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
        float ex=EyeX(size),ey=EyeY(size),ez=EyeZ(size),fx=size*.5f-ex,fy=.7f-ey,fz=size*.5f-ez;
        float len=(float)Math.Sqrt(fx*fx+fy*fy+fz*fz);fx/=len;fy/=len;fz/=len;
        float horizontal=(float)Math.Sqrt(fx*fx+fz*fz),rx=-fz/horizontal,rz=fx/horizontal;
        float ux=-fy*rz,uy=fx*rz-fz*rx,uz=fy*rx;
        float dx=x-ex,dy=y-ey,dz=z-ez,depth=dx*fx+dy*fy+dz*fz,side=dx*rx+dz*rz,up=dx*ux+dy*uy+dz*uz;
        float tan=RenderView.TanY;return depth+radius>=.1f&&depth-radius<=64&&Math.Abs(side)<=depth*tan*4/3+radius*1.3f&&Math.Abs(up)<=depth*tan+radius*1.3f;
    }
}
