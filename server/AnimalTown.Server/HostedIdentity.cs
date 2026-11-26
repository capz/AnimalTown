using System.Globalization;
using System.Security.Cryptography;
using AnimalTown.Jobs;
using Npgsql;

sealed partial class GameDatabase
{
    static async Task InitializeHostedIssuer(NpgsqlConnection connection)
    {
        for(int attempt=0;attempt<8;attempt++)
        {
            string candidate=Convert.ToHexString(RandomNumberGenerator.GetBytes(8));
            await using(var create=Cmd(connection,null,"""
                INSERT INTO hosted_identity(id,issuer) SELECT 1,$1
                WHERE $1<>'0000000000000000' AND NOT EXISTS(SELECT 1 FROM initial_town_imports WHERE town_identity=$1)
                ON CONFLICT(id) DO NOTHING
                """,candidate))await create.ExecuteNonQueryAsync();
            await using var read=Cmd(connection,null,"SELECT EXISTS(SELECT 1 FROM hosted_identity WHERE id=1)");
            if(await read.ExecuteScalarAsync() is true)return;
        }
        throw new InvalidOperationException("Hosted receipt issuer unavailable");
    }
    static async Task<JobIdentity> NextHostedIdentity(NpgsqlConnection c,NpgsqlTransaction t)
    {
        await using var next=Cmd(c,t,"SELECT issuer,nextval('hosted_identity_counter') FROM hosted_identity WHERE id=1");
        await using var row=await next.ExecuteReaderAsync();if(!await row.ReadAsync())throw new GameError(503,"receipt-issuer-unavailable");
        return new(ulong.Parse(row.GetString(0),NumberStyles.HexNumber,CultureInfo.InvariantCulture),checked((ulong)row.GetInt64(1)));
    }
}
