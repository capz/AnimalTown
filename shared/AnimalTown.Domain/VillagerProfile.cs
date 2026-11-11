namespace AnimalTown;

// Personal state only. NPC locations, shared furnishings, shops and terrain stay
// in the town; relationship values belong to each playable villager.
public sealed class VillagerProfile
{
    public bool Created,HousePlaced;
    public int BackpackTier,Wallet,Species,Palette,Tool,Shirt,HouseTier,GardenTier,PendingKind,PendingTier,PendingDay,Tutorial,MetMask,Jobs,JobDay=-1,MailCount,Roof,Wall,Fence,Mailbox,Exterior,Space,HouseX=12,HouseZ=22;
    public long Savings,Debt=24000;
    public float X=18,Z=38,Facing;
    public string Name="Newcomer",Catchphrase="hello there";
    public readonly int[] Pockets=new int[30],Owned=new int[64],Mail=new int[128],Relationships=new int[24];
    public VillagerProfile(){Pockets[0]=4;Pockets[1]=2;Pockets[2]=18;}
    internal void Capture(GameState s)
    {
        BackpackTier=s.BackpackTier;
        Wallet=s.Wallet;
        Species=s.Species;
        Palette=s.Palette;
        Tool=s.Tool;
        Shirt=s.Shirt;
        HouseTier=s.HouseTier;
        GardenTier=s.GardenTier;
        PendingKind=s.PendingKind;
        PendingTier=s.PendingTier;
        PendingDay=s.PendingDay;
        Tutorial=s.Tutorial;
        MetMask=s.MetMask;
        Jobs=s.Jobs;
        JobDay=s.JobDay;
        MailCount=s.MailCount;
        Roof=s.Roof;
        Wall=s.Wall;
        Fence=s.Fence;
        Mailbox=s.Mailbox;
        Exterior=s.Exterior;
        Space=s.Space;
        HouseX=s.Town.HouseX;
        HouseZ=s.Town.HouseZ;
        Savings=s.Savings;
        Debt=s.Debt;
        X=s.X;
        Z=s.Z;
        Facing=s.Facing;
        HousePlaced=s.HousePlaced;
        Name=s.Name;
        Catchphrase=s.Catchphrase;
        Array.Copy(s.Pockets,Pockets,30);
        Array.Copy(s.Owned,Owned,64);
        Array.Copy(s.Mail,Mail,128);
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];int at=i*4;Relationships[at]=v.Friendship;Relationships[at+1]=v.Request;Relationships[at+2]=v.RequestDay;Relationships[at+3]=v.Met;}
    }
    internal void Restore(GameState s)
    {
        s.BackpackTier=BackpackTier;
        s.Wallet=Wallet;
        s.Species=Species;
        s.Palette=Palette;
        s.Tool=Tool;
        s.Shirt=Shirt;
        s.HouseTier=HouseTier;
        s.GardenTier=GardenTier;
        s.PendingKind=PendingKind;
        s.PendingTier=PendingTier;
        s.PendingDay=PendingDay;
        s.Tutorial=Tutorial;
        s.MetMask=MetMask;
        s.Jobs=Jobs;
        s.JobDay=JobDay;
        s.MailCount=MailCount;
        s.Roof=Roof;
        s.Wall=Wall;
        s.Fence=Fence;
        s.Mailbox=Mailbox;
        s.Exterior=Exterior;
        s.Space=Space;
        s.Town.HouseX=HouseX;
        s.Town.HouseZ=HouseZ;
        s.Savings=Savings;
        s.Debt=Debt;
        s.X=X;
        s.Z=Z;
        s.Facing=Facing;
        s.HousePlaced=HousePlaced;
        s.Name=Name;
        s.Catchphrase=Catchphrase;
        Array.Copy(Pockets,s.Pockets,30);
        Array.Copy(Owned,s.Owned,64);
        Array.Copy(Mail,s.Mail,128);
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];int at=i*4;v.Friendship=Relationships[at];v.Request=Relationships[at+1];v.RequestDay=Relationships[at+2];v.Met=Relationships[at+3];}
    }
    internal void Rollover(int day,int month,int previousMonth)
    {
        if(previousMonth>=0&&month>previousMonth&&Savings<=long.MaxValue-Savings/200)Savings+=Savings/200;
        Jobs=0;JobDay=day;
        if(PendingKind!=0&&day>=PendingDay){if(PendingKind==1)HouseTier=PendingTier;else GardenTier=PendingTier;PendingKind=0;}
        for(int i=0;i<MailCount;i++)if(Mail[i]<0)Mail[i]=-Mail[i];
        for(int i=0;i<6;i++){int at=i*4;if(Relationships[at+1]!=0){Relationships[at]=Math.Max(0,Relationships[at]-2);Relationships[at+1]=0;}}
    }
}

public sealed partial class GameState
{
    public const int ProfileCount=4;
    public readonly VillagerProfile[] Profiles={new VillagerProfile{Created=true},new VillagerProfile(),new VillagerProfile(),new VillagerProfile()};
    public int ActiveProfile {get;private set;}
    public void CaptureProfile()=>Profiles[ActiveProfile].Capture(this);
    internal void RestoreProfile(int profile)
    {
        if(profile<0||profile>=ProfileCount)throw new InvalidOperationException("Invalid villager profile");
        if(!Profiles[profile].Created){
            if(profile!=0)throw new InvalidOperationException("Empty town must use slot 1 as its save anchor");
            for(int i=0;i<ProfileCount;i++)if(Profiles[i].Created)throw new InvalidOperationException("Active villager is empty");
        }
        ActiveProfile=profile;Town.ActiveProfile=profile;Profiles[profile].Restore(this);
    }
    // Mutate only a private candidate; SaveRepository publishes the deletion.
    // Loose items/plants and edited terrain are shared world state, not a home.
    public void DeleteProfile(int profile)
    {
        if((uint)profile>=ProfileCount||!Profiles[profile].Created)throw new InvalidOperationException("Villager does not exist");
        CaptureProfile();int owner=profile==0?0:profile+6,building=BuildingWorld.PlayerBuilding(profile);
        BuildingWorld.Entrance(this,building,out float outsideX,out float outsideZ);
        int active=ActiveProfile,kept=0;
        for(int i=0;i<Town.PlacedCount;i++){
            var p=Town.Placed[i];if(p.Owner==owner||BuildingWorld.RoomProfile(p.Room)==profile)continue;
            var spare=Town.Placed[kept];Town.Placed[kept++]=p;Town.Placed[i]=spare;
        }
        Town.PlacedCount=kept;Profiles[profile]=new VillagerProfile();BuildingTypes[building]=0;
        if(active==profile){active=0;for(int i=0;i<ProfileCount;i++)if(Profiles[i].Created){active=i;break;}}
        RestoreProfile(active);
        bool visitors=false;
        for(int i=0;i<ProfileCount;i++)if(Profiles[i].Created&&BuildingWorld.RoomProfile(Profiles[i].Space)==profile)visitors=true;
        for(int i=0;i<6;i++)if(BuildingWorld.RoomProfile(Town.Residents[i].Space)==profile)visitors=true;
        if(visitors&&!CanWalk(outsideX,outsideZ,0)){
            bool found=false;for(int cell=0;cell<Town.Cells;cell++)if(CanWalk(cell%Town.Width+.5f,cell/Town.Width+.5f,0)){
                outsideX=cell%Town.Width+.5f;outsideZ=cell/Town.Width+.5f;found=true;break;
            }
            if(!found)throw new InvalidOperationException("Town has no safe place for visitors");
        }
        for(int i=0;i<ProfileCount;i++)if(Profiles[i].Created&&BuildingWorld.RoomProfile(Profiles[i].Space)==profile){
            Profiles[i].Space=0;Profiles[i].X=outsideX;Profiles[i].Z=outsideZ;Profiles[i].Facing=0;
        }
        for(int i=0;i<6;i++){
            var v=Town.Residents[i];
            bool visitor=BuildingWorld.RoomProfile(v.Space)==profile;
            if(visitor){v.Space=0;v.PosX=outsideX;v.PosZ=outsideZ;v.X=(int)outsideX;v.Z=(int)outsideZ;v.Yaw=0;}
            if(visitor||v.JourneyBuilding==building){v.JourneyBuilding=-1;v.JourneyPhase=0;v.Dwell=0;v.JourneyNextX=v.JourneyNextZ=-1;}
        }
        RestoreProfile(active);Town.NotifyCastersChanged();
    }
    public bool SelectProfile(int profile)
    {
        if(profile<0||profile>=ProfileCount)throw new InvalidOperationException("Invalid villager profile");
        CaptureProfile();bool created=!Profiles[profile].Created;if(created)Profiles[profile]=new VillagerProfile{Created=true};RestoreProfile(profile);
        if(created){Space=0;for(int cell=0;cell<Town.Cells&&!CanWalk(X,Z,0);cell++){X=cell%Town.Width+.5f;Z=cell/Town.Width+.5f;}}
        NormalizeBuildingLocations();return created;
    }
    public string ProfileName(int profile)=>profile==ActiveProfile?Name:Profiles[profile].Name;
    public int PlayerOwner=>ActiveProfile==0?0:ActiveProfile+6;
    public static int OwnerProfile(int owner)=>owner==0?0:owner>=7&&owner<=9?owner-6:-1;
    public bool HomePlaced(int profile)=>profile==ActiveProfile?HousePlaced:Profiles[profile].Created&&Profiles[profile].HousePlaced;
    public int HomeX(int profile)=>profile==ActiveProfile?Town.HouseX:Profiles[profile].HouseX;
    public int HomeZ(int profile)=>profile==ActiveProfile?Town.HouseZ:Profiles[profile].HouseZ;
    public int HomeTier(int profile)=>profile==ActiveProfile?HouseTier:Profiles[profile].HouseTier;
    public int HomeGardenTier(int profile)=>profile==ActiveProfile?GardenTier:Profiles[profile].GardenTier;
    public bool GardenContains(int profile,int x,int z)=>HomePlaced(profile)&&x>=HomeX(profile)&&z>=HomeZ(profile)&&x<HomeX(profile)+GardenWidths[HomeGardenTier(profile)]&&z<HomeZ(profile)+GardenHeights[HomeGardenTier(profile)];
    public bool AnyGardenContains(int x,int z){for(int p=0;p<ProfileCount;p++)if(GardenContains(p,x,z))return true;return false;}
    public bool HomeBlocks(int x,int z){for(int p=0;p<ProfileCount;p++)if(HomePlaced(p)&&x>=HomeX(p)+2&&x<=HomeX(p)+4&&z>=HomeZ(p)+1&&z<=HomeZ(p)+3)return true;return false;}
}
