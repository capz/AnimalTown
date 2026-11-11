namespace AnimalTown;

// Stable space keys: 0 town, 1..4 player rooms, 16..21 resident homes,
// 24 shop, 25 post office. These keys are independent of mesh filenames.
public readonly struct SpaceId
{
    public readonly int Value;
    public SpaceId(int value){Value=value;}
    public bool IsTown=>Value==0;
}
public readonly struct ActorLocation
{
    public readonly SpaceId Space;
    public readonly float X,Z,Yaw;
    public ActorLocation(int space,float x,float z,float yaw){Space=new SpaceId(space);X=x;Z=z;Yaw=yaw;}
}
public sealed class BuildingDefinition
{
    public readonly string Id,Exterior,Door,Floor,North,East,South,West;
    public readonly int InteriorSize;
    public readonly float HingeX,HingeZ,DoorX,DoorZ;
    public readonly float[] Fixtures,Activities;
    public readonly float DoorInset,CameraHeightScale,CameraHeightOffset,CameraBackScale,CameraBackOffset;
    public BuildingDefinition(string id,int size,string exterior,string door,string floor,string north,string east,string south,string west,float hingeX=.95f,float hingeZ=3.02f,float doorX=1.5f,float doorZ=3.5f,float[]? fixtures=null,float doorInset=.65f,float cameraHeightScale=.7f,float cameraHeightOffset=2.8f,float cameraBackScale=.9f,float cameraBackOffset=1.2f,float[]? activities=null)
    {DoorInset=doorInset;CameraHeightScale=cameraHeightScale;CameraHeightOffset=cameraHeightOffset;CameraBackScale=cameraBackScale;CameraBackOffset=cameraBackOffset;Activities=activities??new float[]{3,2,1.8f,2.2f};HingeX=hingeX;HingeZ=hingeZ;DoorX=doorX;DoorZ=doorZ;Fixtures=fixtures??new float[]{.25f,.35f,1.15f,1.6f,.5f,1,2.65f,.35f,1,.7f,.8f,2};Id=id;InteriorSize=size;Exterior=exterior;Door=door;Floor=floor;North=north;East=east;South=south;West=west;}
}
public static class BuildingCatalog
{
    public const int MaximumDefinitions=4;
    public static readonly BuildingDefinition Cottage=new BuildingDefinition("cottage_small",4,"cottage-exterior","cottage-door","cottage-floor","cottage-north","cottage-east","cottage-south","cottage-west");
    private static BuildingDefinition[] definitions={Cottage};
    public static int Count=>definitions.Length;
    public static BuildingDefinition Get(int id)=>id>=0&&id<definitions.Length?definitions[id]:Cottage;
    public static bool Available(int id)=>id>=0&&id<definitions.Length;
    // Explicit bounded binary descriptor, compiled from JSON by host tools.
    public static void Configure(byte[] data)
    {
        int at=0;
        int Int(){if(at>data.Length-4)throw new InvalidOperationException("Truncated building catalog");int n=0;for(int j=0;j<4;j++)n|=data[at++]<<(j*8);return n;}
        string Text(){int n=Int();if(n<1||n>64||at>data.Length-n)throw new InvalidOperationException("Invalid building name");var chars=new char[n];for(int j=0;j<n;j++){char c=(char)data[at++];if(!(c>='a'&&c<='z'||c>='0'&&c<='9'||c=='_'||c=='-'))throw new InvalidOperationException("Invalid building identifier");chars[j]=c;}return new string(chars);}
        if(Int()!=0x424c4447||Int()!=2)throw new InvalidOperationException("Unsupported building catalog");int count=Int();if(count<1||count>MaximumDefinitions)throw new InvalidOperationException("Building catalog budget");
        var next=new BuildingDefinition[count];for(int i=0;i<count;i++){string id=Text();int size=Int();if(size<4||size>16)throw new InvalidOperationException("Interior size budget");float hx=Int()/1024f,hz=Int()/1024f,dx=Int()/1024f,dz=Int()/1024f;float inset=Int()/1024f,ch=Int()/1024f,cho=Int()/1024f,cb=Int()/1024f,cbo=Int()/1024f;
            if(inset<.3f||inset>1||ch<.4f||ch>1.2f||cho<1||cho>4||cb<.6f||cb>1.5f||cbo<.5f||cbo>3)throw new InvalidOperationException("Interior camera/socket bounds");
            int activitiesCount=Int();if(activitiesCount<1||activitiesCount>8)throw new InvalidOperationException("Activity budget");var activities=new float[activitiesCount*2];for(int k=0;k<activities.Length;k++){activities[k]=Int()/1024f;if(activities[k]<BuildingWorld.Radius||activities[k]>size-BuildingWorld.Radius)throw new InvalidOperationException("Activity outside room");}
            int countFixtures=Int();if(countFixtures<0||countFixtures>16||hx<0||hx>3||hz<2.8f||hz>3.3f||dx<.55f||dx>2.45f||dz<3.25f||dz>4)throw new InvalidOperationException("Building socket bounds");
            var fixtures=new float[countFixtures*6];for(int f=0;f<fixtures.Length;f++){fixtures[f]=Int()/1024f;if(fixtures[f]<0||fixtures[f]>16)throw new InvalidOperationException("Fixture bounds");}
            for(int f=0;f<fixtures.Length;f+=6)if(fixtures[f+2]<=0||fixtures[f+3]<=0||fixtures[f+4]<=0||fixtures[f+4]>3||fixtures[f]+fixtures[f+2]>size||fixtures[f+1]+fixtures[f+3]>size||fixtures[f+5]>2||fixtures[f+5]!=(int)fixtures[f+5]||BuildingWorld.DoorClearance(size,fixtures[f],fixtures[f+1],fixtures[f+2],fixtures[f+3]))throw new InvalidOperationException("Invalid fixture layout");
            next[i]=new BuildingDefinition(id,size,Text(),Text(),Text(),Text(),Text(),Text(),Text(),hx,hz,dx,dz,fixtures,inset,ch,cho,cb,cbo,activities);for(int j=0;j<i;j++)if(next[j].Id==id)throw new InvalidOperationException("Duplicate building ID");}
        if(at!=data.Length||next[0].Id!="cottage_small")throw new InvalidOperationException("Invalid building catalog layout");definitions=next;
    }
}
public static class BuildingWorld
{
    public const int Count=12,Actors=7;
    public static int PlayerBuilding(int profile)=>profile==0?0:8+profile;
    public static int PlayerRoom(int profile,int room=1)=>profile==0?room:28+profile*4+room-1;
    public static int RoomProfile(int space)=>space>=1&&space<=4?0:space>=32&&space<=43?(space-32)/4+1:-1;
    public static int PersonalRoom(int space)=>space>=1&&space<=4?space:space>=32&&space<=43?(space-32)%4+1:0;
    public static int BuildingProfile(int building)=>building==0?0:building>=9&&building<12?building-8:-1;
    public const float Radius=.22f;
    public static int Space(int building)=>building==0?1:building<=6?15+building:building<=8?17+building:PlayerRoom(building-8);
    public static int Building(int space)=>space>=1&&space<=4?0:space>=16&&space<=21?space-15:space==24?7:space==25?8:space>=32&&space<=43?PlayerBuilding(RoomProfile(space)):-1;
    public static bool ValidSpace(int space)=>space==0||Building(space)>=0;
    public static bool Exists(GameState s,int building)=>building>=0&&building<Count&&(BuildingProfile(building)<0||s.HomePlaced(BuildingProfile(building)));
    public static bool Available(GameState s,int space)=>space==0||Exists(s,Building(space))&&BuildingCatalog.Available(s.BuildingTypes[Building(space)]);
    public static BuildingDefinition Definition(GameState s,int building)=>BuildingCatalog.Get(s.BuildingTypes[building]);
    public static int Size(GameState s,int space)
    {
        int b=Building(space);if(b<0)return 0;
        int profile=RoomProfile(space),room=PersonalRoom(space);
        if(profile>=0&&room==1&&s.BuildingTypes[PlayerBuilding(profile)]==0){int tier=s.HomeTier(profile);return tier==0?4:tier==1?6:8;}
        if(profile>=0&&room>=2)return room==3?8:6;
        return Definition(s,b).InteriorSize;
    }
    public static void Origin(GameState s,int building,out float x,out float z)
    {if(BuildingProfile(building)>=0){int p=BuildingProfile(building);x=s.HomeX(p)+2;z=s.HomeZ(p)+1;}else if(building<=6){x=s.Town.Residents[building-1].HomeX;z=s.Town.Residents[building-1].HomeZ;}else{x=building==7?31:49;z=25;}}
    public static void Entrance(GameState s,int building,out float x,out float z)
    {Origin(s,building,out x,out z);var d=Definition(s,building);x+=d.DoorX;z+=d.DoorZ;}
    public static void InsideDoor(GameState s,int space,out float x,out float z)
    {int size=Size(s,space);x=size*.5f;z=size-Definition(s,Building(space)).DoorInset;}
    public static ActorLocation Location(GameState s,int actor)
    {if(actor==0)return new ActorLocation(s.Space,s.X,s.Z,s.Facing);var v=s.Town.Residents[actor-1];return new ActorLocation(v.Space,v.PosX,v.PosZ,v.Yaw);}
    public static void SetLocation(GameState s,int actor,ActorLocation p)
    {if(actor==0){s.Space=p.Space.Value;s.X=p.X;s.Z=p.Z;s.Facing=p.Yaw;}else{var v=s.Town.Residents[actor-1];v.Space=p.Space.Value;v.PosX=p.X;v.PosZ=p.Z;v.Yaw=p.Yaw;if(v.Space==0){v.X=(int)p.X;v.Z=(int)p.Z;}}}
    public static bool DoorClearance(int size,float x,float z,float w,float d)
        =>x<size*.5f+.8f&&x+w>size*.5f-.8f&&z+d>size-1.6f;
    // Simple original fixtures are shared geometry and collision data. Player
    // rooms preserve legacy furnishing; neighbours have a bed and writing desk.
    public static bool Fixture(GameState s,int space,int index,out float x,out float z,out float w,out float d,out float height)
    {
        x=z=w=d=height=0;if(space<16||space>21)return false;
        var data=Definition(s,Building(space)).Fixtures;int at=index*6;if(at<0||at+5>=data.Length)return false;
        x=data[at];z=data[at+1];w=data[at+2];d=data[at+3];height=data[at+4];return true;
    }
    public static int FixtureKind(GameState s,int space,int index)=>(int)Definition(s,Building(space)).Fixtures[index*6+5];
    public static bool CanStand(GameState s,int space,float x,float z,int actor=-1,bool people=false)
    {
        if(!Available(s,space))return false;
        if(space==0){if(!s.CanWalk(x,z,0))return false;}
        else{
            int size=Size(s,space);if(!(x>=Radius&&z>=Radius&&x<size-Radius&&z<size-Radius))return false;
            for(int i=0;i<16;i++)if(Fixture(s,space,i,out float fx,out float fz,out float fw,out float fd,out float _)&&Overlaps(x,z,fx,fz,fw,fd))return false;
            for(int i=0;i<s.Town.PlacedCount;i++){var p=s.Town.Placed[i];if(p.Room!=space)continue;Footprint(p,out int w,out int d);if(Overlaps(x,z,p.X,p.Z,w,d))return false;}
        }
        if(people)for(int i=0;i<Actors;i++)if(i!=actor){var p=Location(s,i);if(p.Space.Value==space&&Math.Abs(p.X-x)<Radius*2&&Math.Abs(p.Z-z)<Radius*2)return false;}
        return true;
    }
    private static bool Overlaps(float x,float z,float bx,float bz,float w,float d)=>x>bx-Radius&&x<bx+w+Radius&&z>bz-Radius&&z<bz+d+Radius;
    public static void Footprint(Placement p,out int w,out int d)
    {w=Catalog.Items[p.Item].Width;d=Catalog.Items[p.Item].Height;if((p.Rotation&1)!=0){int swap=w;w=d;d=swap;}}
    public static bool Segment(GameState s,int space,float ax,float az,float bx,float bz,int actor=-1,bool people=false)
    {
        int steps=1+(int)(Math.Max(Math.Abs(bx-ax),Math.Abs(bz-az))/.1f);if(steps>256)return false;
        for(int i=1;i<=steps;i++)if(!CanStand(s,space,ax+(bx-ax)*i/steps,az+(bz-az)*i/steps,actor,people))return false;
        return space!=0||TerrainElevation.Traverse(s.Town,ax,az,bx,bz);
    }
}
