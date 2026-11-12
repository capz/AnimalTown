using AnimalTown;

internal static class SplitSaveChecks
{
    private sealed class Storage : ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Files=new();
        internal int CutAt,Mode,Writes,CorruptReadAt,Reads;
        public byte[] Read(string name)
        {
            var bytes=(byte[])Files[name].Clone();
            if(++Reads==CorruptReadAt){
                bytes[bytes.Length/2]^=1;
                uint crc=SaveCodec.Crc(bytes,0,bytes.Length-4);
                for(int i=0;i<4;i++)bytes[bytes.Length-4+i]=(byte)(crc>>(i*8));
            }
            return bytes;
        }
        public void Write(string name,byte[] bytes)
        {
            Writes++;bool cut=Writes==CutAt;
            if(cut&&Mode==0)throw new IOException("Power cut before write");
            Files[name]=cut&&Mode==1?bytes[..(bytes.Length/2)]:(byte[])bytes.Clone();
            if(cut)throw new IOException("Power cut during/after write");
        }
        internal Storage Copy(){var result=new Storage();foreach(var p in Files)result.Files[p.Key]=(byte[])p.Value.Clone();return result;}
    }
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(7319){Sequence=42,Wallet=1234,Name="Juniper",Species=3,Palette=5,Roof=2,Savings=9876543210};s.MailCount=2;s.Mail[0]=4;s.Mail[1]=-3;
        s.Town.Residents[0].Name="Savedfox";s.Town.Residents[0].Friendship=73;s.Town.Residents[0].PosX=12.125f;s.Town.Stock[0]=1;s.Town.Stock[2]=0;s.Town.Spending=777;
        var parts=SplitSaveCodec.Encode(s);var restored=SplitSaveCodec.Decode(parts);
        check(SaveCodec.Encode(s).SequenceEqual(SaveCodec.Encode(restored)),"all fields round-trip through shared records and four profiles");
        var commit=SplitSaveCodec.EncodeCommit(s,parts);check(SplitSaveCodec.DecodeCommitted(commit,parts).Wallet==1234,"commit binds complete save set");
        long allocated=GC.GetAllocatedBytesForCurrentThread();SplitSaveCodec.VerifyCommitted(commit,parts);
        check(GC.GetAllocatedBytesForCurrentThread()-allocated<16384,"final commit verification fits a small allocation budget without reconstructing a town");
        void Reject(Action operation,string reason){bool rejected=false;try{operation();}catch{rejected=true;}check(rejected,reason);}
        for(int kind=0;kind<SplitSaveCodec.PartCount;kind++){
            var broken=parts.Select(p=>(byte[])p.Clone()).ToArray();broken[kind][32]^=1;Reject(()=>SplitSaveCodec.DecodeCommitted(commit,broken),"corrupt part rejected");
            broken=parts.Select(p=>(byte[])p.Clone()).ToArray();broken[kind]=broken[kind][..^1];Reject(()=>SplitSaveCodec.Decode(broken),"truncated part rejected");
        }
        var swapped=(byte[][])parts.Clone();swapped[1]=parts[2];Reject(()=>SplitSaveCodec.Decode(swapped),"wrong record kind rejected");
        s.Sequence++;var newer=SplitSaveCodec.Encode(s);newer[0]=parts[0];Reject(()=>SplitSaveCodec.Decode(newer),"mixed generations rejected");s.Sequence--;
        var foreign=SplitSaveCodec.Encode(new GameState(123){Sequence=42});foreign[0]=parts[0];Reject(()=>SplitSaveCodec.Decode(foreign),"mixed towns rejected");
        s.Wallet++;var changed=SplitSaveCodec.Encode(s);Reject(()=>SplitSaveCodec.DecodeCommitted(commit,changed),"commit rejects valid but different same-sequence payload");
        check(parts[0].SequenceEqual(changed[0])&&parts[1].SequenceEqual(changed[1])&&parts[2].SequenceEqual(changed[2])&&!parts[3].SequenceEqual(changed[3]),"wallet resides only in player record");s.Wallet--;
        s.Town.Stock[0]=0;changed=SplitSaveCodec.Encode(s);check(parts[0].SequenceEqual(changed[0])&&parts[1].SequenceEqual(changed[1])&&!parts[2].SequenceEqual(changed[2])&&parts[3].SequenceEqual(changed[3]),"stock resides only in shop record");s.Town.Stock[0]=restored.Town.Stock[0];
        s.Town.Residents[0].HomeX++;changed=SplitSaveCodec.Encode(s);check(parts[0].SequenceEqual(changed[0])&&!parts[1].SequenceEqual(changed[1])&&parts[2].SequenceEqual(changed[2])&&parts[3].SequenceEqual(changed[3]),"house anchors reside only in house-position record");s.Town.Residents[0].HomeX--;
        s.Town.Tiles[0]=(byte)((s.Town.Tiles[0]+1)%8);changed=SplitSaveCodec.Encode(s);check(!parts[0].SequenceEqual(changed[0])&&parts[1].SequenceEqual(changed[1])&&parts[2].SequenceEqual(changed[2])&&parts[3].SequenceEqual(changed[3]),"terrain resides only in town record");s.Town.Tiles[0]=restored.Town.Tiles[0];
        var storage=new Storage();var repository=new SaveRepository(storage);s.Sequence=0;repository.Save(s);s.Wallet=2000;repository.Save(s);
        var bounded=SaveCodec.Decode(SaveCodec.Encode(s));var boundedRepository=new SaveRepository(storage.Copy());
        var tiles=bounded.Town.Tiles;var elevation=bounded.Town.Elevation;var beforeSave=SaveCodec.Encode(bounded);
        allocated=GC.GetAllocatedBytesForCurrentThread();boundedRepository.Save(bounded);
        long saveBytes=GC.GetAllocatedBytesForCurrentThread()-allocated;Console.WriteLine("Shared roster save validation allocated "+saveBytes+" bytes");
        check(saveBytes<256*1024,"full save/readback validation stays below 256 KiB with four profiles without duplicate terrain arrays or town generation");
        check(ReferenceEquals(tiles,bounded.Town.Tiles)&&ReferenceEquals(elevation,bounded.Town.Elevation),"readback preserves ownership of live terrain arrays");
        bounded.Sequence--;check(beforeSave.SequenceEqual(SaveCodec.Encode(bounded)),"successful read-only validation does not normalize or mutate live state");
        for(int invalid=0;invalid<3;invalid++){
            var disk=storage.Copy();var game=SaveCodec.Decode(SaveCodec.Encode(s));
            if(invalid==0)game.Wallet=100000;else if(invalid==1)game.Town.Residents[0].PosX=-2;else game.Town.Elevation[0]=255;
            var original=SaveCodec.Encode(game);
            Reject(()=>new SaveRepository(disk).Save(game),"snapshot readback retains full range/terrain semantic validation");
            check(original.SequenceEqual(SaveCodec.Encode(game)),"failed semantic validation leaves the complete live state unchanged");
            check(new SaveRepository(disk).Load()!.Sequence==2,"invalid snapshots preserve the previous complete generation");
        }
        for(int read=1;read<=15;read++){
            var disk=storage.Copy();disk.CorruptReadAt=read;var game=SaveCodec.Decode(SaveCodec.Encode(s));var original=SaveCodec.Encode(game);
            Reject(()=>new SaveRepository(disk).Save(game),"exact readback rejects altered bytes even with a valid recomputed CRC");
            check(original.SequenceEqual(SaveCodec.Encode(game)),"readback failure restores sequence and leaves live state unchanged");
            check(new SaveRepository(disk).Load()!.Sequence==(read>=8?3:2),"readback corruption recovers one complete generation, including an already durable commit");
        }
        for(int mode=0;mode<3;mode++)for(int cut=1;cut<=8;cut++){
            var disk=storage.Copy();disk.Mode=mode;disk.CutAt=cut;var game=SaveCodec.Decode(SaveCodec.Encode(s));game.Wallet=3000;game.Town.Stock[0]=0;
            Reject(()=>new SaveRepository(disk).Save(game),"injected interrupted write reports failure");check(game.Sequence==2,"failed write restores in-memory sequence");
            var recovered=new SaveRepository(disk).Load();bool committed=mode==2&&cut==8;
            check(recovered!=null&&recovered.Sequence==(committed?3:2)&&recovered.Wallet==(committed?3000:2000)&&recovered.Town.Stock[0]==(committed?0:s.Town.Stock[0]),"power-cut recovery uses one whole generation");
        }
        for(int kind=0;kind<8;kind++){
            var disk=storage.Copy();disk.Files.Remove(SplitSaveCodec.FileName(kind,0));var recovered=new SaveRepository(disk).Load();check(recovered!=null&&recovered.Sequence==1&&recovered.Wallet==1234,"missing record falls back to previous complete bank");
        }
        var legacy=new Storage();legacy.Files["AnimalTown1"]=SaveCodec.Encode(s);var imported=new SaveRepository(legacy).Load()!;check(imported.Sequence==2&&imported.Wallet==2000,"legacy combined save imports");new SaveRepository(legacy).Save(imported);
        check(legacy.Files.ContainsKey("AnimalTown1")&&new SaveRepository(legacy).Load()!.Sequence==3,"migration preserves original legacy file and selects split save");
        check(new SaveRepository(new Storage()).Load()==null,"empty card starts new town");
        CopyReductionChecks(check);
        Console.WriteLine("Split save records, ownership, corruption, interrupted-write fallback and legacy migration checks passed");
    }
    private static void CopyReductionChecks(Action<bool,string> check)
    {
        var state=new GameState(7319);var records=SplitSaveCodec.Encode(state);var encoded=SaveCodec.Encode(state);
        // Baseline fixtures from 3baec95: exact byte compatibility also protects
        // record lengths/checksums while writers switch to one exact allocation.
        check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(encoded))=="710357B1A658A42567D8853F171AB2C5EA56D6B3016829B8B39A8B80CAA7797F","combined save remains byte-identical to the baseline format");
        check(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(records.SelectMany(x=>x).ToArray()))=="F04A6D20B7C4C77FD50775D20A732EF25D8117D53F87BC3610B0B5F3019034F7","split records remain byte-identical to the baseline format");
        GC.KeepAlive(SplitSaveCodec.Decode(records));
        long before=GC.GetAllocatedBytesForCurrentThread();var decoded=SplitSaveCodec.Decode(records);long decodeBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        check(decodeBytes<128*1024,"split loading validates one world without another serialized/decoded copy");
        before=GC.GetAllocatedBytesForCurrentThread();var exact=SplitSaveCodec.Encode(state);long splitBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        check(splitBytes<exact.Sum(p=>p.Length)+2048,"split writers allocate the final records without temporary capacity buffers");
        before=GC.GetAllocatedBytesForCurrentThread();encoded=SaveCodec.Encode(state);long combinedBytes=GC.GetAllocatedBytesForCurrentThread()-before;
        check(combinedBytes<encoded.Length+8192,"combined writer keeps one exact output buffer and bounded profile records");
        Console.WriteLine("Save allocation seed 7319: combined "+combinedBytes+", split "+splitBytes+", decode "+decodeBytes+" bytes");GC.KeepAlive(decoded);

        int Get(byte[] b,int at)=>b[at]|b[at+1]<<8|b[at+2]<<16|b[at+3]<<24;
        void Put(byte[] b,int at,int value){for(int i=0;i<4;i++)b[at+i]=(byte)((uint)value>>(i*8));}
        void Seal(byte[] b){Put(b,24,b.Length);Put(b,b.Length-4,(int)SaveCodec.Crc(b,0,b.Length-4));}
        void Reject(int part,int at,int value,string reason){var broken=(byte[][])records.Clone();broken[part]=(byte[])broken[part].Clone();Put(broken[part],at,value);Seal(broken[part]);bool refused=false;try{SplitSaveCodec.Decode(broken);}catch{refused=true;}check(refused,reason);}
        int npc=48;for(int i=0;i<2;i++)npc+=4+Get(records[0],npc)*4;
        Reject(0,npc,Town.Width,"shared validation rejects resident grid coordinates with valid CRC");
        Reject(0,npc+4,Town.Height,"shared validation rejects resident Z coordinates with valid CRC");
        Reject(0,npc+12+Get(records[0],npc+8)*4,Town.Width*1024,"shared validation rejects resident fixed-point positions with valid CRC");
        int ground=npc;for(int i=0;i<6;i++)ground+=20+Get(records[0],ground+8)*4;
        check(Get(records[0],ground)>0,"semantic corruption fixture includes a live ground record");
        Reject(0,ground+4,0,"shared validation rejects invalid ground catalog IDs with valid CRC");
        Reject(0,ground+4+12,4,"shared validation rejects invalid loose-item owners with valid CRC");
        Reject(0,ground+4+20,5,"shared validation rejects invalid ground kinds with valid CRC");
        Reject(1,28+GameState.ProfileCount*12,0,"shared validation rejects resident home anchors with valid CRC");
        Reject(2,28,-1,"shared validation rejects negative shop spending with valid CRC");
        Reject(2,32,5,"shared validation rejects short shop stock lists with valid CRC");
        Reject(2,36,26,"shared validation rejects shop catalog IDs with valid CRC");

        var maximum=new GameState(7319);Array.Fill(maximum.Town.Tiles,(byte)Terrain.Grass);Array.Clear(maximum.Town.Elevation);maximum.Town.GroundCount=maximum.Town.PlacedCount=0;
        for(int profile=0;profile<GameState.ProfileCount;profile++){
            maximum.SelectProfile(profile);maximum.Name=new string('N',16);maximum.Catchphrase=new string('C',32);maximum.BackpackTier=2;maximum.HouseTier=5;maximum.GardenTier=4;
            maximum.MailCount=128;for(int i=0;i<128;i++)maximum.Mail[i]=i%51-25;for(int i=0;i<30;i++)maximum.Pockets[i]=i%25+1;
            check(maximum.PlaceHouse(10+profile*35,60,false),"max-size profile fixture keeps distinct valid home plots");
        }
        maximum.SelectProfile(0);
        for(int i=0;i<maximum.Town.Ground.Length;i++){var g=maximum.Town.Ground[i];g.Item=i%25+1;g.X=i%160;g.Z=110+i/160;g.Kind=i%5;g.Owner=i%4;}maximum.Town.GroundCount=maximum.Town.Ground.Length;
        for(int profile=0;profile<4&&maximum.Town.PlacedCount<maximum.Town.Placed.Length;profile++)for(int room=1;room<=4&&maximum.Town.PlacedCount<maximum.Town.Placed.Length;room++){
            int space=BuildingWorld.PlayerRoom(profile,room),size=BuildingWorld.Size(maximum,space),owner=profile==0?0:profile+6;
            for(int z=0;z<size&&maximum.Town.PlacedCount<maximum.Town.Placed.Length;z++)for(int x=0;x<size&&maximum.Town.PlacedCount<maximum.Town.Placed.Length;x++){
                if(!maximum.CanPlace(10,x,z,0,space,owner,true))continue;var q=maximum.Town.Placed[maximum.Town.PlacedCount++];q.Item=10;q.X=x;q.Z=z;q.Room=space;q.Owner=owner;
            }
        }
        check(maximum.Town.PlacedCount==maximum.Town.Placed.Length,"max-size fixture fills all furniture slots");
        var maxBytes=SaveCodec.Encode(maximum);var maxRecords=SplitSaveCodec.Encode(maximum);
        check(maxBytes.SequenceEqual(SaveCodec.Encode(SaveCodec.Decode(maxBytes)))&&maxBytes.SequenceEqual(SaveCodec.Encode(SplitSaveCodec.Decode(maxRecords))),"exact writers fit maximum profile strings/mail and all512 ground/256 furniture records");

        var legacy=new GameState(101);Array.Fill(legacy.Town.Tiles,(byte)Terrain.Grass);Array.Clear(legacy.Town.Elevation);legacy.Town.GroundCount=0;legacy.Town.Tiles[WaterTerrain.SeaStart*Town.Width]=(byte)Terrain.Water;
        for(int version=1;version<=3;version++){
            var old=SplitSaveCodec.EncodeLegacy(legacy);
            if(version<3){var player=new byte[old[3].Length-(2+9+6*7)*4];Array.Copy(old[3],player,player.Length-4);old[3]=player;}
            foreach(var part in old){Put(part,4,version);Seal(part);}
            var imported=SplitSaveCodec.Decode(old);
            check(imported.ActiveProfile==0&&imported.Name==legacy.Name&&!imported.Profiles[1].Created,"legacy split schemas retain semantic migration and active-profile defaults");
            check(imported.Town.TerrainVersion==SaveCodec.Decode(SaveCodec.Encode(imported)).Town.TerrainVersion,"legacy split import publishes the same final terrain revision as combined decoding");
            check(imported.Town.Tiles[WaterTerrain.SeaStart*Town.Width]==(byte)(version==1?Terrain.Sea:Terrain.Water),"legacy water relabeling applies only to the historical schema");
        }
    }

}
