namespace AnimalTown;

// Independently versioned records. The legacy codec remains the import/export
// format and the common semantic validator, never the new storage payload.
public static class SplitSaveCodec
{
    public const int TownData=0,HousePositions=1,ShopStates=2,PlayerVillager=3,Commit=4;
    public static string FileName(int kind,int bank)
    {string[] names={"AT-Town","AT-Houses","AT-Shops","AT-Player","AT-Commit"};return names[kind]+bank;}
    public static bool IsFile(string name)
    {for(int bank=0;bank<2;bank++)for(int kind=0;kind<5;kind++)if(name==FileName(kind,bank))return true;return name=="AnimalTown0"||name=="AnimalTown1";}
    private sealed class Writer
    {
        private readonly byte[] bytes;private int at;
        internal Writer(GameState s,int kind,int capacity)
        {bytes=new byte[capacity];Int(0x50535441);Int(2);Int(Catalog.Version);Int(kind);Int(s.Sequence);Int(s.Town.Seed);Int(0);}
        internal void Int(int n){if(at>bytes.Length-4)throw new InvalidOperationException("Save record exceeds budget");for(int i=0;i<4;i++)bytes[at++]=(byte)((uint)n>>(i*8));}
        internal void Text(string s){Int(s.Length);for(int i=0;i<s.Length;i++)Int(s[i]);}
        internal void Rle(byte[] source){Int(Runs(source));for(int i=0;i<source.Length;){int n=1;while(i+n<source.Length&&source[i+n]==source[i])n++;Int((source[i]<<24)|n);i+=n;}}
        internal byte[] Finish(){int length=at+4;for(int i=0;i<4;i++)bytes[24+i]=(byte)(length>>(i*8));Int((int)SaveCodec.Crc(bytes,0,at));var result=new byte[at];Array.Copy(bytes,result,at);return result;}
    }
    private sealed class Reader
    {
        private readonly byte[] bytes;private int at;
        internal readonly int Sequence,Seed,Version;
        internal Reader(byte[] source,int kind)
        {
            bytes=source;if(bytes.Length<32||bytes.Length>SaveCodec.MaximumBytes)throw new InvalidOperationException("Invalid save record size");
            if(Int()!=0x50535441)throw new InvalidOperationException("Wrong save record magic");Version=Int();
            if((Version<1||Version>2)||Int()!=Catalog.Version||Int()!=kind)throw new InvalidOperationException("Wrong save record type or version");
            Sequence=Range(0,int.MaxValue);Seed=Int();if(Int()!=bytes.Length)throw new InvalidOperationException("Save record length mismatch");
            int crc=0;for(int i=0;i<4;i++)crc|=bytes[bytes.Length-4+i]<<(i*8);
            if((uint)crc!=SaveCodec.Crc(bytes,0,bytes.Length-4))throw new InvalidOperationException("Save record checksum failed");
        }
        internal int Int(){if(at>bytes.Length-8)throw new InvalidOperationException("Truncated save record");int n=0;for(int i=0;i<4;i++)n|=bytes[at++]<<(i*8);return n;}
        internal int Range(int min,int max){int n=Int();if(n<min||n>max)throw new InvalidOperationException("Save record value outside range");return n;}
        internal string Text(int limit){int n=Range(0,limit);var c=new char[n];for(int i=0;i<n;i++)c[i]=(char)Range(0,65535);return new string(c);}
        internal void Rle(byte[] target,int max)
        {int runs=Range(1,target.Length),cell=0;for(int i=0;i<runs;i++){int packed=Int(),mode=(int)((uint)packed>>24),n=packed&0xffffff;if(mode>max||n==0||n>target.Length-cell)throw new InvalidOperationException("Invalid save terrain run");for(int j=0;j<n;j++)target[cell++]=(byte)mode;}if(cell!=target.Length)throw new InvalidOperationException("Incomplete save terrain");}
        internal void Match(Reader other){if(Version!=other.Version||Seed!=other.Seed||Sequence!=other.Sequence)throw new InvalidOperationException("Mixed save generations");}
        internal void End(){if(at!=bytes.Length-4)throw new InvalidOperationException("Trailing save record data");}
    }
    private static int Runs(byte[] a){int n=1;for(int i=1;i<a.Length;i++)if(a[i]!=a[i-1])n++;return n;}
    public static byte[][] Encode(GameState s)
    {
        var t=s.Town;var records=new byte[4][];
        var w=new Writer(s,TownData,2048+4*(Runs(t.Tiles)+Runs(t.Elevation))+24*t.GroundCount);
        w.Int(Town.Width);w.Int(Town.Height);w.Int(TownGenerator.Version);w.Int(t.Day);w.Int(t.Month);w.Rle(t.Tiles);w.Rle(t.Elevation);
        for(int i=0;i<6;i++){var v=t.Residents[i];w.Int(v.X);w.Int(v.Z);w.Text(v.Name);w.Int((int)(v.PosX*1024));w.Int((int)(v.PosZ*1024));}
        w.Int(t.GroundCount);for(int i=0;i<t.GroundCount;i++){var g=t.Ground[i];w.Int(g.Item);w.Int(g.X);w.Int(g.Z);w.Int(g.Owner);w.Int(g.Day);w.Int(g.Kind);}records[0]=w.Finish();
        w=new Writer(s,HousePositions,128);w.Int(s.HousePlaced?1:0);w.Int(t.HouseX);w.Int(t.HouseZ);for(int i=0;i<6;i++){w.Int(t.Residents[i].HomeX);w.Int(t.Residents[i].HomeZ);}records[1]=w.Finish();
        w=new Writer(s,ShopStates,128);w.Int(t.Spending);w.Int(t.StockCount);for(int i=0;i<12;i++)w.Int(t.Stock[i]);records[2]=w.Finish();
        w=new Writer(s,PlayerVillager,4096+24*t.PlacedCount);
        int[] fields={s.Wallet,(int)s.Savings,(int)(s.Savings>>32),(int)s.Debt,(int)(s.Debt>>32),s.Species,s.Palette,s.Tool,s.Shirt,s.HouseTier,s.GardenTier,s.PendingKind,s.PendingTier,s.PendingDay,s.Tutorial,s.MetMask,s.Jobs,s.JobDay,s.MailCount,s.Roof,s.Wall,s.Fence,s.Mailbox,s.Exterior,s.BackpackTier,(int)(s.X*1024),(int)(s.Z*1024)};
        for(int i=0;i<fields.Length;i++)w.Int(fields[i]);w.Text(s.Name);w.Text(s.Catchphrase);
        for(int i=0;i<GameState.MaximumPockets;i++)w.Int(s.Pockets[i]);for(int i=0;i<64;i++)w.Int(s.Owned[i]);for(int i=0;i<s.MailCount;i++)w.Int(s.Mail[i]);
        for(int i=0;i<6;i++){var v=t.Residents[i];w.Int(v.Friendship);w.Int(v.Request);w.Int(v.RequestDay);w.Int(v.Met);}
        w.Int(t.PlacedCount);for(int i=0;i<t.PlacedCount;i++){var p=t.Placed[i];w.Int(p.Item);w.Int(p.X);w.Int(p.Z);w.Int(p.Rotation);w.Int(p.Room);w.Int(p.Owner);}records[3]=w.Finish();return records;
    }
    public static GameState Decode(byte[][] records)
    {
        if(records.Length!=4)throw new InvalidOperationException("Incomplete save set");var r=new Reader(records[0],TownData);var root=r;var s=new GameState(r.Seed);s.Sequence=r.Sequence;var t=s.Town;
        r.Range(Town.Width,Town.Width);r.Range(Town.Height,Town.Height);r.Range(1,TownGenerator.Version);t.Day=r.Int();t.Month=r.Int();r.Rle(t.Tiles,r.Version>=2?(int)Terrain.CoastWest:7);r.Rle(t.Elevation,5);
        for(int i=0;i<6;i++){var v=t.Residents[i];v.X=r.Int();v.Z=r.Int();v.Name=r.Text(24);v.PosX=r.Int()/1024f;v.PosZ=r.Int()/1024f;}
        t.GroundCount=r.Range(0,512);for(int i=0;i<t.GroundCount;i++){var g=t.Ground[i];g.Item=r.Int();g.X=r.Int();g.Z=r.Int();g.Owner=r.Int();g.Day=r.Int();g.Kind=r.Int();}r.End();
        r=new Reader(records[1],HousePositions);r.Match(root);s.HousePlaced=r.Range(0,1)==1;t.HouseX=r.Int();t.HouseZ=r.Int();for(int i=0;i<6;i++){t.Residents[i].HomeX=r.Int();t.Residents[i].HomeZ=r.Int();}r.End();
        r=new Reader(records[2],ShopStates);r.Match(root);t.Spending=r.Int();t.StockCount=r.Int();for(int i=0;i<12;i++)t.Stock[i]=r.Int();r.End();
        r=new Reader(records[3],PlayerVillager);r.Match(root);s.Wallet=r.Int();uint low=(uint)r.Int();int high=r.Int();s.Savings=((long)high<<32)|low;low=(uint)r.Int();high=r.Int();s.Debt=((long)high<<32)|low;
        s.Species=r.Int();s.Palette=r.Int();s.Tool=r.Int();s.Shirt=r.Int();s.HouseTier=r.Int();s.GardenTier=r.Int();s.PendingKind=r.Int();s.PendingTier=r.Int();s.PendingDay=r.Int();s.Tutorial=r.Int();s.MetMask=r.Int();s.Jobs=r.Int();s.JobDay=r.Int();s.MailCount=r.Range(0,128);s.Roof=r.Int();s.Wall=r.Int();s.Fence=r.Int();s.Mailbox=r.Int();s.Exterior=r.Int();s.BackpackTier=r.Int();s.X=r.Int()/1024f;s.Z=r.Int()/1024f;s.Name=r.Text(16);s.Catchphrase=r.Text(32);
        for(int i=0;i<GameState.MaximumPockets;i++)s.Pockets[i]=r.Int();for(int i=0;i<64;i++)s.Owned[i]=r.Int();for(int i=0;i<s.MailCount;i++)s.Mail[i]=r.Int();
        for(int i=0;i<6;i++){var v=t.Residents[i];v.Friendship=r.Int();v.Request=r.Int();v.RequestDay=r.Int();v.Met=r.Int();}
        t.PlacedCount=r.Range(0,256);for(int i=0;i<t.PlacedCount;i++){var p=t.Placed[i];p.Item=r.Int();p.X=r.Int();p.Z=r.Int();p.Rotation=r.Int();p.Room=r.Int();p.Owner=r.Int();}r.End();
        // Reuse all legacy range, placement and catalog validation before use.
        if(root.Version<2)WaterTerrain.Migrate(t);
        return SaveCodec.Decode(SaveCodec.Encode(s));
    }
    public static byte[] EncodeCommit(GameState s,byte[][] records)
    {var w=new Writer(s,Commit,96);for(int i=0;i<4;i++){var r=new Reader(records[i],i);if(r.Sequence!=s.Sequence||r.Seed!=s.Town.Seed)throw new InvalidOperationException("Mixed save commit");w.Int(records[i].Length);w.Int((int)SaveCodec.Crc(records[i],0,records[i].Length-4));}return w.Finish();}
    public static GameState DecodeCommitted(byte[] commit,byte[][] records)
    {var r=new Reader(commit,Commit);if(records.Length!=4)throw new InvalidOperationException("Incomplete save commit");for(int i=0;i<4;i++){var part=new Reader(records[i],i);part.Match(r);if(r.Int()!=records[i].Length||(uint)r.Int()!=SaveCodec.Crc(records[i],0,records[i].Length-4))throw new InvalidOperationException("Save commit mismatch");}r.End();return Decode(records);}
}
