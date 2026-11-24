namespace AnimalTown;

// Cooked DNMD metadata gives admission a bound for this actual package instead
// of treating the loader's maximum budget as its expected allocation. Descriptor
// sizes intentionally cover both PPC32 and the larger native host-test ABI.
public sealed class ModelMemoryPlan
{
    public const int LoadGuardBytes=64*1024;
    public const int MaximumSharedBytes=3*1024*1024,MaximumInstanceBytes=512*1024;
    public int PackageBytes {get;private set;}
    public uint PackageHash {get;private set;}
    public int SharedBytes {get;private set;}
    public int InstanceBytes {get;private set;}
    public int OneInstancePeak=>checked(SharedBytes+Charge(PackageBytes)+InstanceBytes+LoadGuardBytes);
    public int TwoInstancePeak=>checked(OneInstancePeak+InstanceBytes);
    private static int Charge(int bytes)=>checked((bytes+63)&~31);
    private static int Read(byte[] bytes,int at)
    {
        if(at<0||at>bytes.Length-4)throw new InvalidOperationException("Truncated model memory metadata");
        return (bytes[at]<<24)|(bytes[at+1]<<16)|(bytes[at+2]<<8)|bytes[at+3];
    }
    public static uint Hash(byte[] bytes,int start=0,int count=-1)
    {
        if(count<0)count=bytes.Length-start;
        if(start<0||count<0||start>bytes.Length-count)throw new ArgumentOutOfRangeException("start");
        uint value=2166136261;for(int i=0;i<count;i++)value=unchecked((value^bytes[start+i])*16777619);return value;
    }
    public static ModelMemoryPlan Estimate(byte[] package)
    {
        if(package.Length<160||Read(package,0)!=0x444e444d||Read(package,4)<1||Read(package,4)>2||Read(package,8)!=package.Length||Read(package,12)!=6)
            throw new InvalidOperationException("Invalid DNMD memory-plan package");
        var starts=new int[6];var sizes=new int[6];var counts=new int[6];
        int previous=160;int[] limits={1024,4096,4097,4096,1024,256};
        for(int i=0;i<6;i++){
            int at=16+i*16,start=Read(package,at+4),size=Read(package,at+8);
            if(Read(package,at)!=i+1||start<previous||(start&31)!=0||size<4||start>package.Length-size||Hash(package,start,size)!=unchecked((uint)Read(package,at+12)))
                throw new InvalidOperationException("Invalid DNMD memory-plan section");
            starts[i]=start;sizes[i]=size;counts[i]=Read(package,start);previous=start+size;
            if(counts[i]<0||counts[i]>limits[i])throw new InvalidOperationException("DNMD memory-plan count budget");
        }
        if(counts[0]==0||counts[1]==0||counts[2]==0)throw new InvalidOperationException("Empty DNMD memory-plan model");
        int textures=0,cursor=starts[3]+4,end=starts[3]+sizes[3];
        for(int i=0;i<counts[3];i++){
            if(cursor>end-28)throw new InvalidOperationException("Truncated DNMD memory-plan texture");
            int bytes=Read(package,cursor+24);if(bytes<0||bytes>end-cursor-28)throw new InvalidOperationException("DNMD memory-plan texture range");
            textures=checked(textures+Charge(bytes));cursor=checked(cursor+28+bytes);
        }
        if(cursor!=end)throw new InvalidOperationException("DNMD memory-plan texture trailing bytes");
        int maxJoints=0;cursor=starts[4]+4;end=starts[4]+sizes[4];
        for(int i=0;i<counts[4];i++){
            if(cursor>end-4)throw new InvalidOperationException("Truncated DNMD memory-plan skin");
            int joints=Read(package,cursor);if(joints<1||joints>256||joints>(end-cursor-4)/68)throw new InvalidOperationException("DNMD memory-plan joint budget");
            maxJoints=Math.Max(maxJoints,joints);cursor=checked(cursor+4+joints*68);
        }
        if(cursor!=end)throw new InvalidOperationException("DNMD memory-plan skin trailing bytes");
        int skinOutputs=0;cursor=starts[1]+4;end=starts[1]+sizes[1];
        for(int i=0;i<counts[1];i++){
            if(cursor>end-44)throw new InvalidOperationException("Truncated DNMD memory-plan mesh");
            int skin=Read(package,cursor+4),vertices=Read(package,cursor+12),indices=Read(package,cursor+16);
            if(skin< -1||skin>=counts[4]||vertices<1||vertices>1000000||indices<3||indices>3000000||indices%3!=0||
               44+(long)vertices*80+(long)indices*4>end-cursor)throw new InvalidOperationException("DNMD memory-plan mesh range");
            if(skin>=0)skinOutputs=checked(skinOutputs+Charge(vertices*24));
            cursor=checked(cursor+44+vertices*80+indices*4);
        }
        if(cursor!=end)throw new InvalidOperationException("DNMD memory-plan mesh trailing bytes");
        int[] widths={128,96,64,64,32,96};
        int shared=checked(1024+Charge(package.Length)+textures);
        for(int i=0;i<counts.Length;i++)shared=checked(shared+Charge(counts[i]*widths[i]));
        int nodes=counts[0],meshes=counts[1];
        // Two animation poses, global matrices, traversal flags, output-pointer
        // table, translucent draw sorting and the largest reusable skin palette.
        int instance=checked(512+Charge(nodes*40)*2+Charge(nodes*64)+Charge(nodes)+Charge(meshes*8)+Charge(meshes*4)*2+skinOutputs+(skinOutputs>0?Charge(maxJoints*128):0));
        return new ModelMemoryPlan{PackageBytes=package.Length,PackageHash=Hash(package),SharedBytes=shared,InstanceBytes=instance};
    }
}

public sealed class ModelMemoryCatalog
{
    public const int Magic=0x564d454d,Version=1,Count=6,Stride=24;
    private readonly int[] lengths=new int[Count],one=new int[Count],instances=new int[Count];
    private static int Int(byte[] bytes,int at)=>bytes[at]|bytes[at+1]<<8|bytes[at+2]<<16|bytes[at+3]<<24;
    public ModelMemoryCatalog(byte[] bytes)
    {
        if(bytes.Length!=16+Count*Stride||Int(bytes,0)!=Magic||Int(bytes,4)!=Version||Int(bytes,8)!=Count||Int(bytes,12)!=Stride)
            throw new InvalidOperationException("Invalid villager memory catalog");
        for(int i=0;i<Count;i++){
            int at=16+i*Stride,length=Int(bytes,at),shared=Int(bytes,at+8),instance=Int(bytes,at+12);
            int normal=Int(bytes,at+16),selected=Int(bytes,at+20);
            if(length<160||length>16*1024*1024||shared<length||instance<512||normal<shared||normal>64*1024*1024||
               (long)normal!=shared+((length+63)&~31)+(long)instance+ModelMemoryPlan.LoadGuardBytes||selected!=(long)normal+instance)
                throw new InvalidOperationException("Invalid villager memory admission bound");
            lengths[i]=length;one[i]=normal;instances[i]=instance;
        }
    }
    public int StartupPeak(int species,bool selected)
        =>StartupPeak(species,selected?2:1);
    public int StartupPeak(int species,int instanceCount)
    {
        if(species<0||species>=Count)throw new ArgumentOutOfRangeException("species");
        if(instanceCount<0||instanceCount>BuildingWorld.Actors)throw new ArgumentOutOfRangeException("instanceCount");
        // The manifest validates a <=64 MiB single-instance peak. At most the
        // bounded town actor cohort can share a model, so this sum cannot wrap.
        return one[species]+(instanceCount-1)*instances[species];
    }
    public void ValidatePackage(int species,int length)
    {
        if(species<0||species>=Count||lengths[species]!=length)throw new InvalidOperationException("Villager package does not match its cooked memory plan");
    }
}
