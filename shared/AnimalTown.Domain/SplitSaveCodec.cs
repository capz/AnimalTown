namespace AnimalTown;

// Independently versioned records. The legacy codec remains the import/export
// format and the common semantic validator, never the new storage payload.
public static class SplitSaveCodec
{
    public const int TownData=0,HousePositions=1,ShopStates=2,PlayerVillager=3,Commit=4;
    public const int PartCount=7;
    internal static bool IsProfileTownVersion(int version)=>version==4||version>=6&&version<=10;
    public static int RecordKind(int part)=>part<4?part:part+1;
    public static int ReadVersion(byte[] bytes)=>new Reader(bytes,TownData).Version;
    public static string FileName(int kind,int bank)
    {string[] names={"AT-Town","AT-Houses","AT-Shops","AT-Player","AT-Commit","AT-Villager2-","AT-Villager3-","AT-Villager4-","AT-Residents"};return names[kind]+bank;}
    public static bool IsFile(string name)
    {for(int bank=0;bank<2;bank++)for(int kind=0;kind<9;kind++)if(name==FileName(kind,bank))return true;return name=="AnimalTown0"||name=="AnimalTown1";}
    internal sealed class Writer
    {
        private readonly byte[] bytes;private int at;
        internal Writer(GameState s,int kind,int capacity,int version=4)
        {bytes=new byte[capacity];Int(0x50535441);Int(version);Int(Catalog.Version);Int(kind);Int(s.Sequence);Int(s.Town.Seed);Int(0);}
        internal void Int(int n){if(at>bytes.Length-4)throw new InvalidOperationException("Save record exceeds budget");for(int i=0;i<4;i++)bytes[at++]=(byte)((uint)n>>(i*8));}
        internal void Text(string s){Int(s.Length);for(int i=0;i<s.Length;i++)Int(s[i]);}
        internal void Rle(byte[] source){Int(Runs(source));for(int i=0;i<source.Length;){int n=1;while(i+n<source.Length&&source[i+n]==source[i])n++;Int((source[i]<<24)|n);i+=n;}}
        internal void JobLedger(Jobs.OfflineJobLedger ledger){int words=ledger.WordCount;Int(words);ledger.WriteTo(bytes,at);at+=words*4;}
        internal byte[] Finish(){int length=at+4;for(int i=0;i<4;i++)bytes[24+i]=(byte)(length>>(i*8));Int((int)SaveCodec.Crc(bytes,0,at));if(at==bytes.Length)return bytes;var result=new byte[at];Array.Copy(bytes,result,at);return result;}
    }
    internal sealed class Reader
    {
        private readonly byte[] bytes;private int at;
        internal readonly int Sequence,Seed,Version,CatalogVersion;
        internal Reader(byte[] source,int kind)
        {
            bytes=source;if(bytes.Length<32||bytes.Length>SaveCodec.MaximumBytes)throw new InvalidOperationException("Invalid save record size");
            if(Int()!=0x50535441)throw new InvalidOperationException("Wrong save record magic");Version=Int();
            CatalogVersion=Int();
            if((Version<1||Version>10||Version>=6&&kind!=TownData)||!Catalog.SupportsVersion(CatalogVersion)||Int()!=kind)throw new InvalidOperationException("Wrong save record type or version");
            Sequence=Range(0,int.MaxValue);Seed=Int();if(Int()!=bytes.Length)throw new InvalidOperationException("Save record length mismatch");
            int crc=0;for(int i=0;i<4;i++)crc|=bytes[bytes.Length-4+i]<<(i*8);
            if((uint)crc!=SaveCodec.Crc(bytes,0,bytes.Length-4))throw new InvalidOperationException("Save record checksum failed");
        }
        internal int Int(){if(at>bytes.Length-8)throw new InvalidOperationException("Truncated save record");int n=0;for(int i=0;i<4;i++)n|=bytes[at++]<<(i*8);return n;}
        internal int Range(int min,int max){int n=Int();if(n<min||n>max)throw new InvalidOperationException("Save record value outside range");return n;}
        internal string Text(int limit){int n=Range(0,limit);var c=new char[n];for(int i=0;i<n;i++)c[i]=(char)Range(0,65535);return new string(c);}
        internal void Rle(byte[] target,int max,bool compare=false)
        {int runs=Range(1,target.Length),cell=0;for(int i=0;i<runs;i++){int packed=Int(),mode=(int)((uint)packed>>24),n=packed&0xffffff;if(mode>max||n==0||n>target.Length-cell)throw new InvalidOperationException("Invalid save terrain run");for(int j=0;j<n;j++){if(compare){if(target[cell]!=mode)throw new InvalidOperationException("Save terrain differs from snapshot");}else target[cell]=(byte)mode;cell++;}}if(cell!=target.Length)throw new InvalidOperationException("Incomplete save terrain");}
        internal void SkipWords(int count){if(count<0||count>(bytes.Length-4-at)/4)throw new InvalidOperationException("Truncated ledger");at+=count*4;}
        internal int Offset=>at;
        internal bool HasMore=>at<bytes.Length-4;
        internal void SkipText(int limit){int n=Range(0,limit);for(int i=0;i<n;i++)Range(0,65535);}
        internal void SkipTerrain(){int n=Range(1,Town.Width*Town.Height);for(int i=0;i<n;i++)Int();}
        internal void Match(Reader other){if(Version!=other.Version&&!(Version>=6&&other.Version==4||Version==4&&other.Version>=6)||Seed!=other.Seed||Sequence!=other.Sequence)throw new InvalidOperationException("Mixed save generations");}
        internal void Paths(TownPaths paths,bool compare=false)
        {
            Range(1,1);Range(TownPaths.PackedBytes,TownPaths.PackedBytes);
            for(int i=0;i<TownPaths.PackedBytes;i+=4){int value=Int();for(int j=0;j<4;j++){byte b=(byte)((uint)value>>(j*8));if(compare){if(paths.Packed[i+j]!=b)throw new InvalidOperationException("Readback paths differ from snapshot");}else paths.Packed[i+j]=b;}}
            paths.Validate();
        }
        internal void Traffic(TileTraffic? traffic,bool compare=false)
        {
            Range(1,1);Range(TileTraffic.Bytes,TileTraffic.Bytes);
            for(int i=0;i<TileTraffic.Bytes;i+=4){int value=Int();if(traffic==null)continue;for(int j=0;j<4;j++){byte b=(byte)((uint)value>>(j*8));if(compare){if(traffic.Visits[i+j]!=b)throw new InvalidOperationException("Readback traffic differs from snapshot");}else traffic.Visits[i+j]=b;}}
        }
        internal void JobState(Jobs.TownJobState jobs)
        {Range(Jobs.TownJobState.EncodedWordCount,Jobs.TownJobState.EncodedWordCount);var data=new int[Jobs.TownJobState.EncodedWordCount];for(int i=0;i<data.Length;i++)data[i]=Int();jobs.Restore(data);}
        internal void SnowCheckpoint(SnowWear? snow)
        {
            int length=Range(SnowWear.MinCheckpointWords,SnowWear.MaxCheckpointWords);
            if(length>(bytes.Length-at-4)/4)throw new InvalidOperationException("Truncated snow checkpoint");
            if(snow==null){at+=length*4;return;}
            var words=new int[length];for(int i=0;i<length;i++)words[i]=Int();
            snow.RestoreCheckpoint(words);
        }
        internal void End(){if(at!=bytes.Length-4)throw new InvalidOperationException("Trailing save record data");}
    }
    private static int Runs(byte[] a){int n=1;for(int i=1;i<a.Length;i++)if(a[i]!=a[i-1])n++;return n;}
    public static byte[][] Encode(GameState s)
    {
        s.CaptureProfile();var records=new byte[PartCount][];
        for(int i=0;i<records.Length;i++)records[i]=EncodePart(s,i);return records;
    }
    internal static byte[] EncodePart(GameState s,int part)
    {int guest=s.SuspendVisitor();try{return EncodeResidentPart(s,part);}finally{s.ResumeVisitor(guest);}}
    private static byte[] EncodeResidentPart(GameState s,int part)
    {
        var t=s.Town;
        if(part==0){
        var snow=t.Traffic.Snow.EncodeCheckpoint();
        int version=snow!=null?10:!t.JobState.IsPristine?9:t.DaylightLocation==0?7:8;
        int snowBytes=snow==null?0:4*(snow.Length+1);
        var w=new Writer(s,TownData,2064+TownPaths.PackedBytes+TileTraffic.Bytes+4*(Jobs.TownJobState.EncodedWordCount+1+Runs(t.Tiles)+Runs(t.Elevation))+24*(t.GroundCount+t.PlacedCount)+snowBytes,version);
        w.Int(Town.Width);w.Int(Town.Height);w.Int(TownGenerator.Version);w.Int(t.Day);w.Int(t.Month);w.Rle(t.Tiles);w.Rle(t.Elevation);
        for(int i=0;i<6;i++){var v=t.Residents[i];w.Int(v.X);w.Int(v.Z);w.Text(v.Name);w.Int((int)(v.PosX*1024));w.Int((int)(v.PosZ*1024));}
        w.Int(t.GroundCount);for(int i=0;i<t.GroundCount;i++){var g=t.Ground[i];w.Int(g.Item);w.Int(g.X);w.Int(g.Z);w.Int(g.Owner);w.Int(g.Day);w.Int(g.PackedKind);}
        w.Int(t.PlacedCount);for(int i=0;i<t.PlacedCount;i++){var p=t.Placed[i];w.Int(p.Item);w.Int(p.X);w.Int(p.Z);w.Int(p.Rotation);w.Int(p.Room);w.Int(p.Owner);}
        w.Int(s.ActiveProfile);for(int i=0;i<s.BuildingTypes.Length;i++)w.Int(s.BuildingTypes[i]);
        for(int i=0;i<6;i++){var v=t.Residents[i];w.Int(v.Space);w.Int(v.JourneyBuilding);w.Int(v.JourneyPhase);w.Int(v.Dwell);w.Int((int)(v.Yaw*1024));w.Int(v.JourneyNextX);w.Int(v.JourneyNextZ);}
        t.Paths.Validate();w.Int(1);w.Int(TownPaths.PackedBytes);for(int i=0;i<TownPaths.PackedBytes;i+=4)w.Int(t.Paths.Packed[i]|t.Paths.Packed[i+1]<<8|t.Paths.Packed[i+2]<<16|t.Paths.Packed[i+3]<<24);
        w.Int(1);w.Int(TileTraffic.Bytes);for(int i=0;i<TileTraffic.Bytes;i+=4)w.Int(t.Traffic.Visits[i]|t.Traffic.Visits[i+1]<<8|t.Traffic.Visits[i+2]<<16|t.Traffic.Visits[i+3]<<24);
        if(snow!=null){w.Int(snow.Length);for(int i=0;i<snow.Length;i++)w.Int(snow[i]);}
        if(version>=8)w.Int(t.DaylightLocation);
        if(version>=9){var jobs=t.JobState.Encode();w.Int(jobs.Length);for(int i=0;i<jobs.Length;i++)w.Int(jobs[i]);}
        return w.Finish();
        }
        if(part==1){
        var w=new Writer(s,HousePositions,256);for(int i=0;i<GameState.ProfileCount;i++){w.Int(s.HomePlaced(i)?1:0);w.Int(s.HomeX(i));w.Int(s.HomeZ(i));}for(int i=0;i<6;i++){w.Int(t.Residents[i].HomeX);w.Int(t.Residents[i].HomeZ);}return w.Finish();
        }
        if(part==2){
        int ledgerWords=t.Jobs.Initialized?t.Jobs.WordCount:0;
        var w=new Writer(s,ShopStates,ledgerWords==0?708:712+ledgerWords*4);w.Int(t.Spending);w.Int(t.StockCount);for(int i=0;i<12;i++)w.Int(t.Stock[i]);var shop=t.Shop.Encode();w.Int(shop.Length);for(int i=0;i<shop.Length;i++)w.Int(shop[i]);
        if(ledgerWords!=0)w.JobLedger(t.Jobs);return w.Finish();
        }
        if(part<3||part>=PartCount)throw new ArgumentOutOfRangeException(nameof(part));
        return ProfileCodec.Encode(s,part-3);
    }
    public static byte[][] EncodeLegacy(GameState s)
    {
        SaveCodec.RequireLegacyProfile(s);
        var t=s.Town;var records=new byte[4][];
        var w=new Writer(s,TownData,2048+4*(Runs(t.Tiles)+Runs(t.Elevation))+24*t.GroundCount,3);
        w.Int(Town.Width);w.Int(Town.Height);w.Int(TownGenerator.Version);w.Int(t.Day);w.Int(t.Month);w.Rle(t.Tiles);w.Rle(t.Elevation);
        for(int i=0;i<6;i++){var v=t.Residents[i];w.Int(v.X);w.Int(v.Z);w.Text(v.Name);w.Int((int)(v.PosX*1024));w.Int((int)(v.PosZ*1024));}
        w.Int(t.GroundCount);for(int i=0;i<t.GroundCount;i++){var g=t.Ground[i];w.Int(g.Item);w.Int(g.X);w.Int(g.Z);w.Int(g.Owner);w.Int(g.Day);w.Int(g.PackedKind);}records[0]=w.Finish();
        w=new Writer(s,HousePositions,128,3);w.Int(s.HousePlaced?1:0);w.Int(t.HouseX);w.Int(t.HouseZ);for(int i=0;i<6;i++){w.Int(t.Residents[i].HomeX);w.Int(t.Residents[i].HomeZ);}records[1]=w.Finish();
        w=new Writer(s,ShopStates,128,3);w.Int(t.Spending);w.Int(t.StockCount);for(int i=0;i<12;i++)w.Int(t.Stock[i]);records[2]=w.Finish();
        w=new Writer(s,PlayerVillager,4096+24*t.PlacedCount,3);
        int[] fields={s.Wallet,(int)s.Savings,(int)(s.Savings>>32),(int)s.Debt,(int)(s.Debt>>32),s.Species,s.Palette,s.Tool,s.Shirt,s.HouseTier,s.GardenTier,s.PendingKind,s.PendingTier,s.PendingDay,s.Tutorial,s.MetMask,s.Jobs,s.JobDay,s.MailCount,s.Roof,s.Wall,s.Fence,s.Mailbox,s.Exterior,s.BackpackTier,(int)(s.X*1024),(int)(s.Z*1024)};
        for(int i=0;i<fields.Length;i++)w.Int(fields[i]);w.Text(s.Name);w.Text(s.Catchphrase);
        for(int i=0;i<GameState.MaximumPockets;i++)w.Int(s.Pockets[i]);for(int i=0;i<64;i++)w.Int(s.Owned[i]);for(int i=0;i<s.MailCount;i++)w.Int(s.Mail[i]);
        for(int i=0;i<6;i++){var v=t.Residents[i];w.Int(v.Friendship);w.Int(v.Request);w.Int(v.RequestDay);w.Int(v.Met);}
        w.Int(t.PlacedCount);for(int i=0;i<t.PlacedCount;i++){var p=t.Placed[i];w.Int(p.Item);w.Int(p.X);w.Int(p.Z);w.Int(p.Rotation);w.Int(p.Room);w.Int(p.Owner);}var buildingFields=BuildingSave.EncodeLegacy(s);for(int i=0;i<buildingFields.Length;i++)w.Int(buildingFields[i]);records[3]=w.Finish();return records;
    }
    public static GameState Decode(byte[][] records)
    {return Decode(records,null);}
    internal static GameState Decode(byte[][] records,Town? snapshot,Jobs.OfflineJobLedger? textMemo=null,int referencedEconomySequence=-1)
    {
        if(records.Length==PartCount)return DecodeProfiles(records,snapshot,textMemo,referencedEconomySequence);if(records.Length!=4)throw new InvalidOperationException("Incomplete save set");var r=new Reader(records[0],TownData);var root=r;
        if(snapshot!=null&&(r.Version!=4||r.Seed!=snapshot.Seed))throw new InvalidOperationException("Readback snapshot identity mismatch");
        var s=snapshot==null?new GameState(r.Seed):new GameState(r.Seed,snapshot);s.Sequence=r.Sequence;var t=s.Town;if(snapshot==null)t.Paths.Clear();
        r.Range(Town.Width,Town.Width);r.Range(Town.Height,Town.Height);r.Range(1,TownGenerator.Version);t.Day=r.Int();t.Month=r.Int();r.Rle(t.Tiles,r.Version>=2?(int)Terrain.CoastWest:7,snapshot!=null);r.Rle(t.Elevation,TerrainElevation.MaximumMode,snapshot!=null);
        for(int i=0;i<6;i++){var v=t.Residents[i];v.X=r.Int();v.Z=r.Int();v.Name=r.Text(24);v.PosX=r.Int()/1024f;v.PosZ=r.Int()/1024f;}
        t.GroundCount=r.Range(0,512);for(int i=0;i<t.GroundCount;i++){var g=t.Ground[i];g.Item=r.Int();g.X=r.Int();g.Z=r.Int();g.Owner=r.Int();g.Day=r.Int();g.RestoreKind(r.Range(0,12));}r.End();
        r=new Reader(records[1],HousePositions);r.Match(root);s.HousePlaced=r.Range(0,1)==1;t.HouseX=r.Int();t.HouseZ=r.Int();for(int i=0;i<6;i++){t.Residents[i].HomeX=r.Int();t.Residents[i].HomeZ=r.Int();}r.End();
        r=new Reader(records[2],ShopStates);r.Match(root);t.Spending=r.Int();t.StockCount=r.Int();for(int i=0;i<12;i++)t.Stock[i]=r.Int();r.End();
        r=new Reader(records[3],PlayerVillager);r.Match(root);s.Wallet=r.Int();uint low=(uint)r.Int();int high=r.Int();s.Savings=((long)high<<32)|low;low=(uint)r.Int();high=r.Int();s.Debt=((long)high<<32)|low;
        s.Species=r.Int();s.Palette=r.Int();s.Tool=r.Int();s.Shirt=r.Int();s.HouseTier=r.Int();s.GardenTier=r.Int();s.PendingKind=r.Int();s.PendingTier=r.Int();s.PendingDay=r.Int();s.Tutorial=r.Int();s.MetMask=r.Int();s.Jobs=r.Int();s.JobDay=r.Int();s.MailCount=r.Range(0,128);s.Roof=r.Int();s.Wall=r.Int();s.Fence=r.Int();s.Mailbox=r.Int();s.Exterior=r.Int();s.BackpackTier=r.Int();s.X=r.Int()/1024f;s.Z=r.Int()/1024f;s.Name=r.Text(16);s.Catchphrase=r.Text(32);
        for(int i=0;i<GameState.MaximumPockets;i++)s.Pockets[i]=r.Int();for(int i=0;i<64;i++)s.Owned[i]=r.Int();for(int i=0;i<s.MailCount;i++)s.Mail[i]=r.Int();
        for(int i=0;i<6;i++){var v=t.Residents[i];v.Friendship=r.Int();v.Request=r.Int();v.RequestDay=r.Int();v.Met=r.Int();}
        t.PlacedCount=r.Range(0,256);for(int i=0;i<t.PlacedCount;i++){var p=t.Placed[i];p.Item=r.Int();p.X=r.Int();p.Z=r.Int();p.Rotation=r.Int();p.Room=r.Int();p.Owner=r.Int();}if(root.Version>=3){var fields=new int[BuildingSave.LegacyFieldCount];for(int i=0;i<fields.Length;i++)fields[i]=r.Int();BuildingSave.Decode(s,fields);}r.End();
        // Reuse all legacy range, placement and catalog validation before use.
        if(root.Version<2)WaterTerrain.Migrate(t);
        SaveCodec.UpgradeShop(s);
        return snapshot==null?SaveCodec.Decode(SaveCodec.Encode(s)):SaveCodec.ValidateSnapshot(SaveCodec.Encode(s),snapshot);
    }
    private static GameState DecodeProfiles(byte[][] records,Town? snapshot,Jobs.OfflineJobLedger? textMemo,int referencedEconomySequence)
    {
        var r=new Reader(records[0],TownData);if(!IsProfileTownVersion(r.Version))throw new InvalidOperationException("Invalid shared town schema");var root=r;
        var s=snapshot==null?new GameState(r.Seed):new GameState(r.Seed,snapshot);s.Sequence=r.Sequence;var t=s.Town;if(snapshot==null)t.Paths.Clear();
        r.Range(Town.Width,Town.Width);r.Range(Town.Height,Town.Height);r.Range(1,TownGenerator.Version);t.Day=r.Int();t.Month=r.Int();r.Rle(t.Tiles,(int)Terrain.CoastWest,snapshot!=null);r.Rle(t.Elevation,TerrainElevation.MaximumMode,snapshot!=null);
        for(int i=0;i<6;i++){var v=t.Residents[i];v.X=r.Int();v.Z=r.Int();v.Name=r.Text(24);v.PosX=r.Int()/1024f;v.PosZ=r.Int()/1024f;}
        t.GroundCount=r.Range(0,512);for(int i=0;i<t.GroundCount;i++){var g=t.Ground[i];g.Item=r.Int();g.X=r.Int();g.Z=r.Int();g.Owner=r.Int();g.Day=r.Int();g.RestoreKind(r.Range(0,12));}
        t.PlacedCount=r.Range(0,256);for(int i=0;i<t.PlacedCount;i++){var p=t.Placed[i];p.Item=r.Int();p.X=r.Int();p.Z=r.Int();p.Rotation=r.Int();p.Room=r.Int();p.Owner=r.Int();}
        int active=r.Range(0,3);for(int i=0;i<s.BuildingTypes.Length;i++)s.BuildingTypes[i]=(byte)r.Range(0,255);
        for(int i=0;i<6;i++){var v=t.Residents[i];v.Space=r.Range(0,43);v.JourneyBuilding=r.Range(-1,BuildingWorld.Count-1);v.JourneyPhase=r.Range(0,5);v.Dwell=r.Range(0,36000);v.Yaw=r.Range(-6434,6434)/1024f;v.JourneyNextX=r.Range(-1,Town.Width-1);v.JourneyNextZ=r.Range(-1,Town.Height-1);if(!BuildingWorld.ValidSpace(v.Space)||(v.JourneyNextX<0)!=(v.JourneyNextZ<0)||v.JourneyPhase==1&&v.JourneyBuilding<0)throw new InvalidOperationException("Invalid shared NPC journey");}
        if(r.Version>=6)r.Paths(t.Paths,snapshot!=null);else if(snapshot!=null&&t.Paths.Count!=0)throw new InvalidOperationException("Legacy snapshot contains paths");
        if(r.Version>=7)r.Traffic(t.Traffic,snapshot!=null);
        if(r.Version>=10)r.SnowCheckpoint(t.Traffic.Snow);
        if(r.Version>=8)t.DaylightLocation=r.Range(r.Version>=9?0:1,DaylightLocations.Count);
        if(r.Version>=9)r.JobState(t.JobState);
        r.End();
        for(int i=0;i<GameState.ProfileCount;i++)s.Profiles[i]=ProfileCodec.Decode(s,i,records[3+i]);
        r=new Reader(records[1],HousePositions);r.Match(root);for(int i=0;i<GameState.ProfileCount;i++){var p=s.Profiles[i];if(r.Range(0,1)!=(p.HousePlaced?1:0)||r.Int()!=p.HouseX||r.Int()!=p.HouseZ)throw new InvalidOperationException("Profile plot differs from shared houses");}for(int i=0;i<6;i++){t.Residents[i].HomeX=r.Int();t.Residents[i].HomeZ=r.Int();}r.End();
        r=new Reader(records[2],ShopStates);if(referencedEconomySequence<0)r.Match(root);else if(r.Sequence!=referencedEconomySequence||r.Seed!=root.Seed)throw new InvalidOperationException("Mixed economic reference");t.Spending=r.Int();t.StockCount=r.Int();for(int i=0;i<12;i++)t.Stock[i]=r.Int();
        if(r.HasMore){int length=r.Range(154,154);var shop=new int[length];for(int i=0;i<length;i++)shop[i]=r.Int();t.Shop.Decode(shop);}else SaveCodec.UpgradeShop(s);
        if(r.HasMore){int count=r.Range(7,Jobs.OfflineJobLedger.MaximumWords);if(count>(records[2].Length-r.Offset-4)/4)throw new InvalidOperationException("Truncated economic job ledger");t.Jobs.Decode(records[2],r.Offset,count,snapshot?.Jobs??textMemo);r.SkipWords(count);}
        r.End();s.RestoreProfile(active);s.MigrateJobTutorial(root.CatalogVersion);
        return SaveStateValidation.Finish(s);
    }
    public static byte[] EncodeCommit(GameState s,byte[][] records)
    {if(records.Length!=4&&records.Length!=PartCount)throw new InvalidOperationException("Incomplete save commit");var w=new Writer(s,Commit,128,records.Length==4?3:4);for(int i=0;i<records.Length;i++){var r=new Reader(records[i],RecordKind(i));if(r.Sequence!=s.Sequence||r.Seed!=s.Town.Seed)throw new InvalidOperationException("Mixed save commit");w.Int(records[i].Length);w.Int((int)SaveCodec.Crc(records[i],0,records[i].Length-4));}return w.Finish();}
    public static void VerifyCommitted(byte[] commit,byte[][] records)
    {var r=new Reader(commit,Commit);if(records.Length!=(r.Version>=4?PartCount:4))throw new InvalidOperationException("Incomplete save commit");for(int i=0;i<records.Length;i++){var part=new Reader(records[i],RecordKind(i));part.Match(r);if(r.Int()!=records[i].Length||(uint)r.Int()!=SaveCodec.Crc(records[i],0,records[i].Length-4))throw new InvalidOperationException("Save commit mismatch");}r.End();}
    public static GameState DecodeCommitted(byte[] commit,byte[][] records)
    {VerifyCommitted(commit,records);return Decode(records);}
    internal static void ValidateSnapshot(byte[] commit,byte[][] records,Town snapshot)
    {VerifyCommitted(commit,records);Decode(records,snapshot);}
}
