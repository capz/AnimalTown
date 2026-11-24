namespace AnimalTown;

public static class HelipadSite
{
    public const int X=34,Z=7,Width=4,Depth=3;
    // Keep the flat mesh just above the ground to avoid coplanar depth conflicts.
    public const float CenterX=X+Width*.5f,CenterZ=Z+Depth*.5f,DeckHeight=.025f;
    public const float DoorX=CenterX-.18f,DoorZ=CenterZ+.62f,DoorHeight=DeckHeight+.44f;
    internal const float StepZ=Z+Depth+.16f;
    private static readonly float[] arrivalOffsets={0,-1,1,-2,2};

    public static bool Exists(Town town)
        =>town.At(X,Z)==(int)Terrain.Building&&town.At(X+Width-1,Z+Depth-1)==(int)Terrain.Building;

    public static bool AtGate(int x,int z)=>x>=X-2&&x<=X+Width+2&&z<=Z+Depth+2;

    public static bool TryArrival(GameState state,out float x,out float z)
    {
        if(Exists(state.Town)&&BuildingWorld.CanStand(state,0,DoorX,StepZ,0,true)){
            // Keep the walk to the pad on dry navigable ground.
            for(int row=0;row<3;row++)foreach(float offset in arrivalOffsets){
                x=CenterX+offset;z=Z+Depth+1.3f+row;
                if(BuildingWorld.Segment(state,0,DoorX,StepZ,x,z,0,true))return true;
            }
        }
        x=z=0;return false;
    }
}

public enum FlightPhase { Parked,Boarding,Takeoff,Cruise,Landing,Disembarking }

public sealed class HelipadFlight
{
    private const int BoardTicks=38,TakeoffTicks=42,LandTicks=42,ExitTicks=34;
    private int tick;
    public FlightPhase Phase {get;private set;}
    public int Direction {get;private set;}=1;
    public bool Active=>Phase!=FlightPhase.Parked;
    public bool PlayerOnboard=>Phase==FlightPhase.Takeoff||Phase==FlightPhase.Cruise||Phase==FlightPhase.Landing;
    public bool PlayerWalking=>Phase==FlightPhase.Boarding||Phase==FlightPhase.Disembarking;
    private float Progress(int duration)=>(float)tick/duration;
    private static float Ease(float t)=>t*t*(3-2*t);
    private static float Mix(float a,float b,float t)=>a+(b-a)*t;

    public void Board(int direction)
    {
        if(Active)return;
        Direction=direction<0?-1:1;Phase=FlightPhase.Boarding;tick=0;
    }

    public void Tick()
    {
        switch(Phase){
            case FlightPhase.Boarding:if(++tick>=BoardTicks){Phase=FlightPhase.Takeoff;tick=0;}break;
            case FlightPhase.Takeoff:if(++tick>=TakeoffTicks){Phase=FlightPhase.Cruise;tick=0;}break;
            case FlightPhase.Landing:if(++tick>=LandTicks){Phase=FlightPhase.Disembarking;tick=0;}break;
            case FlightPhase.Disembarking:if(++tick>=ExitTicks)Cancel();break;
        }
    }

    public void Land(){if(Phase==FlightPhase.Cruise){Phase=FlightPhase.Landing;tick=0;}}
    public void Cancel(){Phase=FlightPhase.Parked;tick=0;}

    public void PlayerPose(float standX,float standZ,out float x,out float z,out float height,out float yaw)
    {
        float t=Phase==FlightPhase.Boarding?Ease(Progress(BoardTicks)):1-Ease(Progress(ExitTicks));
        // Walk to the pad, then climb into the cabin.
        float approach=Math.Min(1,t/.55f),climb=Math.Max(0,(t-.55f)/.45f);
        x=Mix(standX,HelipadSite.DoorX,approach);
        z=Mix(Mix(standZ,HelipadSite.StepZ,approach),HelipadSite.DoorZ,climb);
        height=HelipadSite.DoorHeight*climb;
        yaw=Phase==FlightPhase.Boarding?(float)Math.PI:0;
    }

    public void CraftPose(out float x,out float height,out float yaw,out float door)
    {
        x=HelipadSite.CenterX;height=HelipadSite.DeckHeight;yaw=0;door=0;
        if(Phase==FlightPhase.Boarding){door=Math.Min(1,Progress(BoardTicks)*5);return;}
        if(Phase==FlightPhase.Disembarking){door=1;return;}
        if(Phase==FlightPhase.Takeoff){
            float t=Progress(TakeoffTicks),travel=Ease(Math.Max(0,(t-.22f)/.78f));
            x+=Direction*5.8f*travel;height+=3.35f*Ease(Math.Max(0,(t-.15f)/.85f));
            door=Math.Max(0,1-t*7);yaw=Direction<0?(float)Math.PI*travel:0;return;
        }
        if(Phase==FlightPhase.Landing){
            float t=Progress(LandTicks),remaining=1-Ease(t);
            x-=Direction*6.5f*remaining;height+=3.1f*remaining;
            yaw=Direction<0?(float)Math.PI*Math.Min(1,remaining*2):0;
        }
    }
}
