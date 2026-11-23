namespace AnimalTown;

// Explicit bounded wire format, independent of host endianness and reflection.
public static class SaveCodec
{
    public const int MaximumBytes=4*1024*1024;
    private static void Put(byte[]? b,ref int at,int n){if(b==null){at+=4;return;}for(int i=0;i<4;i++)b[at++]=(byte)((uint)n>>(i*8));}
    private static int Get(byte[] b,ref int at)
    { if(at>b.Length-4)throw new InvalidOperationException("Truncated save");int n=0;for(int i=0;i<4;i++)n|=b[at++]<<(8*i);return n; }
    private static void Text(byte[]? b,ref int at,string s)
    { Put(b,ref at,s.Length);for(int i=0;i<s.Length;i++)Put(b,ref at,s[i]); }
    private static string Text(byte[] b,ref int at,int limit)
    { int n=Get(b,ref at);if(n<0||n>limit)throw new InvalidOperationException("Invalid text");var c=new char[n];for(int i=0;i<n;i++)c[i]=(char)Get(b,ref at);return new string(c); }
    // IEEE CRC32, two nibble lookups per byte instead of eight bit branches.
    // Only 64 bytes of lookup data; identical on-disk checksums for all schemas.
    private static readonly uint[] crcNibbles={0,0x1db71064,0x3b6e20c8,0x26d930ac,
        0x76dc4190,0x6b6b51f4,0x4db26158,0x5005713c,0xedb88320,0xf00f9344,
        0xd6d6a3e8,0xcb61b38c,0x9b64c2b0,0x86d3d2d4,0xa00ae278,0xbdbdf21c};
    public static uint Crc(byte[] b,int start,int count)
    {uint c=0xffffffff;for(int i=start;i<start+count;i++){c^=b[i];c=(c>>4)^crcNibbles[c&15];c=(c>>4)^crcNibbles[c&15];}return ~c;}
    public static byte[] Encode(GameState s)
    {int guest=s.SuspendVisitor();try{return EncodeCore(s,true);}finally{s.ResumeVisitor(guest);}}
    public static byte[] EncodeLegacy(GameState s)=>EncodeCore(s,false);
    internal static void RequireLegacyProfile(GameState s)
    {
        if(s.Town.DaylightLocation!=0)throw new InvalidOperationException("Legacy export cannot discard the daylight location");
        if(!s.Town.JobState.IsPristine)throw new InvalidOperationException("Legacy export cannot discard job availability or random streams");
        if(s.Town.Traffic.Snow.HasCheckpoint)throw new InvalidOperationException("Legacy export cannot discard snow dents");
        if(s.Town.Jobs.Initialized)throw new InvalidOperationException("Legacy export cannot discard villager identities and jobs");
        if(s.Town.LostAndFound.HasState)throw new InvalidOperationException("Legacy export cannot discard lost-property recovery records");
        if(s.Town.Koban.X!=KobanSite.PreferredX||s.Town.Koban.Z!=KobanSite.PreferredZ||s.BuildingTypes[KobanSite.Building]!=KobanSite.Definition)throw new InvalidOperationException("Legacy export cannot discard a changed koban plot or type");
        if(s.Space==KobanSite.Room)throw new InvalidOperationException("Leave the koban before exporting a legacy save");
        for(int i=0;i<6;i++)if(s.Town.Residents[i].Space==KobanSite.Room||s.Town.Residents[i].JourneyBuilding==KobanSite.Building)throw new InvalidOperationException("Legacy export cannot discard a koban journey");
        if(s.ActiveProfile!=0||!s.Profiles[0].Created)throw new InvalidOperationException("Legacy export requires the original single player");
        for(int i=1;i<GameState.ProfileCount;i++)if(s.Profiles[i].Created)throw new InvalidOperationException("Legacy export cannot discard villager profiles");
        for(int i=0;i<TownShop.StoredSaleCount;i++)if(s.Town.Shop.SaleItems[i]!=0)throw new InvalidOperationException("Legacy export cannot discard shop listings");
        for(int i=0;i<6;i++)if(s.Town.Shop.NpcCount[i]!=0)throw new InvalidOperationException("Legacy export cannot discard NPC purchases");
        for(int i=0;i<GameState.ProfileCount;i++)if(s.Town.Shop.Proceeds[i]!=0)throw new InvalidOperationException("Legacy export cannot discard shop earnings");
    }
    private static byte[] EncodeCore(GameState s,bool profiles)
    {
        if(!profiles)RequireLegacyProfile(s);
        s.CaptureProfile();
        var snow=profiles?s.Town.Traffic.Snow.EncodeCheckpoint():null;
        int elevationRuns=1;for(int i=1;i<Town.Cells;i++)if(s.Town.Elevation[i]!=s.Town.Elevation[i-1])elevationRuns++;
        int jobWords=profiles&&s.Town.Jobs.Initialized?s.Town.Jobs.WordCount:0;
        int version=profiles?16:7;
        var recovery=profiles?s.Town.LostAndFound.Encode():null;
        var profileRecords=profiles?new byte[GameState.ProfileCount][]:null;
        if(profileRecords!=null)for(int i=0;i<profileRecords.Length;i++)profileRecords[i]=ProfileCodec.Encode(s,i);
        int exactLength=Write(null);if(exactLength>MaximumBytes)throw new InvalidOperationException("Save exceeds format budget");
        var output=new byte[exactLength];if(Write(output)!=exactLength)throw new InvalidOperationException("Snapshot changed while encoding");return output;
        int Write(byte[]? b)
        {
        int p=0;Put(b,ref p,0x41544f57);Put(b,ref p,version);Put(b,ref p,Catalog.Version);Put(b,ref p,s.Sequence);
        Put(b,ref p,0);Put(b,ref p,0);
        Put(b,ref p,Town.Width);Put(b,ref p,Town.Height);
        int[] f={s.Town.Seed,s.Town.Day,s.Town.Month,s.Wallet,(int)s.Savings,(int)(s.Savings>>32),(int)s.Debt,(int)(s.Debt>>32),s.Species,s.Palette,s.Tool,s.Shirt,s.HouseTier,s.GardenTier,s.PendingKind,s.PendingTier,s.PendingDay,s.Tutorial,s.MetMask,s.Jobs,s.JobDay,s.MailCount,s.Roof,s.Wall,s.Fence,s.Mailbox,s.Exterior,s.HousePlaced?1:0,(int)(s.X*1024),(int)(s.Z*1024),s.Town.HouseX,s.Town.HouseZ,s.Town.Spending,s.Town.StockCount};
        for(int i=0;i<f.Length;i++)Put(b,ref p,f[i]);Put(b,ref p,s.BackpackTier);Text(b,ref p,s.Name);Text(b,ref p,s.Catchphrase);
        for(int i=0;i<GameState.MaximumPockets;i++)Put(b,ref p,s.Pockets[i]);for(int i=0;i<64;i++)Put(b,ref p,s.Owned[i]);for(int i=0;i<s.MailCount;i++)Put(b,ref p,s.Mail[i]);
        int countAt=p;Put(b,ref p,0);int runs=0;
        for(int i=0;i<Town.Cells;)
        { byte terrain=s.Town.Tiles[i];int length=1;while(i+length<Town.Cells&&s.Town.Tiles[i+length]==terrain)length++;Put(b,ref p,(terrain<<24)|length);i+=length;runs++; }
        Put(b,ref countAt,runs);
        for(int i=0;i<12;i++)Put(b,ref p,s.Town.Stock[i]);
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];Put(b,ref p,v.X);Put(b,ref p,v.Z);Put(b,ref p,v.Friendship);Put(b,ref p,v.Request);Put(b,ref p,v.RequestDay);Put(b,ref p,v.Met);}
        Put(b,ref p,s.Town.GroundCount);for(int i=0;i<s.Town.GroundCount;i++){var g=s.Town.Ground[i];Put(b,ref p,g.Item);Put(b,ref p,g.X);Put(b,ref p,g.Z);Put(b,ref p,g.Owner);Put(b,ref p,g.Day);Put(b,ref p,g.PackedKind);}
        Put(b,ref p,s.Town.PlacedCount);for(int i=0;i<s.Town.PlacedCount;i++){var q=s.Town.Placed[i];Put(b,ref p,q.Item);Put(b,ref p,q.X);Put(b,ref p,q.Z);Put(b,ref p,q.Rotation);Put(b,ref p,q.Room);Put(b,ref p,q.Owner);}
        Put(b,ref p,TownGenerator.Version);Put(b,ref p,elevationRuns);
        for(int i=0;i<Town.Cells;){byte mode=s.Town.Elevation[i];int length=1;while(i+length<Town.Cells&&s.Town.Elevation[i+length]==mode)length++;Put(b,ref p,(mode<<24)|length);i+=length;}
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];Text(b,ref p,v.Name);Put(b,ref p,v.HomeX);Put(b,ref p,v.HomeZ);Put(b,ref p,(int)(v.PosX*1024));Put(b,ref p,(int)(v.PosZ*1024));}
        var buildingFields=profiles?BuildingSave.Encode(s):BuildingSave.EncodeLegacy(s);for(int i=0;i<buildingFields.Length;i++)Put(b,ref p,buildingFields[i]);
        if(profiles){Put(b,ref p,s.ActiveProfile);for(int i=0;i<GameState.ProfileCount;i++){var data=profileRecords![i];Put(b,ref p,data.Length);if(b!=null)Array.Copy(data,0,b,p,data.Length);p+=data.Length;}}
        if(profiles){s.Town.Paths.Validate();Put(b,ref p,1);Put(b,ref p,TownPaths.PackedBytes);if(b!=null)Array.Copy(s.Town.Paths.Packed,0,b,p,TownPaths.PackedBytes);p+=TownPaths.PackedBytes;}
        if(profiles){var shop=s.Town.Shop.Encode();Put(b,ref p,shop.Length);for(int i=0;i<shop.Length;i++)Put(b,ref p,shop[i]);}
        if(profiles){Put(b,ref p,1);Put(b,ref p,TileTraffic.Bytes);if(b!=null)Array.Copy(s.Town.Traffic.Visits,0,b,p,TileTraffic.Bytes);p+=TileTraffic.Bytes;}
        if(version>=12)Put(b,ref p,s.Town.DaylightLocation);
        if(version>=13){var jobs=s.Town.JobState.Encode();Put(b,ref p,jobs.Length);for(int i=0;i<jobs.Length;i++)Put(b,ref p,jobs[i]);}
        if(profiles){
            Put(b,ref p,snow?.Length??0);if(snow!=null)for(int i=0;i<snow.Length;i++)Put(b,ref p,snow[i]);
            Put(b,ref p,jobWords);if(jobWords!=0){if(b!=null)s.Town.Jobs.WriteTo(b,p);p+=jobWords*4;}
            Put(b,ref p,s.Town.Koban.X);Put(b,ref p,s.Town.Koban.Z);
            Put(b,ref p,recovery!.Length);for(int i=0;i<recovery.Length;i++)Put(b,ref p,recovery[i]);
        }
        if(b!=null){int h=16;Put(b,ref h,p);Put(b,ref h,(int)Crc(b,24,p-24));}return p;
        }
    }
    private static int Range(byte[] b,ref int p,int min,int max)
    { int n=Get(b,ref p);if(n<min||n>max)throw new InvalidOperationException("Save value outside range");return n; }
    public static GameState Decode(byte[] b)
    {return Decode(b,null);}
    internal static GameState ValidateSnapshot(byte[] b,Town snapshot)
    {return Decode(b,snapshot);}
    private static GameState Decode(byte[] b,Town? snapshot)
    {
        if(b.Length<160||b.Length>MaximumBytes)throw new InvalidOperationException("Invalid save size");int p=0;
        if(Get(b,ref p)!=0x41544f57)throw new InvalidOperationException("Invalid save magic");int version=Get(b,ref p);
        int catalogVersion=Get(b,ref p);
        if((version<4||version>16)||!Catalog.SupportsVersion(catalogVersion))throw new InvalidOperationException("Unsupported save version");
        if(snapshot!=null&&version<11)throw new InvalidOperationException("Readback requires current save format");
        int seq=Range(b,ref p,0,int.MaxValue);if(Get(b,ref p)!=b.Length||(uint)Get(b,ref p)!=Crc(b,24,b.Length-24))throw new InvalidOperationException("Save checksum failed");
        // Town dimensions must match exactly. Old larger towns are not cropped
        // or regenerated; creating a smaller town requires deleting town data.
        int sourceWidth=Range(b,ref p,Town.Width,Town.Width),sourceHeight=Range(b,ref p,Town.Height,Town.Height);
        int seed=Get(b,ref p);if(snapshot!=null&&seed!=snapshot.Seed)throw new InvalidOperationException("Readback snapshot seed mismatch");
        var s=snapshot==null?new GameState(seed):new GameState(seed,snapshot);if(snapshot==null)Array.Clear(s.Town.Elevation,0,s.Town.Elevation.Length);s.Sequence=seq;s.Town.Day=Get(b,ref p);s.Town.Month=Get(b,ref p);s.Wallet=Range(b,ref p,0,99999);
        uint low=(uint)Get(b,ref p);int high=Range(b,ref p,0,int.MaxValue);s.Savings=((long)high<<32)|low;
        low=(uint)Get(b,ref p);high=Range(b,ref p,0,0);s.Debt=((long)high<<32)|low;if(s.Debt>1600000)throw new InvalidOperationException("Debt invalid");
        s.Species=Range(b,ref p,0,5);s.Palette=Range(b,ref p,0,7);s.Tool=Range(b,ref p,0,Catalog.Items.Length-1);s.Shirt=Range(b,ref p,0,Catalog.Items.Length-1);s.HouseTier=Range(b,ref p,0,5);s.GardenTier=Range(b,ref p,0,4);s.PendingKind=Range(b,ref p,0,2);s.PendingTier=Range(b,ref p,0,5);s.PendingDay=Get(b,ref p);
        s.Tutorial=Range(b,ref p,0,7);s.MetMask=Range(b,ref p,0,63);s.Jobs=Range(b,ref p,0,2);s.JobDay=Get(b,ref p);s.MailCount=Range(b,ref p,0,128);
        s.Roof=Range(b,ref p,0,11);s.Wall=Range(b,ref p,0,11);s.Fence=Range(b,ref p,0,5);s.Mailbox=Range(b,ref p,0,5);s.Exterior=Range(b,ref p,0,4);s.HousePlaced=Range(b,ref p,0,1)==1;
        s.X=Range(b,ref p,0,sourceWidth*1024-1)/1024f;s.Z=Range(b,ref p,0,sourceHeight*1024-1)/1024f;s.Town.HouseX=Range(b,ref p,1,sourceWidth-15);s.Town.HouseZ=Range(b,ref p,1,sourceHeight-13);s.Town.Spending=Range(b,ref p,0,int.MaxValue);s.Town.StockCount=Range(b,ref p,6,12);
        s.BackpackTier=Range(b,ref p,0,2);
        s.Name=Text(b,ref p,16);s.Catchphrase=Text(b,ref p,32);
        for(int i=0;i<GameState.MaximumPockets;i++)s.Pockets[i]=Range(b,ref p,0,Catalog.Items.Length-1);
        for(int i=s.Capacity;i<GameState.MaximumPockets;i++)if(s.Pockets[i]!=0)throw new InvalidOperationException("Locked backpack slot occupied");
        for(int i=0;i<64;i++)s.Owned[i]=Range(b,ref p,0,1);for(int i=0;i<s.MailCount;i++)s.Mail[i]=Range(b,ref p,1-Catalog.Items.Length,Catalog.Items.Length-1);
        {
            int runCount=Range(b,ref p,1,Town.Cells),cell=0;
            for(int i=0;i<runCount;i++){
                int packed=Get(b,ref p),terrain=(int)((uint)packed>>24),length=packed&0xffffff;
                if(terrain>(version>=6?(int)Terrain.CoastWest:7)||length==0||length>Town.Cells-cell)throw new InvalidOperationException("Terrain run invalid");
                for(int j=0;j<length;j++,cell++){
                    if(snapshot!=null){if(s.Town.Tiles[cell]!=terrain)throw new InvalidOperationException("Readback terrain differs from snapshot");}
                    else s.Town.Tiles[cell]=(byte)terrain;
                }
            }
            if(cell!=Town.Cells)throw new InvalidOperationException("Incomplete terrain");
        }
        for(int i=0;i<12;i++)s.Town.Stock[i]=Range(b,ref p,0,Catalog.Items.Length-1);
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];v.X=Range(b,ref p,0,sourceWidth-1);v.Z=Range(b,ref p,0,sourceHeight-1);v.Friendship=Range(b,ref p,0,255);v.Request=Range(b,ref p,0,Catalog.Items.Length-1);v.RequestDay=Get(b,ref p);v.Met=Range(b,ref p,0,1);}
        int groundCount=Range(b,ref p,0,512);s.Town.GroundCount=0;
        for(int i=0;i<groundCount;i++){
            var g=s.Town.Ground[s.Town.GroundCount];g.Item=Range(b,ref p,1,Catalog.Items.Length-1);g.X=Range(b,ref p,0,sourceWidth-1);g.Z=Range(b,ref p,0,sourceHeight-1);g.Owner=Range(b,ref p,0,3);g.Day=Get(b,ref p);g.RestoreKind(Range(b,ref p,0,12));
            s.Town.GroundCount++;
        }
        int count=Range(b,ref p,0,256);s.Town.PlacedCount=0;
        for(int i=0;i<count;i++){var q=s.Town.Placed[i];q.Item=Range(b,ref p,1,Catalog.Items.Length-1);q.X=Get(b,ref p);q.Z=Get(b,ref p);q.Rotation=Range(b,ref p,0,3);q.Room=Range(b,ref p,0,version>=8?43:version>=7?25:4);q.Owner=Range(b,ref p,0,version>=8?9:version>=7?6:0);s.Town.PlacedCount++;}
        if(version>=5){
            Range(b,ref p,1,TownGenerator.Version);int elevationRuns=Range(b,ref p,1,Town.Cells),cell=0;
            for(int i=0;i<elevationRuns;i++){int packed=Get(b,ref p),mode=(int)((uint)packed>>24),length=packed&0xffffff;if(mode>TerrainElevation.MaximumMode||length==0||length>Town.Cells-cell)throw new InvalidOperationException("Invalid saved elevation");for(int j=0;j<length;j++){if(snapshot!=null){if(s.Town.Elevation[cell]!=mode)throw new InvalidOperationException("Readback elevation differs from snapshot");}else s.Town.Elevation[cell]=(byte)mode;cell++;}}
            if(cell!=Town.Cells)throw new InvalidOperationException("Incomplete saved elevation");
            for(int i=0;i<6;i++){var v=s.Town.Residents[i];v.Name=Text(b,ref p,24);v.HomeX=Range(b,ref p,1,Town.Width-4);v.HomeZ=Range(b,ref p,1,Town.Height-4);v.PosX=Range(b,ref p,0,Town.Width*1024-1)/1024f;v.PosZ=Range(b,ref p,0,Town.Height*1024-1)/1024f;}
        }else for(int i=0;i<6;i++){var v=s.Town.Residents[i];v.PosX=v.X+.5f;v.PosZ=v.Z+.5f;}
        if(version>=7){var fields=new int[version>=16?BuildingSave.FieldCount:version>=8?BuildingSave.ProfileFieldCount:BuildingSave.LegacyFieldCount];for(int i=0;i<fields.Length;i++)fields[i]=Get(b,ref p);BuildingSave.Decode(s,fields);}
        if(version>=8){
            var body=new VillagerProfile{Created=true};body.Capture(s);int active=Range(b,ref p,0,GameState.ProfileCount-1);
            for(int i=0;i<GameState.ProfileCount;i++){int length=Range(b,ref p,32,2048);if(p>b.Length-length)throw new InvalidOperationException("Truncated profile");var data=new byte[length];Array.Copy(b,p,data,0,length);p+=length;s.Profiles[i]=ProfileCodec.Decode(s,i,data);}
            body.Created=s.Profiles[active].Created;
            if(!ProfileCodec.Same(body,s.Profiles[active]))throw new InvalidOperationException("Selected profile differs from active state");s.RestoreProfile(active);
        }else s.CaptureProfile();
        for(int a=0;a<GameState.ProfileCount;a++)if(s.HomePlaced(a))for(int c=a+1;c<GameState.ProfileCount;c++)if(s.HomePlaced(c)&&s.HomeX(a)<s.HomeX(c)+15&&s.HomeX(a)+15>s.HomeX(c)&&s.HomeZ(a)<s.HomeZ(c)+13&&s.HomeZ(a)+13>s.HomeZ(c))throw new InvalidOperationException("Overlapping player home plots");
        // Main's v9 stores paths (marker 1); the shop prerelease's v9 stores
        // only its 154-word shop extension. V10 explicitly stores both, in order.
        int extension=version>=9?Get(b,ref p):0;
        if(version==9&&extension!=1&&extension!=154)throw new InvalidOperationException("Invalid v9 save extension");
        if(version>=10||extension==1){
            if(extension!=1)throw new InvalidOperationException("Missing saved paths");
            Range(b,ref p,TownPaths.PackedBytes,TownPaths.PackedBytes);if(p>b.Length-TownPaths.PackedBytes)throw new InvalidOperationException("Truncated paths");
            for(int i=0;i<TownPaths.PackedBytes;i++){if(snapshot!=null){if(s.Town.Paths.Packed[i]!=b[p+i])throw new InvalidOperationException("Readback paths differ from snapshot");}else s.Town.Paths.Packed[i]=b[p+i];}p+=TownPaths.PackedBytes;s.Town.Paths.Validate();
        }else if(snapshot==null)s.Town.Paths.Clear();
        if(version>=10||extension==154){int length=version>=10?Range(b,ref p,154,154):154;var shop=new int[length];for(int i=0;i<length;i++)shop[i]=Get(b,ref p);s.Town.Shop.Decode(shop);}
        else UpgradeShop(s);
        if(version>=11){
            Range(b,ref p,1,1);Range(b,ref p,TileTraffic.Bytes,TileTraffic.Bytes);if(p>b.Length-TileTraffic.Bytes)throw new InvalidOperationException("Truncated tile traffic");
            for(int i=0;i<TileTraffic.Bytes;i++){if(snapshot!=null){if(s.Town.Traffic.Visits[i]!=b[p+i])throw new InvalidOperationException("Readback traffic differs from snapshot");}else s.Town.Traffic.Visits[i]=b[p+i];}p+=TileTraffic.Bytes;
        }
        if(version>=12)s.Town.DaylightLocation=Range(b,ref p,version>=13?0:1,DaylightLocations.Count);
        if(version>=13){Range(b,ref p,Jobs.TownJobState.EncodedWordCount,Jobs.TownJobState.EncodedWordCount);var jobs=new int[Jobs.TownJobState.EncodedWordCount];for(int i=0;i<jobs.Length;i++)jobs[i]=Get(b,ref p);s.Town.JobState.Restore(jobs);}
        // The two schema-14 releases used different catalog versions. Keep
        // both readable; schema 15 stores snow and jobs in separate sections.
        if((version==14&&catalogVersion==1)||version>=15){
            int length=Range(b,ref p,version>=15?0:SnowWear.MinCheckpointWords,SnowWear.MaxCheckpointWords);
            if(length!=0){
                if(length<SnowWear.MinCheckpointWords||length>(b.Length-p)/4)throw new InvalidOperationException("Truncated snow checkpoint");
                var snow=new int[length];for(int i=0;i<length;i++)snow[i]=Get(b,ref p);
                s.Town.Traffic.Snow.RestoreCheckpoint(snow);
            }
        }
        if((version==14&&catalogVersion>=2)||version>=15){
            int jobWords=Range(b,ref p,version>=15?0:7,Jobs.OfflineJobLedger.MaximumWords);
            if(jobWords!=0){if(jobWords<7||jobWords>(b.Length-p)/4)throw new InvalidOperationException("Truncated offline job ledger");s.Town.Jobs.Decode(b,p,jobWords,snapshot?.Jobs);p+=jobWords*4;}
        }
        if(version>=16){
            int kobanX=Get(b,ref p),kobanZ=Get(b,ref p);s.Town.Koban.Restore(kobanX,kobanZ);
            int words=Range(b,ref p,4,TownLostAndFound.MaximumEncodedWords);
            if(words>(b.Length-p)/4)throw new InvalidOperationException("Truncated lost-property records");
            var recovery=new int[words];for(int i=0;i<words;i++)recovery[i]=Get(b,ref p);s.Town.LostAndFound.Decode(recovery);
            s.Town.Koban.Validate(s);
        }else s.Town.Koban.Migrate(s);
        int placedCount=s.Town.PlacedCount;s.Town.PlacedCount=0;
        for(int i=0;i<placedCount;i++){var q=s.Town.Placed[i];if(!s.CanPlace(q.Item,q.X,q.Z,q.Rotation,q.Room,q.Owner,true))throw new InvalidOperationException("Invalid placement");s.Town.PlacedCount++;}
        if(p!=b.Length)throw new InvalidOperationException("Trailing save bytes");
        if(version<5)for(int i=0;i<6;i++){s.Town.Residents[i].PosX=s.Town.Residents[i].X+.5f;s.Town.Residents[i].PosZ=s.Town.Residents[i].Z+.5f;}
        if(version<6)WaterTerrain.Migrate(s.Town);
        s.NormalizeBuildingLocations();s.CaptureProfile();s.Town.NotifyTerrainChanged();s.MigrateJobTutorial(catalogVersion);s.ActivateSavedVisitor();
        return s;
    }
    internal static void UpgradeShop(GameState s)
    {
        if(s.BuildingTypes[7]==0)s.BuildingTypes[7]=1;
        // Keep empty (already sold) slots empty. Move essentials to the cabinet.
        for(int i=0;i<s.Town.StockCount;i++)if(s.Town.Stock[i]!=0&&s.Town.Stock[i]<10)s.Town.Stock[i]=10+(Math.Max(s.Town.Day,0)+i)%4;
    }
}
