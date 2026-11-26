namespace AnimalTown.Online;

public static class OnlineMovementRules
{
    public const float WalkPerSecond=2.5f,RunPerSecond=4.5f;
    public static bool Move(GameState world,int room,OnlineMovementInput input,float seconds,ref float x,ref float z,out float yaw)
    {
        if(seconds<0||seconds>.1001f)throw new ArgumentOutOfRangeException(nameof(seconds));
        float dx=input.X/127f,dz=input.Z/127f,length=(float)Math.Sqrt(dx*dx+dz*dz);
        yaw=length>.01f?(float)Math.Atan2(dx,dz):0;
        if(length>1){dx/=length;dz/=length;}
        float step=(input.Run?RunPerSecond:WalkPerSecond)*seconds;
        if(BuildingWorld.Segment(world,room,x,z,x+dx*step,z))x+=dx*step;
        if(BuildingWorld.Segment(world,room,x,z,x,z+dz*step))z+=dz*step;
        return length>.01f;
    }
}
