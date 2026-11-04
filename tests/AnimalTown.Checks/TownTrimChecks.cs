using System.Buffers.Binary;
using AnimalTown;

internal static class TownTrimChecks
{
    internal static void Run(Action<bool,string> check)
    {
        byte[] normal=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures/town-20x20-v3.bin"));
        byte[] outskirts=File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory,"Fixtures/town-20x20-outskirts-v3.bin"));
        foreach(byte[] fixture in new[]{normal,outskirts}){
            var state=SaveCodec.Decode(fixture);byte[] oldTerrain=Terrain(fixture);
            bool same=true;for(int z=0;z<Town.Height;z++)for(int x=0;x<Town.Width;x++)same&=state.Town.Tiles[z*Town.Width+x]==oldTerrain[z*400+x];
            check(same&&state.Town.Tiles.Length==25600,"legacy terrain is cropped with its original 400-cell stride, without regenerating retained cells");
            check(state.Town.At(140,140)==(int)AnimalTown.Terrain.Camp&&state.Town.At(159,158)==(int)AnimalTown.Terrain.Bridge,"retained terrain edits survive near the new boundary");
            check(state.Sequence==42&&state.Name=="Old neighbour"&&state.Catchphrase=="same town"&&state.Wallet==12345&&state.Savings==67890&&state.Species==4&&state.Palette==7,"trimming preserves identity, progress and money");
            check(state.Capacity==30&&state.Pockets[29]==11&&state.Owned[11]==1&&state.MailCount==1&&state.Mail[0]==-12,"backpack, catalog and mail survive the trim");
            check(state.HousePlaced&&state.Town.HouseX==12&&state.Town.HouseZ==22&&state.HouseTier==5&&state.GardenTier==4,"home plot fits the retained region with upgrades intact");
            check(state.Town.PlacedCount==2&&state.Town.Placed[0].Item==10&&state.Town.Placed[0].X==15&&state.Town.Placed[0].Z==29&&state.Town.Placed[1].Room==1&&state.Town.Placed[1].X==1&&state.Town.Placed[1].Z==1,"home relocation preserves outdoor offsets and interior furniture");
            check(state.CanWalk(state.X,state.Z,0),"imported player has a safe arrival");
            int trees=0;bool bounded=true;for(int i=0;i<state.Town.GroundCount;i++){var g=state.Town.Ground[i];if(g.Kind==3)trees++;bounded&=state.Town.Inside(g.X,g.Z);}
            check(trees==128&&bounded,"only trees inside the cropped town remain and every ground record stays in bounds");
            for(int i=0;i<6;i++)check(state.Town.Inside(state.Town.Residents[i].X,state.Town.Residents[i].Z),"every imported resident stays in bounds");
            byte[] encoded=SaveCodec.Encode(state);
            check(Int(encoded,4)==5&&Int(encoded,24)==160&&Int(encoded,28)==160,"v5 save records the 160 x 160 fine-cell dimensions");
            check(encoded.SequenceEqual(SaveCodec.Encode(SaveCodec.Decode(encoded))),"trimmed v5 save is stable on repeated loading and saving");
            check(encoded.Length<fixture.Length,"cropped terrain reduces the serialized save size");
        }
        var retained=SaveCodec.Decode(normal);
        check(retained.X==18.25f&&retained.Z==38.5f,"player coordinates within the crop are unchanged");
        var moved=SaveCodec.Decode(outskirts);
        check(moved.Town.Residents[0].X==10&&moved.Town.Residents[0].Z==14,"resident outside the crop returns to its retained home area");
        int money=-1;bool fossil=false,flower=false;
        for(int i=0;i<moved.Town.GroundCount;i++){
            var g=moved.Town.Ground[i];if(g.Item==24)money=i;
            if(g.Item==16&&g.Kind==4)fossil=true;if(g.Item==2&&g.Kind==4)flower=true;
        }
        check(money>=0&&moved.Town.Ground[money].Kind==4&&moved.Town.Ground[money].Owner==2&&moved.Town.Ground[money].Day==GroundItem.PendingRecoveryDay&&fossil&&flower,"removed outskirts possessions move to Lost & Found with owner protection and pending fresh retention");
        check(!moved.ClaimLost(money,0)&&moved.ClaimLost(money,2),"recovered possessions remain claimable only by their owner during protection");
        var delayed=SaveCodec.Decode(SaveCodec.Encode(SaveCodec.Decode(outskirts)));delayed.Rollover(300,100);
        int Money()=>Array.FindIndex(delayed.Town.Ground,0,delayed.Town.GroundCount,g=>g.Item==24);
        check(Money()>=0&&delayed.Town.Ground[Money()].Day==300&&!delayed.ClaimLost(Money(),0),"calendar catch-up gives recovered possessions a fresh protected window after re-saving");
        delayed.Rollover(306,100);check(Money()>=0,"recovered possessions remain for six days after first calendar update");
        delayed.Rollover(307,100);check(Money()<0,"recovered possessions use the normal seven-day expiry after migration");
        byte[] v2=Version2(normal);var older=SaveCodec.Decode(v2);
        check(older.Capacity==20&&older.Pockets[14]==12&&older.Town.At(140,140)==(int)AnimalTown.Terrain.Camp,"v2 town cropping retains old backpack migration");
        var oldest=SaveCodec.Decode(Version1(v2));
        check(oldest.Town.At(32,26)==(int)AnimalTown.Terrain.Building&&oldest.Town.Tiles.Length==Town.Cells&&oldest.Capacity==20,"original 80 x 72 raw-terrain saves still load");
        retained.X=159.75f;retained.Z=159.5f;var edge=SaveCodec.Decode(SaveCodec.Encode(retained));
        check(edge.X==159.75f&&edge.Z==159.5f&&!edge.Town.Inside(160,159)&&!edge.Town.Inside(159,160),"fractional positions inside the last retained cell save without permitting an extra cell");
        byte[] edited=Terrain(outskirts);edited[22*400+12]=(byte)AnimalTown.Terrain.Camp;
        var alternate=SaveCodec.Decode(WithTerrain(outskirts,edited));
        check(alternate.HousePlaced&&(alternate.Town.HouseX!=12||alternate.Town.HouseZ!=22)&&alternate.Town.ValidPlot(alternate.Town.HouseX,alternate.Town.HouseZ),"relocation finds another plot when the original default is not usable");
        for(int z=0;z<160;z++)Array.Fill(edited,(byte)AnimalTown.Terrain.Water,z*400,160);
        bool noPlot=false;try{SaveCodec.Decode(WithTerrain(outskirts,edited));}catch(InvalidOperationException error){noPlot=error.Message=="Trimmed town has no valid home plot";}
        check(noPlot,"an impossible crop fails migration instead of fabricating or dropping the player's home");
        int at=TerrainAt(normal);
        Reject(normal,at,160001,"oversized legacy run count is rejected");
        Reject(normal,at+4,0,"zero-length terrain runs are rejected");
        Reject(normal,at+4,160001,"legacy terrain overrun is rejected");
        Reject(normal,at+4,8<<24|1,"invalid legacy terrain values are rejected");
        byte[] current=SaveCodec.Encode(retained);
        Reject(current,24,400,"v4 dimension mismatch is rejected");
        Reject(current,32+28*4,160*1024,"new player bounds reject coordinates outside the cropped map");
        byte[] damaged=(byte[])normal.Clone();damaged[damaged.Length-1]^=1;
        bool crc=false;try{SaveCodec.Decode(damaged);}catch(InvalidOperationException){crc=true;}
        check(crc,"corrupt legacy saves still fail CRC validation before migration");

        void Reject(byte[] source,int offset,int value,string message){byte[] b=(byte[])source.Clone();Put(b,offset,value);Seal(b);bool rejected=false;try{SaveCodec.Decode(b);}catch(InvalidOperationException){rejected=true;}check(rejected,message);}
    }
    private static int Int(byte[] b,int at)=>BinaryPrimitives.ReadInt32LittleEndian(b.AsSpan(at));
    private static void Put(byte[] b,int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(b.AsSpan(at),value);
    private static void Seal(byte[] b){Put(b,16,b.Length);Put(b,20,(int)SaveCodec.Crc(b,24,b.Length-24));}
    private static int PocketsAt(byte[] b){int p=24+34*4+(Int(b,4)>=3?4:0);for(int i=0;i<2;i++)p+=4+Int(b,p)*4;return p;}
    private static int TerrainAt(byte[] b)=>PocketsAt(b)+(Int(b,4)>=3?30:15)*4+64*4+Int(b,24+21*4)*4;
    private static byte[] Terrain(byte[] b){var cells=new byte[400*400];int at=TerrainAt(b),count=Int(b,at),cell=0;for(int i=0;i<count;i++){int run=Int(b,at+4+i*4);Array.Fill(cells,(byte)((uint)run>>24),cell,run&0xffffff);cell+=run&0xffffff;}return cells;}
    private static byte[] Version2(byte[] source){int pockets=PocketsAt(source);var bytes=new List<byte>(source);bytes.RemoveRange(pockets+15*4,15*4);bytes.RemoveRange(24+34*4,4);var b=bytes.ToArray();Put(b,4,2);Put(b,PocketsAt(b)+14*4,12);Seal(b);return b;}
    private static byte[] Version1(byte[] v2){int terrainAt=TerrainAt(v2),tail=terrainAt+4+Int(v2,terrainAt)*4;byte[] terrain=Terrain(v2);using var stream=new MemoryStream();stream.Write(v2,0,terrainAt);for(int z=0;z<72;z++)stream.Write(terrain,z*400,80);stream.Write(v2,tail,192);int count=Int(v2,tail+192),start=tail+196;var records=new List<byte>();for(int i=0;i<count;i++){int record=start+i*24;if(Int(v2,record+4)<80&&Int(v2,record+8)<72)records.AddRange(v2.AsSpan(record,24).ToArray());}byte[] size=new byte[4];Put(size,0,records.Count/24);stream.Write(size);stream.Write(records.ToArray());stream.Write(v2,start+count*24,v2.Length-start-count*24);byte[] b=stream.ToArray();Put(b,4,1);Seal(b);return b;}
    private static byte[] WithTerrain(byte[] source,byte[] cells){int at=TerrainAt(source),tail=at+4+Int(source,at)*4;using var stream=new MemoryStream();using var writer=new BinaryWriter(stream);writer.Write(source,0,at);writer.Write(0);int runs=0;for(int cell=0;cell<cells.Length;){int length=1;while(cell+length<cells.Length&&cells[cell+length]==cells[cell])length++;writer.Write(cells[cell]<<24|length);cell+=length;runs++;}writer.Write(source,tail,source.Length-tail);var result=stream.ToArray();Put(result,at,runs);Seal(result);return result;}
}
