namespace AnimalTown;

// Persistent intention sits above transient routes. NPCs continue travelling
// while the player is indoors; invisible rooms never need a rendered model.
public sealed class VillagerJourney
{
    public readonly VillagerNavigation Outdoors=new VillagerNavigation();
    public readonly InteriorNavigation Indoors=new InteriorNavigation();
    public readonly PortalSystem Doors=new PortalSystem();
    public readonly bool[] Moving=new bool[Town.NpcActorCount];
    public readonly bool[] ExternallyPaused=new bool[Town.NpcActorCount];
    private readonly Villager?[] actors=new Villager?[Town.NpcActorCount];
    private GameState? current;private int tick;
    public void Tick(GameState s)
    {
        if(current!=s){current=s;tick=0;}
        int talking=s.Town.Jobs.Initialized?s.ResidentJobs.ConversationNpc:-1;
        for(int i=0;i<actors.Length;i++){
            int actor=i+1;var v=s.NpcPresent(i)?s.Town.NpcAt(i):null;
            if(v!=actors[i]){
                actors[i]=v;Outdoors.Stop(i);Indoors.Reset(actor);
                if(Doors.Busy(actor))Doors.Cancel(actor);
                if(v!=null&&v.JourneyPhase==0&&v.Dwell==0)v.Dwell=90+i*45;
            }
            Outdoors.Paused[i]=v==null||Doors.Busy(actor)||v.Space!=0||i==talking||ExternallyPaused[i];
        }
        Outdoors.Tick(s,Doors);Indoors.BeginTick();Doors.Tick(s);tick++;
        for(int i=0;i<actors.Length;i++){
            int actor=i+1;Moving[i]=false;var v=actors[i];
            if(v==null)continue;
            if(i==talking||ExternallyPaused[i]){Outdoors.Stop(i);continue;}
            if(Doors.Busy(actor)){Outdoors.Stop(i);Moving[i]=Doors.Moving(actor);continue;}
            if(v.Space!=0){
                Outdoors.Stop(i);
                bool shopping=v.Space==TownShop.Room&&!s.NpcVisitor(i).HasValue;
                if(v.JourneyPhase<2){v.JourneyPhase=2;v.Dwell=0;}
                if(v.JourneyPhase==2){
                    var points=BuildingWorld.Definition(s,BuildingWorld.Building(v.Space)).Activities;int spot=i%(points.Length/2)*2;float gx=points[spot],gz=points[spot+1];
                    if(shopping){
                        if(v.Dwell==0)s.Town.Shop.NpcTarget[i]=s.Town.Shop.ChooseNpcDisplay(s,i);
                        int target=s.Town.Shop.NpcTarget[i];
                        if(target<0||target>=TownShop.DisplayCount+TownShop.SaleCount||!ShopLayout.Active(s,target)){v.JourneyPhase=4;Indoors.Reset(actor);continue;}
                        ShopLayout.Approach(target,out gx,out gz);
                    }
                    if(!BuildingWorld.CanStand(s,v.Space,gx,gz)){gx=BuildingWorld.Size(s,v.Space)*.5f;gz=2;}
                    if(++v.Dwell>600||!BuildingWorld.CanStand(s,v.Space,gx,gz)){v.JourneyPhase=4;v.Dwell=0;Indoors.Reset(actor);continue;}
                    Moving[i]=Indoors.Tick(s,actor,gx,gz,Doors);
                    if(Indoors.Arrived(actor)){v.JourneyPhase=3;v.Dwell=120+i*19;}
                }else if(v.JourneyPhase==3){
                    if(shopping&&v.Dwell==60){int target=s.Town.Shop.NpcTarget[i];if(target>=0)s.Town.Shop.BuyNpc(s,i,target<12?ShopSource.Display:ShopSource.Consignment,target<12?target:target-12);s.Town.Shop.NpcTarget[i]=-1;}
                    if(v.Dwell>0)v.Dwell--;else{v.JourneyPhase=4;Indoors.Reset(actor);}
                }
                else{BuildingWorld.InsideDoor(s,v.Space,out float x,out float z);
                    if(Math.Abs(v.PosX-x)<.6f&&Math.Abs(v.PosZ-z)<.7f){Doors.Request(s,actor,BuildingWorld.Building(v.Space));continue;}
                    if(!BuildingWorld.CanStand(s,v.Space,x,z)){z-=.5f;if(!BuildingWorld.CanStand(s,v.Space,x,z)){x+=.5f;}}
                    Moving[i]=Indoors.Tick(s,actor,x,z,Doors);
                }
                continue;
            }
            if(v.JourneyPhase>=2&&v.JourneyPhase<=4){v.JourneyPhase=5;v.JourneyBuilding=-1;v.Dwell=0;Indoors.Reset(actor);Outdoors.Stop(i);}
            if(v.JourneyPhase==5){
                if(v.JourneyNextX<0||!s.Town.Walkable(v.JourneyNextX,v.JourneyNextZ)){v.JourneyPhase=0;v.Dwell=300+i*17;continue;}
                float dx=v.PosX-v.JourneyNextX-.5f,dz=v.PosZ-v.JourneyNextZ-.5f;
                if(dx*dx+dz*dz<.08f){v.JourneyPhase=0;v.JourneyNextX=v.JourneyNextZ=-1;v.Dwell=180;continue;}
                var r=Outdoors.Routes[i];Moving[i]=r.Moving;
                if(Moving[i])v.Dwell=0;else if(r.CrossingPause==0)v.Dwell++;
                if(v.Dwell>900){v.JourneyPhase=0;v.Dwell=180;continue;}
                if(r.GoalX!=v.JourneyNextX||r.GoalZ!=v.JourneyNextZ||r.Index>=r.Length&&r.Wait==0&&!Outdoors.Planning(i))Outdoors.Target(i,v.JourneyNextX,v.JourneyNextZ);
                continue;
            }
            if(v.JourneyPhase==0){
                Moving[i]=Outdoors.Routes[i].Moving;
                if(v.Dwell>0){v.Dwell--;continue;}
                int b=QuietBuilding(s,i);
                if(b<0){v.Dwell=120+i*11;continue;}
                var route=Outdoors.Routes[i];
                v.JourneyNextX=route.HasDestination?route.GoalX:(int)v.PosX;
                v.JourneyNextZ=route.HasDestination?route.GoalZ:(int)v.PosZ;
                v.JourneyBuilding=b;v.JourneyPhase=1;v.Dwell=0;
                BuildingWorld.Entrance(s,b,out float x,out float z);Outdoors.Target(i,(int)x,(int)z);
            }else{
                int b=v.JourneyBuilding;var r=Outdoors.Routes[i];
                if(r.Moving)v.Dwell=0;else if(r.CrossingPause==0)v.Dwell++;
                if(v.Dwell>900||!BuildingWorld.Exists(s,b)){v.JourneyPhase=5;v.Dwell=0;continue;}
                BuildingWorld.Entrance(s,b,out float x,out float z);float dx=v.PosX-x,dz=v.PosZ-z;
                if(dx*dx+dz*dz<.7f){Outdoors.Stop(i);Doors.Request(s,actor,b);}
                else{Moving[i]=r.Moving;if(r.GoalX!=(int)x||r.GoalZ!=(int)z||r.Index>=r.Length&&r.Wait==0&&!Outdoors.Planning(i))Outdoors.Target(i,(int)x,(int)z);}
            }
        }
    }

    private int QuietBuilding(GameState s,int npc)
    {
        var villager=s.Town.NpcAt(npc);
        int cycle=tick/900;
        int homes=s.ResidentPresent(Town.LocalResidentCount)?Town.SavedVillagerCount:Town.LocalResidentCount;
        int preferred=s.NpcVisitor(npc).HasValue?7+(npc+cycle)%2
            :cycle==0?npc+1
            :(cycle+npc)%3==0?7:1+(npc+cycle)%homes;
        bool resident=s.ResidentPresent(npc);
        int best=-1;float bestScore=float.MaxValue;
        for(int building=1;building<=8;building++){
            if(building>homes&&building<7)continue;
            if(!BuildingWorld.Exists(s,building))continue;
            BuildingWorld.Entrance(s,building,out float x,out float z);
            if(!s.CanWalk(x,z,0))continue;
            int room=BuildingWorld.Space(building);
            int occupants=VillagerNavigation.NearbyActors(s,npc,room,0,0,0),residentOccupants=0;
            for(int i=0;i<Town.NpcActorCount;i++){
                if(i==npc||!s.NpcPresent(i))continue;
                var other=s.Town.NpcAt(i);
                bool approaching=other.Space==0&&other.JourneyPhase==1&&other.JourneyBuilding==building;
                if(approaching)occupants++;
                if(resident&&(approaching||other.Space==room)&&s.ResidentPresent(i))residentOccupants++;
            }
            int approach=VillagerNavigation.NearbyActors(s,npc,0,x,z,VillagerNavigation.CrowdRadius);
            float distance=Math.Abs(villager.PosX-x)+Math.Abs(villager.PosZ-z);
            float score=occupants*4096+approach*256+distance+(building==preferred?0:24);
            if(residentOccupants>=VillagerNavigation.ResidentsPerCrossing)score+=65536;
            if(score<bestScore){best=building;bestScore=score;}
        }
        return best;
    }
}
