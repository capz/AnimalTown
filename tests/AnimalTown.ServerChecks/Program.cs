using AnimalTown;
using AnimalTown.Jobs;
using Npgsql;
using System.Security.Cryptography;
using System.Text.Json;

var connection=Environment.GetEnvironmentVariable("ANIMALTOWN_TEST_CONNECTION")??throw new Exception("Test database required");
await using var c=new NpgsqlConnection(connection);await c.OpenAsync();
var id=Guid.Parse("a8f3ab2e-f33d-4830-b01e-ef9a4fe25a32");
var original=new GameState(7319){Name="LegacySplit",Sequence=11,Wallet=1234,Palette=4,Savings=56789};original.Town.Residents[0].Name="Legacyfox";original.Town.Paths.Clear();
if(args[0]=="seed-legacy"){
    // Reproduce the pre-split schema, before the server runs its migration.
    await using(var cmd=new NpgsqlCommand("CREATE TABLE IF NOT EXISTS players (id uuid PRIMARY KEY,name varchar(16) NOT NULL UNIQUE,salt bytea NOT NULL,password bytea NOT NULL,state bytea NOT NULL,revision bigint NOT NULL DEFAULT 0,chat_restricted boolean NOT NULL DEFAULT false)",c))await cmd.ExecuteNonQueryAsync();
    byte[] salt=RandomNumberGenerator.GetBytes(16);byte[] password=Rfc2898DeriveBytes.Pbkdf2("test-password-long",salt,210000,HashAlgorithmName.SHA256,32);
    await using var insert=new NpgsqlCommand("INSERT INTO players(id,name,salt,password,state,revision) VALUES($1,$2,$3,$4,$5,17)",c);
    insert.Parameters.AddWithValue(id);insert.Parameters.AddWithValue(original.Name);insert.Parameters.AddWithValue(salt);insert.Parameters.AddWithValue(password);insert.Parameters.AddWithValue(SaveCodec.EncodeLegacy(original));await insert.ExecuteNonQueryAsync();
}else if(args[0]=="verify-calendar"){
    var current=await FixtureState(Guid.Parse(args[1]));var now=DateTime.UtcNow;
    if(current.Town.Month!=now.Year*12+now.Month-1)throw new Exception("Backend calendar must use the card's zero-based month, without advancing its season");
    Console.WriteLine("Backend/card calendar month agrees with UTC; hosted towns cannot enter winter one month early.");
}else if(args[0]=="write-initial-save"){
    int sequence=args.Length>2?int.Parse(args[2]):11;
    int wallet=args.Length>3?int.Parse(args[3]):4356;
    var town=new GameState(1859){Name="CardVillager",Sequence=sequence,Wallet=wallet,Savings=9021};
    ulong townId=args.Length>4?Convert.ToUInt64(args[4],16):0x435244544F574E01UL;
    town.ResidentJobs.Enable(townId,DateTimeOffset.UtcNow.ToUnixTimeSeconds());
    var records=SplitSaveCodec.Encode(town);
    using var output=new BinaryWriter(File.Create(args[1]));
    void WriteRecord(byte[] record){output.Write(record.Length);output.Write(record);}
    WriteRecord(SplitSaveCodec.EncodeCommit(town,records));
    foreach(var record in records)WriteRecord(record);
}else if(args[0]=="seed-placement-review"){
    var host=Guid.Parse(args[1]);var guest=Guid.Parse(args[2]);var town=await FixtureState(host);
    if(!town.PlaceHouse(12,22,false))throw new Exception("Review fixture needs a home");
    int room=BuildingWorld.PlayerRoom(town.ActiveProfile);
    if(!town.CanPlace(10,3,0,0,room,0) || !town.CanPlace(11,1,0,1,room,0))
        throw new Exception("Review fixture needs valid placements");
    await WriteFixture(host,town);
    Guid keep=Guid.NewGuid(),koban=Guid.NewGuid();
    await using var insert=new NpgsqlCommand("""
        INSERT INTO guest_placements(host,id,guest,villager,item,x,z,rotation,room,owner_slot,version)
        VALUES($1,$2,$3,'test-villager',10,3,0,0,$4,0,3),
              ($1,$5,$3,'test-villager',11,1,0,1,$4,0,1)
        """,c);
    foreach(var value in new object[]{host,keep,guest,room,koban})insert.Parameters.AddWithValue(value);
    await insert.ExecuteNonQueryAsync();
    Console.WriteLine(JsonSerializer.Serialize(new{keep,koban,room}));
}else if(args[0]=="expire-placement-review"){
    await using var expire=new NpgsqlCommand("UPDATE town_review_leases SET expires=clock_timestamp()-interval '1 second' WHERE host=$1",c);
    expire.Parameters.AddWithValue(Guid.Parse(args[1]));
    if(await expire.ExecuteNonQueryAsync()!=1)throw new Exception("Review fixture missing lease");
}else if(args[0]=="seed-shop"){
    for(int actor=1;actor<=2;actor++){
        Guid player=Guid.Parse(args[actor]);byte[][] parts=new byte[7][];
        await using(var read=new NpgsqlCommand("SELECT town_data,house_positions,shop_states,player_villager,player_villager2,player_villager3,player_villager4 FROM players WHERE id=$1",c)){
            read.Parameters.AddWithValue(player);await using var r=await read.ExecuteReaderAsync();if(!await r.ReadAsync())throw new Exception("Shop test actor missing");for(int i=0;i<7;i++)parts[i]=r.GetFieldValue<byte[]>(i);
        }
        var state=SplitSaveCodec.Decode(parts);state.Wallet=10000;Array.Clear(state.Pockets);if(actor==1)state.Pockets[0]=10;state.Sequence++;
        parts=SplitSaveCodec.Encode(state);
        await using var write=new NpgsqlCommand("UPDATE players SET town_data=$2,house_positions=$3,shop_states=$4,player_villager=$5,player_villager2=$6,player_villager3=$7,player_villager4=$8,revision=revision+1 WHERE id=$1",c);
        write.Parameters.AddWithValue(player);foreach(var part in parts)write.Parameters.AddWithValue(part);await write.ExecuteNonQueryAsync();
    }
}else if(args[0]=="seed-koban"){
    var host=Guid.Parse(args[1]);var guest=Guid.Parse(args[2]);
    var town=await FixtureState(host);var visitor=await FixtureState(guest);
    town.SelectProfile(1);town.Name="LocalKoban";Array.Clear(town.Pockets);
    int pocket=visitor.FreeSlot();if(pocket<0||!visitor.Receive(12))throw new Exception("No guest donation pocket");
    var cells=new List<int>();
    for(int cell=0;cell<Town.Cells&&cells.Count<3;cell++){
        int x=cell%Town.Width,z=cell/Town.Width;
        if(town.Town.At(x,z)==(int)Terrain.Grass&&!town.AnyGardenContains(x,z)&&
            town.CanWalk(x+.5f,z+.5f,0)&&town.Town.FindGround(x,z)<0)cells.Add(cell);
    }
    if(cells.Count!=3||!town.Town.AddGround(11,cells[2]%Town.Width,cells[2]/Town.Width,4,town.Town.Day))throw new Exception("No custody fixture space");
    await WriteFixture(host,town);await WriteFixture(guest,visitor);
    Console.WriteLine(JsonSerializer.Serialize(new{pocket,x=cells[0]%Town.Width,z=cells[0]/Town.Width,moveX=cells[1]%Town.Width,moveZ=cells[1]/Town.Width}));
}else if(args[0]=="fill-koban-pockets"){
    var player=Guid.Parse(args[1]);var state=await FixtureState(player);
    for(int i=0;i<state.Capacity;i++)state.Pockets[i]=1;
    await WriteFixture(player,state);
}else if(args[0]=="seed-deferred-custody"){
    var player=Guid.Parse(args[1]);var state=await FixtureState(player);
    const ulong townId=0xC0570D1001UL;long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    state.ResidentJobs.Enable(townId,now);var ledger=state.Town.Jobs;
    int slot=ledger.FreeReward();if(slot<0)throw new Exception("No deferred custody fixture slot");
    ledger.Rewards[slot]=new VillagerJobReward{
        Id=ledger.Allocate(),Job=ledger.Allocate(),Owner=new JobIdentity(townId+1,1),Giver=ledger.Npc(0),
        State=JobRewardState.Custody,Item=13,Category=JobRewardCategory.Furniture,ResolvedAt=now
    };
    await WriteFixture(player,state);
}else{
    await using(var cmd=new NpgsqlCommand("SELECT state,town_data,house_positions,shop_states,player_villager,player_villager2,player_villager3,player_villager4,revision FROM players WHERE id=$1",c)){
        cmd.Parameters.AddWithValue(id);await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync()||!r.IsDBNull(0)||r.GetInt64(8)!=17)throw new Exception("Migration lost row or changed revision");
        var restored=SplitSaveCodec.Decode(new[]{r.GetFieldValue<byte[]>(1),r.GetFieldValue<byte[]>(2),r.GetFieldValue<byte[]>(3),r.GetFieldValue<byte[]>(4),r.GetFieldValue<byte[]>(5),r.GetFieldValue<byte[]>(6),r.GetFieldValue<byte[]>(7)});
        if(!SaveCodec.Encode(original).SequenceEqual(SaveCodec.Encode(restored)))throw new Exception("Migration changed progress, terrain or identity");
    }
    await using(var cmd=new NpgsqlCommand("SELECT count(*) FROM players WHERE state IS NOT NULL OR town_data IS NULL OR house_positions IS NULL OR shop_states IS NULL OR player_villager IS NULL",c))if((long)(await cmd.ExecuteScalarAsync())! !=0)throw new Exception("Backend still stores combined/incomplete saves");
    bool rejected=false;try{await using var cmd=new NpgsqlCommand("UPDATE players SET player_villager=NULL WHERE id=$1",c);cmd.Parameters.AddWithValue(id);await cmd.ExecuteNonQueryAsync();}catch(PostgresException e)when(e.SqlState=="23514"){rejected=true;}
    if(!rejected)throw new Exception("Database accepted partial save set");
    rejected=false;try{await using var cmd=new NpgsqlCommand("UPDATE players SET player_villager3=NULL WHERE id=$1",c);cmd.Parameters.AddWithValue(id);await cmd.ExecuteNonQueryAsync();}catch(PostgresException e)when(e.SqlState=="23514"){rejected=true;}
    if(!rejected)throw new Exception("Database accepted partial villager roster");
    rejected=false;try{await using var cmd=new NpgsqlCommand("UPDATE players SET player_villager2=NULL,player_villager3=NULL,player_villager4=NULL WHERE id=$1",c);cmd.Parameters.AddWithValue(id);await cmd.ExecuteNonQueryAsync();}catch(PostgresException e)when(e.SqlState=="23514"){rejected=true;}
    if(!rejected)throw new Exception("Database accepted a schema-4 town with its extra profiles removed");
    var empty=SaveCodec.Decode(SaveCodec.Encode(original));empty.DeleteProfile(0);var emptyParts=SplitSaveCodec.Encode(empty);
    await using(var cmd=new NpgsqlCommand("UPDATE players SET town_data=$2,house_positions=$3,shop_states=$4,player_villager=$5,player_villager2=$6,player_villager3=$7,player_villager4=$8 WHERE id=$1",c)){
        cmd.Parameters.AddWithValue(id);foreach(var part in emptyParts)cmd.Parameters.AddWithValue(part);await cmd.ExecuteNonQueryAsync();
    }
    await using(var cmd=new NpgsqlCommand("SELECT town_data,house_positions,shop_states,player_villager,player_villager2,player_villager3,player_villager4 FROM players WHERE id=$1",c)){
        cmd.Parameters.AddWithValue(id);await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())throw new Exception("Empty town disappeared");
        var parts=new byte[7][];for(int i=0;i<7;i++)parts[i]=r.GetFieldValue<byte[]>(i);var restored=SplitSaveCodec.Decode(parts);
        if(restored.Profiles.Any(p=>p.Created)||!SaveCodec.Encode(empty).SequenceEqual(SaveCodec.Encode(restored)))throw new Exception("Backend resurrected a resident or changed the empty town");
    }
    Console.WriteLine("Backend split storage, exact legacy migration and last-resident deletion checks passed");
}

async Task<GameState> FixtureState(Guid player)
{
    await using var read=new NpgsqlCommand("SELECT town_data,house_positions,shop_states,player_villager,player_villager2,player_villager3,player_villager4 FROM players WHERE id=$1",c);
    read.Parameters.AddWithValue(player);await using var r=await read.ExecuteReaderAsync();
    if(!await r.ReadAsync())throw new Exception("Fixture actor missing");
    var parts=new byte[7][];for(int i=0;i<parts.Length;i++)parts[i]=r.GetFieldValue<byte[]>(i);
    return SplitSaveCodec.Decode(parts);
}

async Task WriteFixture(Guid player,GameState state)
{
    state.Sequence++;var parts=SplitSaveCodec.Encode(state);
    await using var write=new NpgsqlCommand("UPDATE players SET town_data=$2,house_positions=$3,shop_states=$4,player_villager=$5,player_villager2=$6,player_villager3=$7,player_villager4=$8,revision=revision+1 WHERE id=$1",c);
    write.Parameters.AddWithValue(player);foreach(var part in parts)write.Parameters.AddWithValue(part);
    await write.ExecuteNonQueryAsync();
}
