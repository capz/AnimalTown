namespace AnimalTown;

public enum DoorPhase { Idle, Waiting, Opening, Crossing, Closing }
public sealed class PortalSystem
{
    public const int Duration=36,CommitTick=18,MaximumWait=180;
    public const float PlayerEntryRadius=1.6f,DoorRadius=.95f;
    public sealed class Transition
    {
        public DoorPhase Phase;
        public int Building=-1,Tick,Wait,Cooldown,Type;
        public float OriginX,OriginZ;
        public bool Committed,WaitMoving;
        public ActorLocation Source,Destination;
    }
    public readonly Transition[] Actors=new Transition[BuildingWorld.Actors];
    public readonly bool[] Ready={true,true,true,true};
    // Rendering admission is transient, separate from art availability and saves.
    // Existing callers keep their original behavior until a residency owner gates it.
    public readonly bool[] ResourcesReady={true,true,true,true,true,true,true};
    private readonly int[] owners=new int[BuildingWorld.Count];
    public PortalSystem(){for(int i=0;i<Actors.Length;i++)Actors[i]=new Transition();for(int i=0;i<owners.Length;i++)owners[i]=-1;}
    public bool Busy(int actor)=>Actors[actor].Phase!=DoorPhase.Idle;
    public int Owner(int building)=>owners[building];
    public bool Request(GameState s,int actor,int building)
    {
        if(actor<0||actor>=Actors.Length||!BuildingWorld.Exists(s,building))return false;
        var t=Actors[actor];if(Busy(actor)||t.Cooldown>0||!BuildingCatalog.Available(s.BuildingTypes[building])||!Ready[s.BuildingTypes[building]])return false;
        var p=BuildingWorld.Location(s,actor);int destination;
        if(p.Space.IsTown){destination=BuildingWorld.Space(building);if(!ExteriorReach(s,actor,building,p,out _))return false;BuildingWorld.InsideDoor(s,destination,out float x,out float z);t.Destination=new ActorLocation(destination,x,z-.7f,(float)Math.PI);}
        else{if(BuildingWorld.Building(p.Space.Value)!=building)return false;BuildingWorld.InsideDoor(s,p.Space.Value,out float x,out float z);if(Distance(p.X,p.Z,x,z)>DoorRadius)return false;BuildingWorld.Entrance(s,building,out x,out z);t.Destination=new ActorLocation(0,x,z+1f,0);}
        t.Source=new ActorLocation(p.Space.Value,p.X,p.Z,p.Space.IsTown?(float)Math.PI:0);BuildingWorld.SetLocation(s,actor,t.Source);t.Building=building;BuildingWorld.Origin(s,building,out t.OriginX,out t.OriginZ);t.Type=s.BuildingTypes[building];t.Tick=t.Wait=0;t.Committed=false;t.Phase=DoorPhase.Waiting;return true;
    }
    // Both the prompt and the actual request use the same approach test.
    private static bool ExteriorReach(GameState s,int actor,int building,ActorLocation p,out float distance)
    {
        BuildingWorld.Entrance(s,building,out float x,out float z);distance=Distance(p.X,p.Z,x,z);
        if(distance>(actor==0?PlayerEntryRadius+.0001f:DoorRadius))return false; // coordinate rounding at the boundary
        if(actor!=0)return true;
        // The extra reach stays on the front approach, never through the side
        // or back wall. Trees, furniture, water and cliff steps block the walk.
        return p.Z>=z-.25f&&BuildingWorld.Segment(s,0,p.X,p.Z,x,z,actor);
    }
    public int Nearby(GameState s,int actor,float facing)
    {
        var p=BuildingWorld.Location(s,actor);if(!p.Space.IsTown){BuildingWorld.InsideDoor(s,p.Space.Value,out float x,out float z);return Distance(p.X,p.Z,x,z)<=DoorRadius?BuildingWorld.Building(p.Space.Value):-1;}
        int best=-1;float distance=float.MaxValue;
        for(int b=0;b<BuildingWorld.Count;b++)if(BuildingWorld.Exists(s,b)&&ExteriorReach(s,actor,b,p,out float d)){
            BuildingWorld.Entrance(s,b,out float x,out float z);
            if(d<distance&&(z-p.Z)*(float)Math.Cos(facing)+(x-p.X)*(float)Math.Sin(facing)>-.1f){best=b;distance=d;}
        }
        return best;
    }
    public void Tick(GameState s)
    {
        for(int actor=0;actor<Actors.Length;actor++){
            var t=Actors[actor];t.WaitMoving=false;if(t.Cooldown>0)t.Cooldown--;if(t.Phase==DoorPhase.Idle)continue;
            var current=BuildingWorld.Location(s,actor);
            if(!BuildingWorld.Exists(s,t.Building)||current.Space.Value!=(t.Committed?t.Destination.Space.Value:t.Source.Space.Value)){Cancel(actor);continue;}
            BuildingWorld.Origin(s,t.Building,out float ox,out float oz);
            var expected=t.Committed?t.Destination:t.Source;
            if(ox!=t.OriginX||oz!=t.OriginZ||s.BuildingTypes[t.Building]!=t.Type||!Ready[t.Type]||Distance(current.X,current.Z,expected.X,expected.Z)>.01f){Cancel(actor);continue;}
            if(t.Phase==DoorPhase.Waiting){
                if(++t.Wait>MaximumWait){Cancel(actor);continue;}
                if(!ResourcesReady[actor])continue;
                if(owners[t.Building]>=0){StepAside(s,actor,t);continue;}
                // Entry and exit landings must both be available before opening.
                if(!BuildingWorld.CanStand(s,t.Destination.Space.Value,t.Destination.X,t.Destination.Z,actor,true))continue;
                owners[t.Building]=actor;t.Phase=DoorPhase.Opening;t.Tick=t.Wait=0;
                t.Source=new ActorLocation(t.Source.Space.Value,t.Source.X,t.Source.Z,t.Source.Space.IsTown?(float)Math.PI:0);BuildingWorld.SetLocation(s,actor,t.Source);
            }
            if(++t.Tick==CommitTick){
                if(!ResourcesReady[actor]||!CorridorClear(s,actor,t)||!BuildingWorld.CanStand(s,t.Destination.Space.Value,t.Destination.X,t.Destination.Z,actor,true)){
                    t.Tick--;if(++t.Wait>MaximumWait)Cancel(actor);continue;
                }
                BuildingWorld.SetLocation(s,actor,t.Destination);t.Committed=true;
            }
            t.Phase=t.Tick<10?DoorPhase.Opening:t.Tick<26?DoorPhase.Crossing:DoorPhase.Closing;
            if(t.Tick>=Duration){owners[t.Building]=-1;t.Phase=DoorPhase.Idle;t.Cooldown=30;}
        }
    }
    public bool Moving(int actor)
    {var t=Actors[actor];return t.WaitMoving||Busy(actor)&&t.Phase!=DoorPhase.Waiting&&t.Tick>=8&&t.Tick<28;}
    private static bool CorridorClear(GameState s,int actor,Transition t)
    {
        float x,z;
        if(t.Destination.Space.IsTown){BuildingWorld.Entrance(s,t.Building,out x,out z);}
        else BuildingWorld.InsideDoor(s,t.Destination.Space.Value,out x,out z);
        for(int other=0;other<BuildingWorld.Actors;other++)if(other!=actor){var p=BuildingWorld.Location(s,other);if(p.Space.Value==t.Destination.Space.Value&&Math.Abs(p.X-x)<BuildingWorld.Radius*2&&Math.Abs(p.Z-z)<1)return false;}
        return true;
    }
    private static void StepAside(GameState s,int actor,Transition t)
    {
        var p=BuildingWorld.Location(s,actor);float x,z;
        if(p.Space.IsTown){BuildingWorld.Entrance(s,t.Building,out x,out z);z+=.65f;}
        else{BuildingWorld.InsideDoor(s,p.Space.Value,out x,out z);z-=.7f;}
        for(int side=0;side<2;side++){
            float gx=x+(side==0?.8f:-.8f),dx=gx-p.X,dz=z-p.Z,distance=(float)Math.Sqrt(dx*dx+dz*dz);if(distance<.03f)return;
            float step=Math.Min(.05f,distance),nx=p.X+dx/distance*step,nz=p.Z+dz/distance*step;
            if(!BuildingWorld.Segment(s,p.Space.Value,p.X,p.Z,nx,nz,actor,true))continue;
            t.Source=new ActorLocation(p.Space.Value,nx,nz,PlayerMovement.Yaw(dx,dz));BuildingWorld.SetLocation(s,actor,t.Source);t.WaitMoving=true;return;
        }
    }
    public void Cancel(int actor)
    {var t=Actors[actor];if(t.Building>=0&&owners[t.Building]==actor)owners[t.Building]=-1;t.Phase=DoorPhase.Idle;t.Cooldown=30;}
    public float Openness(int building)
    {int actor=owners[building];if(actor<0)return 0;int tick=Actors[actor].Tick;return tick<10?tick/10f:tick<26?1:(Duration-tick)/10f;}
    public float PlayerFade
    {get{var t=Actors[0];if(t.Phase==DoorPhase.Idle||t.Phase==DoorPhase.Waiting)return 0;return t.Tick<10?0:t.Tick<16?(t.Tick-10)/6f:t.Tick<20?1:t.Tick<28?(28-t.Tick)/8f:0;}}
    public ActorLocation VisualLocation(GameState s,int actor)
    {
        var t=Actors[actor];var p=BuildingWorld.Location(s,actor);if(t.Phase==DoorPhase.Idle||t.Phase==DoorPhase.Waiting)return p;
        float tx,tz;
        if(p.Space.IsTown){BuildingWorld.Origin(s,t.Building,out tx,out tz);tx+=BuildingWorld.Definition(s,t.Building).DoorX;tz+=3.05f;}
        else{tx=BuildingWorld.Size(s,p.Space.Value)*.5f;tz=BuildingWorld.Size(s,p.Space.Value)-.05f;}
        float amount=t.Committed?Math.Max(0,Math.Min(1,(28-t.Tick)/10f)):Math.Max(0,Math.Min(1,(t.Tick-8)/10f));
        return new ActorLocation(p.Space.Value,p.X+(tx-p.X)*amount,p.Z+(tz-p.Z)*amount,p.Yaw);
    }
    private static float Distance(float ax,float az,float bx,float bz){float dx=ax-bx,dz=az-bz;return (float)Math.Sqrt(dx*dx+dz*dz);}
}
