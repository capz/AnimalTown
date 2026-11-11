using AnimalTown;
using Npgsql;
using System.Security.Cryptography;

var connection=Environment.GetEnvironmentVariable("ANIMALTOWN_TEST_CONNECTION")??throw new Exception("Test database required");
await using var c=new NpgsqlConnection(connection);await c.OpenAsync();
var id=Guid.Parse("a8f3ab2e-f33d-4830-b01e-ef9a4fe25a32");
var original=new GameState(7319){Name="LegacySplit",Sequence=11,Wallet=1234,Palette=4,Savings=56789};original.Town.Residents[0].Name="Legacyfox";
if(args[0]=="seed-legacy"){
    // Reproduce the pre-split schema, before the server runs its migration.
    await using(var cmd=new NpgsqlCommand("CREATE TABLE IF NOT EXISTS players (id uuid PRIMARY KEY,name varchar(16) NOT NULL UNIQUE,salt bytea NOT NULL,password bytea NOT NULL,state bytea NOT NULL,revision bigint NOT NULL DEFAULT 0,chat_restricted boolean NOT NULL DEFAULT false)",c))await cmd.ExecuteNonQueryAsync();
    byte[] salt=RandomNumberGenerator.GetBytes(16);byte[] password=Rfc2898DeriveBytes.Pbkdf2("test-password-long",salt,210000,HashAlgorithmName.SHA256,32);
    await using var insert=new NpgsqlCommand("INSERT INTO players(id,name,salt,password,state,revision) VALUES($1,$2,$3,$4,$5,17)",c);
    insert.Parameters.AddWithValue(id);insert.Parameters.AddWithValue(original.Name);insert.Parameters.AddWithValue(salt);insert.Parameters.AddWithValue(password);insert.Parameters.AddWithValue(SaveCodec.EncodeLegacy(original));await insert.ExecuteNonQueryAsync();
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
    Console.WriteLine("Backend split storage and exact legacy migration checks passed");
}
