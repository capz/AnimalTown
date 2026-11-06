namespace AnimalTown;

// Fixed 30 Hz movement. Radial input preserves slow analog movement and caps
// combined axes so diagonals cannot exceed the intended walk/run speed.
public static class PlayerMovement
{
    public const float WalkStep=.085f*1.05f,RunStep=.16f*1.05f,DeadZone=.18f;
    public static bool Move(GameState state,int room,float stickX,float stickY,int buttons,bool run,out float headingX,out float headingZ)
    {
        float dx=stickX,dz=-stickY,lengthSquared=dx*dx+dz*dz;
        if(lengthSquared<DeadZone*DeadZone){dx=((buttons&2)!=0?1:0)-((buttons&1)!=0?1:0);dz=((buttons&4)!=0?1:0)-((buttons&8)!=0?1:0);lengthSquared=dx*dx+dz*dz;}
        if(lengthSquared==0){headingX=headingZ=0;return false;}
        if(lengthSquared>1){float factor=1/(float)Math.Sqrt(lengthSquared);dx*=factor;dz*=factor;}
        float speed=run?RunStep:WalkStep,oldX=state.X,oldZ=state.Z;
        float nx=oldX+dx*speed,nz=oldZ+dz*speed;
        if(state.CanWalk(nx,oldZ,room)&&(room==0||BuildingWorld.Segment(state,room,oldX,oldZ,nx,oldZ,0,true))&&(room!=0||TerrainElevation.Traverse(state.Town,oldX,oldZ,nx,oldZ)))state.X=nx;
        if(state.CanWalk(state.X,nz,room)&&(room==0||BuildingWorld.Segment(state,room,state.X,oldZ,state.X,nz,0,true))&&(room!=0||TerrainElevation.Traverse(state.Town,state.X,oldZ,state.X,nz)))state.Z=nz;
        headingX=state.X-oldX;headingZ=state.Z-oldZ;
        // Sliding follows actual travel. Fully blocked input still lets the
        // player face a tree/wall to interact with it.
        if(headingX==0&&headingZ==0){headingX=dx;headingZ=dz;}
        return true;
    }
    public static float Yaw(float x,float z)=>(float)Math.Atan2(x,z);
    public static void Target(float x,float z,out int faceX,out int faceZ)
    {
        float threshold=(float)Math.Sqrt(x*x+z*z)*.38268343f;
        faceX=Math.Abs(x)>threshold?(x<0?-1:1):0;
        faceZ=Math.Abs(z)>threshold?(z<0?-1:1):0;
    }
}
