using AnimalTown;
using Npgsql;

sealed partial class GameDatabase
{
    // The caller holds the home-player row. Public NPC time can advance while
    // a card is offline; its personal calendar advances once when reserved or
    // published, then is checkpointed with the matching home card.
    async Task<bool> AdvanceHostedPersonalCalendar(NpgsqlConnection c,NpgsqlTransaction t,Guid player,GameState state)
    {
        int previousDay,previousMonth;
        await EnsureOnlineCardHead(c,t,player);
        await using(var read=Cmd(c,t,"SELECT day,month FROM hosted_personal_calendar WHERE player=$1",player))
        await using(var row=await read.ExecuteReaderAsync())
        {
            if(await row.ReadAsync()){previousDay=row.GetInt32(0);previousMonth=row.GetInt32(1);}
            else {previousDay=int.MinValue;previousMonth=-1;}
        }
        if(previousDay==int.MinValue)
        {
            await using var read=Cmd(c,t,"SELECT save_commit,save_records FROM online_card_heads WHERE player=$1",player);
            await using var row=await read.ExecuteReaderAsync();if(!await row.ReadAsync())throw new GameError(409,"town-checkpoint-required");
            var baseline=SplitSaveCodec.DecodeCommitted(row.GetFieldValue<byte[]>(0),row.GetFieldValue<byte[][]>(1));
            previousDay=baseline.Town.Day;previousMonth=baseline.Town.Month;
        }
        int day=Day,month=Month;
        bool changed=previousDay<day;
        if(changed){state.AdvanceHostedPersonalCalendar(day,month,previousMonth);state.AdvanceHostedCalendar(day,month);}
        await using(var mark=Cmd(c,t,"""
            INSERT INTO hosted_personal_calendar(player,day,month) VALUES($1,$2,$3)
            ON CONFLICT(player) DO UPDATE SET day=excluded.day,month=excluded.month
            """,player,Math.Max(day,previousDay),changed?month:previousMonth))await mark.ExecuteNonQueryAsync();
        return changed;
    }

    internal async Task AdvanceReservedCardClock(HostedAuthority authority)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();
        if(authority.Host==authority.Player)await LockTown(c,t,authority.Player);
        else await LockVisitParticipants(c,t,authority.Host,authority.Player);
        await RequireHostedActor(c,t,authority);
        await using(var active=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM online_travel WHERE player=$1 AND device=$2 AND phase=1)",authority.Player,authority.Device.Id))
            if(await active.ExecuteScalarAsync() is not true)return;
        var personal=await Load(c,t,authority.Player,false);
        if(await AdvanceHostedPersonalCalendar(c,t,authority.Player,personal.State))
        {
            await Save(c,t,authority.Player,personal.State);
            await AppendHostedEvent(c,t,authority.Host,authority.Player,2,authority.Player,InventoryPacket(personal.State,personal.Revision+1));
        }
        await t.CommitAsync();
    }
}
