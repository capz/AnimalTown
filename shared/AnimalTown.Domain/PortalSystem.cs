namespace AnimalTown;

public enum DoorPhase { Idle, Waiting, Opening, Crossing, Closing }
public sealed class PortalSystem
{
    public const int Duration=36,CommitTick=18,MaximumWait=180;
    public sealed class Transition
    {
        public DoorPhase Phase;
        public int Building=-1,Tick,Wait,Cooldown;
        public bool Committed;
        public ActorLocation Source,Destination;
    }
    public readonly Transition[] Actors=new Transition[BuildingWorld.Actors];
    public readonly bool[] Ready={true,true,true,true};
    private readonly int[] owners=new int[BuildingWorld.Count];
    public PortalSystem(){for(int i=0;i<Actors.Length;i++)Actors[i]=new Transition();for(int i=0;i<owners.Length;i++)owners[i]=-1;}
    public bool Busy(int actor)=>Actors[actor].Phase!=DoorPhase.Idle;
    public int Owner(int building)=>owners[building];
    public bool Request(GameState s,int actor,int building)
    {
        if(actor<0||actor>=Actors.Length||!BuildingWorld.Exists(s,building))return false;
        var t=Actors[actor];if(Busy(actor)||t.Cooldown>0||!BuildingCatalog.Available(s.BuildingTypes[building])||!Ready[s.BuildingTypes[building]])return false;
        var p=BuildingWorld.Location(s,actor);int destination;
        if(p.Space.IsTown){destination=BuildingWorld.Space(building);BuildingWorld.Entrance(s,building,out float x,out float z);if(Distance(p.X,p.Z,x,z)>.95f)return false;BuildingWorld.InsideDoor(s,destination,out x,out z);t.Destination=new ActorLocation(destination,x,z-.7f,(float)Math.PI);}
        else{if(BuildingWorld.Building(p.Space.Value)!=building)return false;BuildingWorld.InsideDoor(s,p.Space.Value,out float x,out float z);if(Distance(p.X,p.Z,x,z)>.95f)return false;BuildingWorld.Entrance(s,building,out x,out z);t.Destination=new ActorLocation(0,x,z+1f,0);}
        t.Source=new ActorLocation(p.Space.Value,p.X,p.Z,p.Space.IsTown?(float)Math.PI:0);BuildingWorld.SetLocation(s,actor,t.Source);t.Building=building;t.Tick=t.Wait=0;t.Committed=false;t.Phase=DoorPhase.Waiting;return true;
    }
    public int Nearby(GameState s,int actor,float facing)
    {
        var p=BuildingWorld.Location(s,actor);if(!p.Space.IsTown){BuildingWorld.InsideDoor(s,p.Space.Value,out float x,out float z);return Distance(p.X,p.Z,x,z)<.95f?BuildingWorld.Building(p.Space.Value):-1;}
        int best=-1;float distance=.95f;
        for(int b=0;b<BuildingWorld.Count;b++)if(BuildingWorld.Exists(s,b)){BuildingWorld.Entrance(s,b,out float x,out float z);float d=Distance(p.X,p.Z,x,z);
            if(d<distance&&(z-p.Z)*(float)Math.Cos(facing)+(x-p.X)*(float)Math.Sin(facing)>-.1f){best=b;distance=d;}}
        return best;
    }
    public void Tick(GameState s)
    {
        for(int actor=0;actor<Actors.Length;actor++){
            var t=Actors[actor];if(t.Cooldown>0)t.Cooldown--;if(t.Phase==DoorPhase.Idle)continue;
            var current=BuildingWorld.Location(s,actor);
            if(!BuildingWorld.Exists(s,t.Building)||current.Space.Value!=(t.Committed?t.Destination.Space.Value:t.Source.Space.Value)){Cancel(actor);continue;}
            if(t.Phase==DoorPhase.Waiting){
                if(++t.Wait>MaximumWait){Cancel(actor);continue;}
                if(owners[t.Building]>=0)continue;
                // Entry and exit landings must both be available before opening.
                if(!BuildingWorld.CanStand(s,t.Destination.Space.Value,t.Destination.X,t.Destination.Z,actor,true))continue;
                owners[t.Building]=actor;t.Phase=DoorPhase.Opening;t.Tick=0;
            }
            if(++t.Tick==CommitTick){
                if(!BuildingWorld.CanStand(s,t.Destination.Space.Value,t.Destination.X,t.Destination.Z,actor,true)){Cancel(actor);continue;}
                BuildingWorld.SetLocation(s,actor,t.Destination);t.Committed=true;
            }
            t.Phase=t.Tick<10?DoorPhase.Opening:t.Tick<26?DoorPhase.Crossing:DoorPhase.Closing;
            if(t.Tick>=Duration){owners[t.Building]=-1;t.Phase=DoorPhase.Idle;t.Cooldown=30;}
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
