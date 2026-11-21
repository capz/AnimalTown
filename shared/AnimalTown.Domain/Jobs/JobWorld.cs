namespace AnimalTown.Jobs;

public enum JobWorldKind { Empty, Ball, Snowball, Snowman }
public struct JobWorldObject
{
    public JobIdentity Id,Builder;
    public JobWorldKind Kind;
    public float X,Z,Vx,Vz,Radius;
    public long CreatedAt;
    public int Acre;
}
public struct TownNotice
{
    public JobIdentity Id,Author;
    public long At;
    public string Text;
}
public sealed partial class OfflineJobLedger
{
    public const int WorldCapacity=8,NoticeCapacity=8;
    internal readonly JobWorldObject[] World=new JobWorldObject[WorldCapacity];
    internal readonly TownNotice[] Notices=new TownNotice[NoticeCapacity];
    internal readonly int[] HomeFloors=new int[GameState.ProfileCount],HomeWalls=new int[GameState.ProfileCount];
    public int HomeFloor(int profile)=>HomeFloors[profile];
    public int HomeWall(int profile)=>HomeWalls[profile];
    internal int ObjectDay=int.MinValue,NoticeNext;
    public JobWorldObject WorldObject(int slot)=>World[slot];
    public TownNotice Notice(int slot)=>Notices[(NoticeNext+NoticeCapacity-1-slot)%NoticeCapacity];
    private void WriteWorld(JobWireWriter w)
    {
        w.Int(ObjectDay);w.Int(NoticeNext);for(int i=0;i<HomeFloors.Length;i++){w.Int(HomeFloors[i]);w.Int(HomeWalls[i]);}
        for(int i=0;i<World.Length;i++){var o=World[i];w.Int((int)o.Kind);if(o.Kind==JobWorldKind.Empty)continue;w.Key(o.Id);w.Key(o.Builder);w.Int((int)(o.X*1000));w.Int((int)(o.Z*1000));w.Int((int)(o.Radius*10000));w.Long(o.CreatedAt);w.Int(o.Acre);}
        for(int i=0;i<Notices.Length;i++){var n=Notices[i];w.Key(n.Id);if(n.Id.IsEmpty)continue;w.Key(n.Author);w.Long(n.At);w.Text(n.Text);}
    }
    private void ReadWorld(JobWireReader r)
    {
        ObjectDay=r.Int();NoticeNext=r.Range(0,NoticeCapacity-1);for(int i=0;i<HomeFloors.Length;i++){HomeFloors[i]=r.Range(0,Catalog.Items.Length-1);HomeWalls[i]=r.Range(0,Catalog.Items.Length-1);}
        for(int i=0;i<World.Length;i++){var o=new JobWorldObject{Kind=(JobWorldKind)r.Range(0,3)};if(o.Kind==JobWorldKind.Empty)continue;o.Id=r.Key(false);o.Builder=r.Key();o.X=r.Range(0,Town.Width*1000-1)/1000f;o.Z=r.Range(0,Town.Height*1000-1)/1000f;o.Radius=r.Range(1000,12000)/10000f;o.CreatedAt=r.Long();o.Acre=r.Range(0,Town.ScreenColumns*Town.ScreenRows-1);World[i]=o;}
        for(int i=0;i<Notices.Length;i++){var id=r.Key();if(id.IsEmpty)continue;Notices[i]=new TownNotice{Id=id,Author=r.Key(false),At=r.Long(),Text=r.Text(1000)};}
    }
}
public sealed partial class VillagerJobService
{
    public JobOutcome PostNotice(string body,ulong sequence)
    {
        if(!Enabled)return new JobOutcome(JobResult.NotAvailable);
        ulong fingerprint=MailFingerprint(JobCommandKind.PostNotice,default,0,0,body);
        if(MailReceipt(sequence,fingerprint,out var prior))return prior;
        if(!JobLetterGrader.TryGrade(body,false,out var grade)||grade.ScalarCount==0)return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.InvalidLetter));
        if(!ledger.CanAllocate(1))return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.InvalidIdentity));
        var id=ledger.Allocate();ledger.Notices[ledger.NoticeNext]=new TownNotice{Id=id,Author=ledger.Player(state.ActiveProfile),At=Now,Text=grade.NormalizedBody};ledger.NoticeNext=(ledger.NoticeNext+1)%OfflineJobLedger.NoticeCapacity;
        if(state.Tutorial==6)state.Tutorial=7;
        return CommitMail(sequence,fingerprint,new JobOutcome(JobResult.Ok,id));
    }
    // Called at the existing fixed 30 Hz, including while a menu is open.
    // Eight bodies, no allocations, no job RNG draws, and no card I/O.
    public void TickWorld(float playerBeforeX,float playerBeforeZ)
    {
        if(!Enabled)return;
        int day=(int)(Now/86400);bool winter=JobCalendar.Season(JobTemplate.FavorSnowman,Now);
        if(day!=ledger.ObjectDay){
            ledger.ObjectDay=day;
            for(int i=0;i<ledger.World.Length;i++){
                var o=ledger.World[i];if(o.Kind==JobWorldKind.Snowman&&Now-o.CreatedAt>=3*86400)DestroyWorldObject(i);
                if(!winter&&(o.Kind==JobWorldKind.Snowball||o.Kind==JobWorldKind.Snowman))DestroyWorldObject(i);
            }
            SpawnWorldObjects(winter);
        }
        float pdx=state.X-playerBeforeX,pdz=state.Z-playerBeforeZ;
        for(int i=0;i<ledger.World.Length;i++){
            var o=ledger.World[i];if(o.Kind!=JobWorldKind.Ball&&o.Kind!=JobWorldKind.Snowball)continue;
            float dx=o.X-state.X,dz=o.Z-state.Z,contact=o.Radius+.34f;
            if(state.Space==0&&dx*dx+dz*dz<contact*contact&&(pdx!=0||pdz!=0)&&dx*pdx+dz*pdz>0){o.Vx=pdx*1.7f;o.Vz=pdz*1.7f;o.Builder=ledger.Player(state.ActiveProfile);if(o.Kind==JobWorldKind.Snowball)o.Radius=Math.Min(.7f,o.Radius+(float)Math.Sqrt(pdx*pdx+pdz*pdz)*.007f);}
            if(o.Vx!=0||o.Vz!=0){
                float nx=o.X+o.Vx,nz=o.Z+o.Vz;
                if(state.CanWalk(nx,o.Z,0))o.X=nx;else o.Vx=-o.Vx*.3f;
                if(state.CanWalk(o.X,nz,0))o.Z=nz;else o.Vz=-o.Vz*.3f;
                o.Vx*=.9f;o.Vz*=.9f;if(Math.Abs(o.Vx)<.0005f)o.Vx=0;if(Math.Abs(o.Vz)<.0005f)o.Vz=0;
                o.Acre=(int)o.X/Town.ScreenTileSize+(int)o.Z/Town.ScreenTileSize*Town.ScreenColumns;
                if(o.Kind==JobWorldKind.Ball&&o.Builder.IsValid){for(int n=0;n<6;n++){var v=state.Town.Residents[n];dx=v.PosX-o.X;dz=v.PosZ-o.Z;if(v.Space==0&&dx*dx+dz*dz<(o.Radius+.3f)*(o.Radius+.3f)){RecordWorldEvidence(JobTemplate.FavorBall,o.Id,o.Builder,Now,++ledger.EventSequences[2],ledger.Npc(n));o.Vx=-o.Vx*.4f;o.Vz=-o.Vz*.4f;break;}}}
            }
            ledger.World[i]=o;
            if(o.Kind!=JobWorldKind.Snowball||o.Radius<.4f||o.Builder.IsEmpty)continue;
            for(int j=i+1;j<ledger.World.Length;j++){
                var other=ledger.World[j];dx=o.X-other.X;dz=o.Z-other.Z;
                if(other.Kind!=JobWorldKind.Snowball||other.Radius<.4f||dx*dx+dz*dz>(o.Radius+other.Radius)*(o.Radius+other.Radius)||!ledger.CanAllocate(1))continue;
                // Combining two independently rolled balls creates a new identity.
                if(other.Vx*other.Vx+other.Vz*other.Vz>o.Vx*o.Vx+o.Vz*o.Vz&&other.Builder.IsValid)o.Builder=other.Builder;
                o.Kind=JobWorldKind.Snowman;o.Id=ledger.Allocate();o.CreatedAt=Now;o.Radius=Math.Max(o.Radius,other.Radius);o.Vx=o.Vz=0;ledger.World[i]=o;ledger.World[j]=default;
                RecordWorldEvidence(JobTemplate.FavorSnowman,o.Id,o.Builder,Now,++ledger.EventSequences[2],homeAcre:o.Acre);ledger.Revision++;break;
            }
        }
    }
    private void SpawnWorldObjects(bool winter)
    {
        int balls=0,snow=0;for(int i=0;i<ledger.World.Length;i++){if(ledger.World[i].Kind==JobWorldKind.Ball)balls++;if(ledger.World[i].Kind==JobWorldKind.Snowball)snow++;}
        uint hash=unchecked((uint)state.Town.Seed^(uint)ledger.ObjectDay*2246822519u);
        for(int i=0;i<ledger.World.Length&&ledger.CanAllocate(1);i++){
            if(ledger.World[i].Kind!=JobWorldKind.Empty)continue;
            var kind=balls<2?JobWorldKind.Ball:winter&&snow<4?JobWorldKind.Snowball:JobWorldKind.Empty;if(kind==JobWorldKind.Empty)break;
            bool found=false;float x=0,z=0;
            for(int tries=0;tries<Town.Cells;tries++){hash=unchecked(hash*1664525u+1013904223u);int cell=(int)(hash%Town.Cells);x=cell%Town.Width+.5f;z=cell/Town.Width+.5f;if(!state.CanWalk(x,z,0)||state.Town.At((int)x,(int)z)!=0)continue;found=true;break;}
            if(!found)break;
            ledger.World[i]=new JobWorldObject{Id=ledger.Allocate(),Kind=kind,X=x,Z=z,Radius=kind==JobWorldKind.Ball?.32f:.25f,CreatedAt=Now,Acre=(int)x/Town.ScreenTileSize+(int)z/Town.ScreenTileSize*Town.ScreenColumns};if(kind==JobWorldKind.Ball)balls++;else snow++;
        }
        ledger.Revision++;
    }
    public bool DestroyWorldObject(int slot)
    {
        if((uint)slot>=ledger.World.Length||ledger.World[slot].Kind==JobWorldKind.Empty)return false;
        var o=ledger.World[slot];if(o.Kind==JobWorldKind.Snowman)RecordWorldEvidence(JobTemplate.FavorSnowman,o.Id,o.Builder,Now,++ledger.EventSequences[2],homeAcre:o.Acre,destroyed:true);
        ledger.World[slot]=default;ledger.Revision++;return true;
    }
}
