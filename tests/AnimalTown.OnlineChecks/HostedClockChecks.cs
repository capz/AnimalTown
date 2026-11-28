using AnimalTown;
using AnimalTown.Online;
using Npgsql;

static class HostedClockChecks
{
    static Guid GuidFor(OnlineId id){var bytes=new byte[16];new OnlinePacketWriter(bytes).Id(id);return new Guid(bytes);}
    public static async Task Deliver(OnlineId letter,OnlineId job,Action<bool,string> check)
    {
        string? database=Environment.GetEnvironmentVariable("ANIMALTOWN_TEST_CONNECTION");
        if(string.IsNullOrEmpty(database))return; // a remote fixture has no clock-control access
        await using var connection=new NpgsqlConnection(database);await connection.OpenAsync();
        Guid mail=GuidFor(letter),quest=GuidFor(job);
        long now=DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        await using(var due=new NpgsqlCommand("UPDATE hosted_mail SET due=$2 WHERE id=$1 AND NOT delivered",connection))
        {due.Parameters.AddWithValue(mail);due.Parameters.AddWithValue(now);check(await due.ExecuteNonQueryAsync()==1,"Fixture advances only its scheduled mail deadline");}
        async Task<bool> Flag(string field)
        {
            await using var read=new NpgsqlCommand("SELECT "+field+" FROM hosted_mail WHERE id=$1",connection);read.Parameters.AddWithValue(mail);
            return await read.ExecuteScalarAsync() is true;
        }
        async Task Wait(string field)
        {
            for(int i=0;i<80;i++){if(await Flag(field))return;await Task.Delay(100);}
            throw new Exception("Hosted mail did not advance its "+field+" state");
        }
        await Wait("delivered");
        await using(var result=new NpgsqlCommand("SELECT phase FROM hosted_jobs WHERE id=$1",connection))
        {result.Parameters.AddWithValue(quest);check(await result.ExecuteScalarAsync() is 4,"Scheduled resident delivery completes the correct guest's letter favor");}
        // Advance a day only for this message's reply, preserving the real
        // backend clock and every other fixture/account's deadlines.
        await using(var due=new NpgsqlCommand("UPDATE hosted_mail SET due=$2 WHERE id=$1 AND delivered AND NOT replied",connection))
        {due.Parameters.AddWithValue(mail);due.Parameters.AddWithValue(now-86401);await due.ExecuteNonQueryAsync();}
        await Wait("replied");
        async Task<GameState> Home()
        {
            await using var read=new NpgsqlCommand("""
                SELECT p.town_data,p.house_positions,p.shop_states,p.player_villager,p.player_villager2,p.player_villager3,p.player_villager4
                FROM players p JOIN hosted_mail m ON m.sender=p.id WHERE m.id=$1
                """,connection);read.Parameters.AddWithValue(mail);
            await using var row=await read.ExecuteReaderAsync();if(!await row.ReadAsync())throw new Exception("Fixture home disappeared");
            var parts=new byte[7][];for(int i=0;i<parts.Length;i++)parts[i]=row.GetFieldValue<byte[]>(i);return SplitSaveCodec.Decode(parts);
        }
        var home=await Home();check(home.Name=="GuestA"&&home.MailCount==1&&!home.Pockets.Contains(Catalog.WritingPaper),"NPC reply returns to the guest home: name="+home.Name+" letters="+home.MailCount+" paper="+home.Pockets.Count(i=>i==Catalog.WritingPaper));
        check(home.Town.Jobs.Letter(home.ActiveProfile,0).Gift==0,"The online NPC reply remains text-only");
        var pockets=(int[])home.Pockets.Clone();int wallet=home.Wallet;
        await Task.Delay(1200);var retried=await Home();
        check(retried.MailCount==1&&retried.Wallet==wallet&&retried.Pockets.SequenceEqual(pockets),"Repeated scheduled processing cannot duplicate the letter, item or Bells");
    }
}
