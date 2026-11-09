namespace AnimalTown;

// Independent ambient notices. Only six residents are inspected; no routes,
// conversations, rewards or requests are started/consumed by this scheduler.
public sealed class VillagerAttention
{
    public const int Greeting=1,RequestReady=2,Deadline=3,MaximumVisible=2;
    public const long GreetingDuration=4000000,UrgentDuration=6000000;
    private readonly int[] kinds=new int[6];
    private readonly bool[] shown=new bool[6];
    private readonly long[] until=new long[6],quietUntil=new long[6];
    private GameState? current;
    private int pendingGreetings;
    private long last=-1,emitted=-1000000;
    public int Revision {get;private set;}
    public int Kind(int resident)=>kinds[resident];
    public bool Calling(int resident)=>kinds[resident]!=0&&shown[resident];
    public void Spoke(int resident,long now)
    {Clear(resident);pendingGreetings&=~(1<<resident);quietUntil[resident]=now+15000000;}
    // Daily cooldown starts only after the renderer actually fits the bubble.
    public void Shown(GameState s,int resident,long now)
    {
        if(current!=s||kinds[resident]==0||shown[resident])return;
        shown[resident]=true;until[resident]=now+(kinds[resident]==Greeting?GreetingDuration:UrgentDuration);
        var v=s.Town.Residents[resident];
        if(kinds[resident]==Greeting){v.LastGreetingDay=s.Town.Day;pendingGreetings&=~(1<<resident);}
        else v.LastUrgentDay=s.Town.Day;
    }
    private void Clear(int i){if(kinds[i]!=0){kinds[i]=0;Revision++;}}
    public void Update(GameState s,long now,int minuteOfDay,bool enabled,int visibleMask)
    {
        if(current!=s||now<last){
            current=s;pendingGreetings=0;emitted=now-1000000;
            for(int i=0;i<6;i++){Clear(i);quietUntil[i]=now;}
        }
        last=now;
        if(!enabled){for(int i=0;i<6;i++)Clear(i);pendingGreetings=0;return;}
        int nearby=0,count=0;
        for(int i=0;i<6;i++){
            var v=s.Town.Residents[i];float dx=v.PosX-s.X,dz=v.PosZ-s.Z;
            bool seen=(visibleMask&(1<<i))!=0&&v.Space==0&&dx*dx+dz*dz<=81&&ClearSight(s,v.PosX,v.PosZ);
            if(seen){
                nearby|=1<<i;
                if(v.Met!=0&&v.LastSeenDay!=int.MinValue&&(long)s.Town.Day-v.LastSeenDay>=2&&v.LastGreetingDay!=s.Town.Day)pendingGreetings|=1<<i;
                v.LastSeenDay=s.Town.Day;
            }else pendingGreetings&=~(1<<i);
            if(!seen||now>=until[i]||kinds[i]>=RequestReady&&Reason(s,i,minuteOfDay)!=kinds[i])Clear(i);
            if(kinds[i]!=0)count++;
        }
        if(now-emitted<800000)return;
        int chosen=-1,kind=0;float nearest=float.MaxValue;
        // Urgent notices win; distance breaks ties instead of array order.
        for(int pass=0;pass<2&&chosen<0;pass++)for(int i=0;i<6;i++){
            if((nearby&(1<<i))==0||kinds[i]!=0||now<quietUntil[i])continue;
            var v=s.Town.Residents[i];int reason=Reason(s,i,minuteOfDay);
            if(pass==0){if(reason==0||v.LastUrgentDay==s.Town.Day)continue;}
            else{if((pendingGreetings&(1<<i))==0||v.LastGreetingDay==s.Town.Day)continue;reason=Greeting;}
            float dx=v.PosX-s.X,dz=v.PosZ-s.Z,d=dx*dx+dz*dz;
            if(d<nearest){chosen=i;kind=reason;nearest=d;}
        }
        if(chosen<0)return;
        if(count>=MaximumVisible){
            if(kind==Greeting)return;
            int victim=-1;for(int i=0;i<6;i++)if(kinds[i]==Greeting&&(victim<0||until[i]<until[victim]))victim=i;
            if(victim<0)return;Clear(victim);
        }
        kinds[chosen]=kind;shown[chosen]=false;until[chosen]=now+(kind==Greeting?GreetingDuration:UrgentDuration);emitted=now;Revision++;
    }
    private static int Reason(GameState s,int i,int minuteOfDay)
    {
        var v=s.Town.Residents[i];if(v.Request==0||v.RequestDay!=s.Town.Day)return 0;
        if(s.Find(v.Request)>=0)return RequestReady;
        return minuteOfDay>=300&&minuteOfDay<360?Deadline:0;
    }
    public static bool ClearSight(GameState s,float x,float z)
    {
        float dx=x-s.X,dz=z-s.Z;int steps=Math.Max(1,(int)Math.Ceiling(Math.Sqrt(dx*dx+dz*dz)*2));
        float from=TerrainElevation.At(s.Town,s.X,s.Z)+1.6f,to=TerrainElevation.At(s.Town,x,z)+1.6f;
        for(int i=1;i<steps;i++){
            float t=i/(float)steps,px=s.X+dx*t,pz=s.Z+dz*t;
            if(s.Town.At((int)px,(int)pz)==(int)Terrain.Building||s.HousePlaced&&px>=s.Town.HouseX+2&&px<s.Town.HouseX+5&&pz>=s.Town.HouseZ+1&&pz<s.Town.HouseZ+4||TerrainElevation.At(s.Town,px,pz)>from+(to-from)*t)return false;
        }
        return true;
    }
}
