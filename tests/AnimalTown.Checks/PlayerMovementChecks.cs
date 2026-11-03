using AnimalTown;

internal static class PlayerMovementChecks
{
    internal static void Run(Action<bool,string> check)
    {
        check(Math.Abs(PlayerMovement.WalkStep/.085f-1.05f)<.000001f&&Math.Abs(PlayerMovement.RunStep/.16f-1.05f)<.000001f,"walk and run are exactly five percent faster");
        var state=new GameState(7);Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);state.Town.GroundCount=0;
        foreach(bool run in new[]{false,true})for(int input=0;input<2;input++)for(int z=-1;z<=1;z++)for(int x=-1;x<=1;x++){
            if(x==0&&z==0)continue;state.X=state.Z=50.5f;
            int buttons=(x<0?1:x>0?2:0)|(z<0?8:z>0?4:0);
            bool moved=PlayerMovement.Move(state,0,input==0?x:0,input==0?-z:0,input==1?buttons:0,run,out float hx,out float hz);
            float dx=state.X-50.5f,dz=state.Z-50.5f,speed=run?PlayerMovement.RunStep:PlayerMovement.WalkStep;
            check(moved&&Math.Abs(Math.Sqrt(dx*dx+dz*dz)-speed)<.00001,"eight-way stick and D-pad movement share the same maximum speed");
            float yaw=PlayerMovement.Yaw(hx,hz),length=(float)Math.Sqrt(hx*hx+hz*hz);
            check(Math.Abs(Math.Sin(yaw)-hx/length)<.000001&&Math.Abs(Math.Cos(yaw)-hz/length)<.000001,"player forward yaw matches the complete movement vector");
            PlayerMovement.Target(hx,hz,out int tx,out int tz);check(tx==x&&tz==z,"interaction target follows diagonal as well as cardinal facing");
        }
        state.X=state.Z=50.5f;
        check(!PlayerMovement.Move(state,0,.1f,.1f,0,false,out _,out _)&&state.X==50.5f&&state.Z==50.5f,"radial dead zone suppresses idle drift");
        check(PlayerMovement.Move(state,0,.14f,-.14f,0,false,out float slowX,out float slowZ)&&slowX>0&&slowZ>0,"light diagonal tilt is not discarded by per-axis dead zones");
        state.X=state.Z=50.5f;PlayerMovement.Move(state,0,.5f,0,4,false,out float priorityX,out float priorityZ);
        check(priorityX>0&&priorityZ==0&&Math.Abs(state.X-50.5f-PlayerMovement.WalkStep*.5f)<.00001,"analog input keeps proportional speed and takes priority over a duplicate D-pad mapping");
        check(!PlayerMovement.Move(state,0,0,0,15,false,out _,out _),"opposite D-pad directions cancel without movement");
        state.X=3.75f;state.Z=2;PlayerMovement.Move(state,1,1,-1,0,false,out float slideX,out float slideZ);
        check(state.X==3.75f&&state.Z>2&&slideX==0&&slideZ>0,"diagonal collision slides along an indoor wall and faces actual travel");
        state.X=state.Z=2.98f;state.Town.SetTerrain(3,2,Terrain.Water);state.Town.SetTerrain(2,3,Terrain.Water);
        check(PlayerMovement.Move(state,0,1,-1,0,false,out float blockedX,out float blockedZ)&&state.X==2.98f&&state.Z==2.98f&&blockedX>0&&blockedZ>0,"blocked diagonal cannot cut corners and can still face the obstacle for interaction");
        PlayerMovement.Target(0,0,out int idleX,out int idleZ);check(idleX==0&&idleZ==0,"zero vector does not invent a facing target");
    }
}
