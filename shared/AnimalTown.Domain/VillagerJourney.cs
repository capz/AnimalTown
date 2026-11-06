namespace AnimalTown;

// Persistent intention sits above transient routes. NPCs continue travelling
// while the player is indoors; invisible rooms never need a rendered model.
public sealed class VillagerJourney
{
    public readonly VillagerNavigation Outdoors=new VillagerNavigation();
    public readonly InteriorNavigation Indoors=new InteriorNavigation();
    public readonly PortalSystem Doors=new PortalSystem();
    public readonly bool[] Moving=new bool[6];
    private GameState? current;private int tick;
    public void Tick(GameState s)
    {
        if(current!=s){current=s;tick=0;for(int i=0;i<6;i++)if(s.Town.Residents[i].JourneyPhase==0&&s.Town.Residents[i].Dwell==0)s.Town.Residents[i].Dwell=90+i*45;}
        for(int i=0;i<6;i++)Outdoors.Paused[i]=Doors.Busy(i+1)||s.Town.Residents[i].Space!=0;
        Outdoors.Tick(s);Indoors.BeginTick();Doors.Tick(s);tick++;
        for(int i=0;i<6;i++){
            int actor=i+1;var v=s.Town.Residents[i];Moving[i]=false;
            if(Doors.Busy(actor)){Outdoors.Stop(i);Moving[i]=Doors.Actors[actor].Phase!=DoorPhase.Waiting;continue;}
            if(v.Space!=0){
                Outdoors.Stop(i);
                if(v.JourneyPhase<2){v.JourneyPhase=2;v.Dwell=0;}
                if(v.JourneyPhase==2){
                    var points=BuildingWorld.Definition(s,BuildingWorld.Building(v.Space)).Activities;int spot=i%(points.Length/2)*2;float gx=points[spot],gz=points[spot+1];
                    if(!BuildingWorld.CanStand(s,v.Space,gx,gz)){gx=BuildingWorld.Size(s,v.Space)*.5f;gz=2;}
                    if(++v.Dwell>600||!BuildingWorld.CanStand(s,v.Space,gx,gz)){v.JourneyPhase=4;v.Dwell=0;Indoors.Reset(actor);continue;}
                    Moving[i]=Indoors.Tick(s,actor,gx,gz);
                    if(Indoors.Arrived(actor)){v.JourneyPhase=3;v.Dwell=120+i*19;}
                }else if(v.JourneyPhase==3){if(v.Dwell>0)v.Dwell--;else{v.JourneyPhase=4;Indoors.Reset(actor);}}
                else{BuildingWorld.InsideDoor(s,v.Space,out float x,out float z);
                    if(Math.Abs(v.PosX-x)<.6f&&Math.Abs(v.PosZ-z)<.7f){Doors.Request(s,actor,BuildingWorld.Building(v.Space));continue;}
                    if(!BuildingWorld.CanStand(s,v.Space,x,z)){z-=.5f;if(!BuildingWorld.CanStand(s,v.Space,x,z)){x+=.5f;}}
                    Moving[i]=Indoors.Tick(s,actor,x,z);
                }
                continue;
            }
            if(v.JourneyPhase>=2&&v.JourneyPhase<=4){v.JourneyPhase=5;v.JourneyBuilding=-1;v.Dwell=0;Indoors.Reset(actor);Outdoors.Stop(i);}
            if(v.JourneyPhase==5){
                if(v.JourneyNextX<0||!s.Town.Walkable(v.JourneyNextX,v.JourneyNextZ)){v.JourneyPhase=0;v.Dwell=300+i*17;continue;}
                float dx=v.PosX-v.JourneyNextX-.5f,dz=v.PosZ-v.JourneyNextZ-.5f;
                if(dx*dx+dz*dz<.08f){v.JourneyPhase=0;v.JourneyNextX=v.JourneyNextZ=-1;v.Dwell=180;continue;}
                var r=Outdoors.Routes[i];Moving[i]=r.Moving;v.Dwell=Moving[i]?0:v.Dwell+1;
                if(v.Dwell>900){v.JourneyPhase=0;v.Dwell=180;continue;}
                if(r.GoalX!=v.JourneyNextX||r.GoalZ!=v.JourneyNextZ||r.Index>=r.Length&&r.Wait==0)Outdoors.Target(i,v.JourneyNextX,v.JourneyNextZ);
                continue;
            }
            if(v.JourneyPhase==0){
                Moving[i]=Outdoors.Routes[i].Moving;
                if(v.Dwell>0){v.Dwell--;continue;}
                // Stagger initial visits, then alternate own home and neighbours.
                int b=tick<900?i+1:1+(i+(tick/900)%6)%6;v.JourneyNextX=Outdoors.Routes[i].GoalX;v.JourneyNextZ=Outdoors.Routes[i].GoalZ;v.JourneyBuilding=b;v.JourneyPhase=1;v.Dwell=0;
                BuildingWorld.Entrance(s,b,out float x,out float z);Outdoors.Target(i,(int)x,(int)z);
            }else{
                int b=v.JourneyBuilding;v.Dwell=Outdoors.Routes[i].Moving?0:v.Dwell+1;if(v.Dwell>900||!BuildingWorld.Exists(s,b)){v.JourneyPhase=5;v.Dwell=0;continue;}
                BuildingWorld.Entrance(s,b,out float x,out float z);float dx=v.PosX-x,dz=v.PosZ-z;
                if(dx*dx+dz*dz<.7f){Outdoors.Stop(i);Doors.Request(s,actor,b);}
                else{Moving[i]=Outdoors.Routes[i].Moving;var r=Outdoors.Routes[i];if(r.GoalX!=(int)x||r.GoalZ!=(int)z||r.Index>=r.Length&&r.Wait==0)Outdoors.Target(i,(int)x,(int)z);}
            }
        }
    }
}
