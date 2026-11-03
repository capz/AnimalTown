namespace AnimalTown;

// Explicit bounded wire format, independent of host endianness and reflection.
public static class SaveCodec
{
    public const int MaximumBytes=1024*1024;
    private static void Put(byte[] b,ref int at,int n){for(int i=0;i<4;i++)b[at++]=(byte)((uint)n>>(i*8));}
    private static int Get(byte[] b,ref int at)
    { if(at>b.Length-4)throw new InvalidOperationException("Truncated save");int n=0;for(int i=0;i<4;i++)n|=b[at++]<<(8*i);return n; }
    private static void Text(byte[] b,ref int at,string s)
    { Put(b,ref at,s.Length);for(int i=0;i<s.Length;i++)Put(b,ref at,s[i]); }
    private static string Text(byte[] b,ref int at,int limit)
    { int n=Get(b,ref at);if(n<0||n>limit)throw new InvalidOperationException("Invalid text");var c=new char[n];for(int i=0;i<n;i++)c[i]=(char)Get(b,ref at);return new string(c); }
    public static uint Crc(byte[] b,int start,int count)
    { uint c=0xffffffff;for(int i=start;i<start+count;i++){c^=b[i];for(int j=0;j<8;j++)c=(c>>1)^((c&1)!=0?0xedb88320u:0);}return ~c; }
    public static byte[] Encode(GameState s)
    {
        int terrainRuns=1;for(int i=1;i<Town.Cells;i++)if(s.Town.Tiles[i]!=s.Town.Tiles[i-1])terrainRuns++;
        int capacity=2048+(s.Name.Length+s.Catchphrase.Length)*4+s.MailCount*4+s.Town.GroundCount*24+s.Town.PlacedCount*24+terrainRuns*4;
        if(capacity>MaximumBytes)throw new InvalidOperationException("Save exceeds format budget");
        // Allocate from actual encoded content, not the 1-MiB format ceiling.
        var b=new byte[capacity];int p=0;Put(b,ref p,0x41544f57);Put(b,ref p,3);Put(b,ref p,Catalog.Version);Put(b,ref p,s.Sequence);
        Put(b,ref p,0);Put(b,ref p,0);
        int[] f={s.Town.Seed,s.Town.Day,s.Town.Month,s.Wallet,(int)s.Savings,(int)(s.Savings>>32),(int)s.Debt,(int)(s.Debt>>32),s.Species,s.Palette,s.Tool,s.Shirt,s.HouseTier,s.GardenTier,s.PendingKind,s.PendingTier,s.PendingDay,s.Tutorial,s.MetMask,s.Jobs,s.JobDay,s.MailCount,s.Roof,s.Wall,s.Fence,s.Mailbox,s.Exterior,s.HousePlaced?1:0,(int)(s.X*1024),(int)(s.Z*1024),s.Town.HouseX,s.Town.HouseZ,s.Town.Spending,s.Town.StockCount};
        for(int i=0;i<f.Length;i++)Put(b,ref p,f[i]);Put(b,ref p,s.BackpackTier);Text(b,ref p,s.Name);Text(b,ref p,s.Catchphrase);
        for(int i=0;i<GameState.MaximumPockets;i++)Put(b,ref p,s.Pockets[i]);for(int i=0;i<64;i++)Put(b,ref p,s.Owned[i]);for(int i=0;i<s.MailCount;i++)Put(b,ref p,s.Mail[i]);
        int countAt=p;Put(b,ref p,0);int runs=0;
        for(int i=0;i<Town.Cells;)
        { byte terrain=s.Town.Tiles[i];int length=1;while(i+length<Town.Cells&&s.Town.Tiles[i+length]==terrain)length++;Put(b,ref p,(terrain<<24)|length);i+=length;runs++; }
        Put(b,ref countAt,runs);
        for(int i=0;i<12;i++)Put(b,ref p,s.Town.Stock[i]);
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];Put(b,ref p,v.X);Put(b,ref p,v.Z);Put(b,ref p,v.Friendship);Put(b,ref p,v.Request);Put(b,ref p,v.RequestDay);Put(b,ref p,v.Met);}
        Put(b,ref p,s.Town.GroundCount);for(int i=0;i<s.Town.GroundCount;i++){var g=s.Town.Ground[i];Put(b,ref p,g.Item);Put(b,ref p,g.X);Put(b,ref p,g.Z);Put(b,ref p,g.Owner);Put(b,ref p,g.Day);Put(b,ref p,g.Kind);}
        Put(b,ref p,s.Town.PlacedCount);for(int i=0;i<s.Town.PlacedCount;i++){var q=s.Town.Placed[i];Put(b,ref p,q.Item);Put(b,ref p,q.X);Put(b,ref p,q.Z);Put(b,ref p,q.Rotation);Put(b,ref p,q.Room);Put(b,ref p,q.Owner);}
        int h=16;Put(b,ref h,p);Put(b,ref h,(int)Crc(b,24,p-24));var result=new byte[p];Array.Copy(b,result,p);return result;
    }
    private static int Range(byte[] b,ref int p,int min,int max)
    { int n=Get(b,ref p);if(n<min||n>max)throw new InvalidOperationException("Save value outside range");return n; }
    public static GameState Decode(byte[] b)
    {
        if(b.Length<160||b.Length>MaximumBytes)throw new InvalidOperationException("Invalid save size");int p=0;
        if(Get(b,ref p)!=0x41544f57)throw new InvalidOperationException("Invalid save magic");int version=Get(b,ref p);
        if((version<1||version>3)||Get(b,ref p)!=Catalog.Version)throw new InvalidOperationException("Unsupported save version");
        int seq=Range(b,ref p,0,int.MaxValue);if(Get(b,ref p)!=b.Length||(uint)Get(b,ref p)!=Crc(b,24,b.Length-24))throw new InvalidOperationException("Save checksum failed");
        var s=new GameState(Get(b,ref p));s.Sequence=seq;s.Town.Day=Get(b,ref p);s.Town.Month=Get(b,ref p);s.Wallet=Range(b,ref p,0,99999);
        uint low=(uint)Get(b,ref p);int high=Range(b,ref p,0,int.MaxValue);s.Savings=((long)high<<32)|low;
        low=(uint)Get(b,ref p);high=Range(b,ref p,0,0);s.Debt=((long)high<<32)|low;if(s.Debt>1600000)throw new InvalidOperationException("Debt invalid");
        s.Species=Range(b,ref p,0,5);s.Palette=Range(b,ref p,0,7);s.Tool=Range(b,ref p,0,25);s.Shirt=Range(b,ref p,0,25);s.HouseTier=Range(b,ref p,0,5);s.GardenTier=Range(b,ref p,0,4);s.PendingKind=Range(b,ref p,0,2);s.PendingTier=Range(b,ref p,0,5);s.PendingDay=Get(b,ref p);
        s.Tutorial=Range(b,ref p,0,6);s.MetMask=Range(b,ref p,0,63);s.Jobs=Range(b,ref p,0,2);s.JobDay=Get(b,ref p);s.MailCount=Range(b,ref p,0,128);
        s.Roof=Range(b,ref p,0,11);s.Wall=Range(b,ref p,0,11);s.Fence=Range(b,ref p,0,5);s.Mailbox=Range(b,ref p,0,5);s.Exterior=Range(b,ref p,0,4);s.HousePlaced=Range(b,ref p,0,1)==1;
        s.X=Range(b,ref p,0,(Town.Width-1)*1024)/1024f;s.Z=Range(b,ref p,0,(Town.Height-1)*1024)/1024f;s.Town.HouseX=Range(b,ref p,1,Town.Width-15);s.Town.HouseZ=Range(b,ref p,1,Town.Height-13);s.Town.Spending=Range(b,ref p,0,int.MaxValue);s.Town.StockCount=Range(b,ref p,6,12);
        if(version>=3)s.BackpackTier=Range(b,ref p,0,2);
        s.Name=Text(b,ref p,16);s.Catchphrase=Text(b,ref p,32);
        int pocketCount=version>=3?GameState.MaximumPockets:15;
        for(int i=0;i<pocketCount;i++)s.Pockets[i]=Range(b,ref p,0,25);
        // An old 15-slot save may contain items in slots 10..14. Grant the
        // 20-slot tier on migration rather than lose or hide those items.
        if(version<3)for(int i=10;i<15;i++)if(s.Pockets[i]!=0)s.BackpackTier=1;
        for(int i=s.Capacity;i<GameState.MaximumPockets;i++)if(s.Pockets[i]!=0)throw new InvalidOperationException("Locked backpack slot occupied");
        for(int i=0;i<64;i++)s.Owned[i]=Range(b,ref p,0,1);for(int i=0;i<s.MailCount;i++)s.Mail[i]=Range(b,ref p,-25,25);
        if(version==1)
        { for(int z=0;z<72;z++)for(int x=0;x<80;x++){if(p>=b.Length||b[p]>7)throw new InvalidOperationException("Terrain invalid");s.Town.Tiles[z*Town.Width+x]=b[p++];} }
        else
        {
            int runCount=Range(b,ref p,1,Town.Cells),cell=0;
            for(int i=0;i<runCount;i++){int packed=Get(b,ref p),terrain=(int)((uint)packed>>24),length=packed&0xffffff;if(terrain>7||length==0||length>Town.Cells-cell)throw new InvalidOperationException("Terrain run invalid");for(int n=0;n<length;n++)s.Town.Tiles[cell++]=(byte)terrain;}
            if(cell!=Town.Cells)throw new InvalidOperationException("Incomplete terrain");
        }
        for(int i=0;i<12;i++)s.Town.Stock[i]=Range(b,ref p,0,25);
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];v.X=Range(b,ref p,0,Town.Width-1);v.Z=Range(b,ref p,0,Town.Height-1);v.Friendship=Range(b,ref p,0,255);v.Request=Range(b,ref p,0,25);v.RequestDay=Get(b,ref p);v.Met=Range(b,ref p,0,1);}
        s.Town.GroundCount=Range(b,ref p,0,512);for(int i=0;i<s.Town.GroundCount;i++){var g=s.Town.Ground[i];g.Item=Range(b,ref p,1,25);g.X=Range(b,ref p,0,Town.Width-1);g.Z=Range(b,ref p,0,Town.Height-1);g.Owner=Range(b,ref p,0,3);g.Day=Get(b,ref p);g.Kind=Range(b,ref p,0,4);}
        int count=Range(b,ref p,0,256);s.Town.PlacedCount=0;
        for(int i=0;i<count;i++){var q=s.Town.Placed[i];q.Item=Range(b,ref p,1,25);q.X=Get(b,ref p);q.Z=Get(b,ref p);q.Rotation=Range(b,ref p,0,3);q.Room=Range(b,ref p,0,4);q.Owner=Range(b,ref p,0,0);if(!s.CanPlace(q.Item,q.X,q.Z,q.Rotation,q.Room,0))throw new InvalidOperationException("Invalid placement");s.Town.PlacedCount++;}
        if(p!=b.Length)throw new InvalidOperationException("Trailing save bytes");s.Town.NotifyTerrainChanged();return s;
    }
}
