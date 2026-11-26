using AnimalTown;
using AnimalTown.Jobs;
using AnimalTown.Online;
using Npgsql;

sealed partial class GameDatabase
{
    // Human job partitions are persisted independently. They never alias the
    // one offline card-visitor capsule or the owner's resident job partition.
    sealed record HostedJob(Guid Id, string Villager, int Profile, int Requester, int Target,
        JobTemplate Template, int Item, int Phase, int Version, long Accepted, long Due,
        int ParcelPocket, int Borrowers, int PreviousTarget, string? Reward);

    static async Task<List<HostedJob>> ReadHostedJobs(NpgsqlConnection c, NpgsqlTransaction t, Guid host, Guid player)
    {
        var jobs = new List<HostedJob>();
        await using var read = Cmd(c, t, """
            SELECT id,villager,profile,requester,target,template,item,phase,version,accepted,due,parcel_pocket,borrowers,previous_target,reward_identity
            FROM hosted_jobs WHERE host=$1 AND player=$2 AND phase<=5 ORDER BY created,id LIMIT 128
            """, host, player);
        await using var rows = await read.ExecuteReaderAsync();
        while (await rows.ReadAsync()) jobs.Add(new(rows.GetGuid(0), rows.GetString(1), rows.GetInt32(2), rows.GetInt32(3), rows.GetInt32(4),
            (JobTemplate)rows.GetInt32(5), rows.GetInt32(6), rows.GetInt32(7), rows.GetInt32(8), rows.GetInt64(9), rows.GetInt64(10),
            rows.GetInt32(11), rows.GetInt32(12), rows.GetInt32(13), rows.IsDBNull(14) ? null : rows.GetString(14)));
        return jobs;
    }
    static async Task WriteHostedJob(NpgsqlConnection c, NpgsqlTransaction t, HostedJob job)
    {
        await using var update = Cmd(c, t, """
            UPDATE hosted_jobs SET target=$2,phase=$3,version=$4,due=$5,parcel_pocket=$6,borrowers=$7,previous_target=$8,reward_identity=$9,accepted=$10 WHERE id=$1
            """, job.Id, job.Target, job.Phase, job.Version, job.Due, job.ParcelPocket, job.Borrowers,
            job.PreviousTarget, (object?)job.Reward ?? DBNull.Value, job.Accepted);
        await update.ExecuteNonQueryAsync();
    }
    static void WriteHostedJobView(OnlinePacketWriter w, HostedJob job)
    {
        w.Id(WireId(job.Id)); w.Int(job.Requester); w.Int(job.Target); w.Int((int)job.Template); w.Int(job.Item);
        w.Int(job.Phase); w.Int(job.Version); w.Long(job.Due); w.Int(job.ParcelPocket);
    }
    static void RequireNpcReach(GameState town, HostedSeat seat, int npc)
    {
        if ((uint)npc >= Town.SavedVillagerCount || !town.NpcPresent(npc)) throw new GameError(409, "resident-unavailable");
        var target = town.Town.Residents[npc]; float x = target.PosX - seat.X, z = target.PosZ - seat.Z;
        if (target.Space != seat.Room || x * x + z * z > 6.25f) throw new GameError(409, "resident-out-of-reach");
    }
    static async Task RequireHostedConversation(NpgsqlConnection c, NpgsqlTransaction t, HostedAuthority authority, GameState town, HostedSeat seat, int npc)
    {
        RequireNpcReach(town, seat, npc);
        await using var read = Cmd(c, t, "SELECT EXISTS(SELECT 1 FROM hosted_job_conversations WHERE host=$1 AND player=$2 AND npc=$3 AND expires>clock_timestamp())", authority.Host, authority.Player, npc);
        if (await read.ExecuteScalarAsync() is not true) throw new GameError(409, "start-resident-conversation");
    }
    static async Task RequireUnboundOnlinePocket(NpgsqlConnection c, NpgsqlTransaction t, Guid player, int pocket)
    {
        await using var read = Cmd(c, t, "SELECT EXISTS(SELECT 1 FROM hosted_jobs WHERE player=$1 AND parcel_pocket=$2 AND phase BETWEEN 1 AND 3)", player, pocket);
        if (await read.ExecuteScalarAsync() is true) throw new GameError(409, "job-parcel-reserved");
    }
    static bool DifferentAcre(Villager first, Villager second) =>
        first.HomeX / Town.ScreenTileSize != second.HomeX / Town.ScreenTileSize || first.HomeZ / Town.ScreenTileSize != second.HomeZ / Town.ScreenTileSize;
    static List<int> HostedJobTargets(GameState town, int npc, int except = -1)
    {
        var candidates = new List<int>(6);
        for (int i = 0; i < Town.SavedVillagerCount; i++)
            if (i != npc && i != except && town.NpcPresent(i) && DifferentAcre(town.Town.Residents[npc], town.Town.Residents[i])) candidates.Add(i);
        return candidates;
    }

    async Task<(byte[] Reply, bool Town, bool Inventory)> HostedJobCommand(NpgsqlConnection c, NpgsqlTransaction t,
        HostedAuthority authority, HostedSeat seat, GameState town, GameState player, OnlineCommand command)
    {
        long now = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        string identity = ReviewDeviceVillager(player, authority.Device);
        var jobs = (await ReadHostedJobs(c, t, authority.Host, authority.Player)).Where(j => j.Villager == identity).ToList();
        if (command.Kind == OnlineCommandKind.Jobs)
        {
            if (command.A < 0 || command.A > 127) throw new GameError(400, "invalid-job-page");
            var page = jobs.Where(j => j.Phase == 5 || j.Due > now).Skip(command.A).Take(5).ToList();
            return (CommandResult(command, 200, extra: w => { w.Int(page.Count); foreach (var row in page) WriteHostedJobView(w, row); }), false, false);
        }
        await RequireOnlineInventory(c, t, authority.Device, authority.Host, authority.Visit);
        if (command.Kind == OnlineCommandKind.AskJob)
        {
            int npc = command.A; RequireNpcReach(town, seat, npc);
            var npcId = town.Town.Jobs.Npc(npc);
            if (!command.Entity.Same(new(npcId.Town, npcId.Serial))) throw new GameError(409, "resident-identity-changed");
            await using (var talk = Cmd(c, t, """
                INSERT INTO hosted_job_conversations(host,player,npc,expires) VALUES($1,$2,$3,clock_timestamp()+interval '2 minutes')
                ON CONFLICT(host,player) DO UPDATE SET npc=excluded.npc,expires=excluded.expires
                """, authority.Host, authority.Player, npc)) await talk.ExecuteNonQueryAsync();
            var existing = jobs.FirstOrDefault(j => (j.Requester == npc || j.Target == npc && j.Phase is 1 or 2) && (j.Phase == 5 || j.Due > now));
            if (existing is not null)
            {
                bool restored = false;
                if ((existing.Phase == 2 || existing.Phase == 1 && JobRulesV1.Definition(existing.Template).ParcelOnAcceptance) && existing.ParcelPocket < 0)
                {
                    int pocket = player.FreeSlot(); if (pocket < 0) throw new GameError(409, "visitor-pockets-full");
                    player.Pockets[pocket] = JobContent.Parcel; existing = existing with { ParcelPocket = pocket, Version = checked(existing.Version + 1) };
                    await WriteHostedJob(c, t, existing); restored = true;
                }
                return (CommandResult(command, 200, existing.Version, extra: w => WriteHostedJobView(w, existing)), false, restored);
            }
            var available = town.Town.JobState.Resident(npc);
            if (!available.CanStartConversation || !available.FreshGenerationEnabled) return (CommandResult(command, 204), false, false);
            bool rolled = JobSelection.RollTemplate(town.Town.JobState.Offers, out var template);
            available.ConversationCompleted();
            if (!rolled || !OnlineJobPolicy.CanOffer(town, player, npc, template, now))
            { available.StartNoJobCooldown(); return (CommandResult(command, 204), true, false); }
            bool favor = JobRulesV1.Definition(template).Family == JobFamily.Favor;
            if (favor && town.Town.Jobs.Job(OfflineJobLedger.SharedStart + npc).Occupying) return (CommandResult(command, 204), true, false);
            int item = template == JobTemplate.DeliveryClothing ? JobContent.PickCategory(town.Town.JobState.Offers, JobRewardCategory.Clothing, town.Town.Jobs.Outfit(npc)) :
                template is JobTemplate.DeliveryProperty or JobTemplate.RetrievalBorrowed ? JobContent.PickTagged(town.Town.JobState.Offers, ItemFlags.Property) :
                template == JobTemplate.FavorFruit ? Enumerable.Range(0, player.Capacity).Select(i => player.Pockets[i]).First(i => i != town.Town.Jobs.NativeFruit && Catalog.Has(i, ItemFlags.Fruit)) : 0;
            var targets = favor ? new List<int>() : HostedJobTargets(town, npc);
            if (!favor && targets.Count == 0) return (CommandResult(command, 204), true, false);
            int target = favor ? -1 : targets[town.Town.JobState.Offers.NextInt(targets.Count)];
            await using (var count = Cmd(c, t, "SELECT count(*) FROM hosted_jobs WHERE host=$1 AND phase<=4", authority.Host))
                if ((long)(await count.ExecuteScalarAsync())! >= 128) throw new GameError(409, "town-jobs-full");
            await using(var count=Cmd(c,t,"SELECT count(*) FROM hosted_jobs WHERE host=$1 AND phase=0",authority.Host))
                if((long)(await count.ExecuteScalarAsync())!>=OfflineJobLedger.OfferCapacity)throw new GameError(409,"town-offers-full");
            var offer = new HostedJob(Guid.NewGuid(), identity, player.ActiveProfile, npc, target, template, item, 0, 1, now,
                now + (long)JobRulesV1.Definition(template).DeadlineDays * 86400, -1, template == JobTemplate.RetrievalBorrowed ? 1 : 0, -1, null);
            await using (var insert = Cmd(c, t, """
                INSERT INTO hosted_jobs(id,host,player,villager,profile,requester,target,template,item,phase,version,accepted,due,borrowers)
                VALUES($1,$2,$3,$4,$5,$6,$7,$8,$9,0,1,$10,$11,$12)
                """, offer.Id, authority.Host, authority.Player, identity, offer.Profile, npc, target, (int)template, item, now, offer.Due, offer.Borrowers)) await insert.ExecuteNonQueryAsync();
            return (CommandResult(command, 200, 1, extra: w => WriteHostedJobView(w, offer)), true, false);
        }
        var job = jobs.FirstOrDefault(j => j.Id == HostId(command.Entity)) ?? throw new GameError(404, "participant-job-not-found");
        if (!OnlineJobPolicy.Supported(job.Template)) throw new GameError(403, "unsupported-online-job");
        if (job.Version != command.Revision) throw new GameError(409, "job-changed");
        if (job.Phase != 5 && now > job.Due) throw new GameError(409, "job-expired");
        if (command.Kind == OnlineCommandKind.AcceptJob)
        {
            await RequireHostedConversation(c, t, authority, town, seat, job.Requester);
            if (job.Phase != 0 || !OnlineJobPolicy.CanOffer(town, player, job.Requester, job.Template, now)) throw new GameError(409, "job-unavailable");
            var family = JobRulesV1.Definition(job.Template).Family;
            if(job.Template==JobTemplate.FavorFruit&&player.Find(job.Item)<0)throw new GameError(409,"job-supply-required");
            int limit = family == JobFamily.Delivery ? JobRulesV1.DeliveryRecordsPerPlayer : family == JobFamily.Retrieval ? JobRulesV1.RetrievalRecordsPerPlayer : 6;
            int occupied = jobs.Count(j => j.Phase is >= 1 and <= 4 && JobRulesV1.Definition(j.Template).Family == family);
            for (int i = player.ActiveProfile * OfflineJobLedger.PersonalPerPlayer; i < (player.ActiveProfile + 1) * OfflineJobLedger.PersonalPerPlayer; i++)
            { var local = player.Town.Jobs.Job(i); if (local.Occupying && JobRulesV1.Definition(local.Template).Family == family) occupied++; }
            if (occupied >= limit) throw new GameError(409, "participant-jobs-full");
            int rewardRecords=0;
            for(int i=0;i<OfflineJobLedger.RewardCapacity;i++)if(player.Town.Jobs.Reward(i).State!=JobRewardState.Empty)rewardRecords++;
            await using(var reserved=Cmd(c,t,"SELECT count(*) FROM hosted_jobs WHERE player=$1 AND villager=$2 AND phase BETWEEN 1 AND 3 AND reward_identity IS NULL",authority.Player,identity))
                if(rewardRecords+(long)(await reserved.ExecuteScalarAsync())!>=OfflineJobLedger.RewardCapacity)throw new GameError(409,"home-rewards-full");
            if (family == JobFamily.Favor)
            {
                if (town.Town.Jobs.Job(OfflineJobLedger.SharedStart + job.Requester).Occupying) throw new GameError(409, "favor-already-claimed");
                await using var claimed = Cmd(c, t, "SELECT EXISTS(SELECT 1 FROM hosted_jobs WHERE host=$1 AND template>=5 AND phase BETWEEN 1 AND 4 AND (requester=$2 OR template=$3))", authority.Host, job.Requester, (int)job.Template);
                if (await claimed.ExecuteScalarAsync() is true) throw new GameError(409, "favor-already-claimed");
            }
            int pocket = -1;
            if (JobRulesV1.Definition(job.Template).ParcelOnAcceptance)
            { pocket = player.FreeSlot(); if (pocket < 0) throw new GameError(409, "visitor-pockets-full"); player.Pockets[pocket] = JobContent.Parcel; }
            job = job with { Phase = 1, ParcelPocket = pocket, Version = checked(job.Version + 1), Accepted = now,
                Due = now + (long)JobRulesV1.Definition(job.Template).DeadlineDays * 86400 };
            await WriteHostedJob(c, t, job);
            return (CommandResult(command, 200, job.Version, extra: w => WriteHostedJobView(w, job)), false, pocket >= 0);
        }
        // Completion/abandonment share the same transaction and reward ledger;
        // their mutations are handled below rather than accepting client payoffs.
        return await FinishHostedJobCommand(c, t, authority, seat, town, player, command, job, now);
    }

    async Task<(byte[] Reply, bool Town, bool Inventory)> FinishHostedJobCommand(NpgsqlConnection c, NpgsqlTransaction t,
        HostedAuthority authority, HostedSeat seat, GameState town, GameState player, OnlineCommand command, HostedJob job, long now)
    {
        if (command.Kind == OnlineCommandKind.CollectReward)
        {
            if (job.Reward is null) throw new GameError(409, "reward-not-ready");
            var rewardId = ParseJobKey(job.Reward);
            player.ResidentJobs.EnableOnlineAdapter(now);
            var outcome = player.ResidentJobs.CollectOnlineReward(rewardId);
            if (outcome.Result == JobResult.NotAvailable) throw new GameError(409, "reward-already-collected");
            if (player.Town.Jobs.FindReward(rewardId) < 0 && job.Phase == 5)
            { job = job with { Phase = 6, Version = checked(job.Version + 1) }; await WriteHostedJob(c, t, job); }
            return (CommandResult(command, 200, job.Version, extra: w => { w.Int((int)outcome.Result); w.Int(outcome.Amount); w.Int(outcome.Item); }), false, true);
        }
        if (command.Kind == OnlineCommandKind.AbandonJob)
        {
            if (job.Phase > 2 || job.Phase > 0 && job.Template >= JobTemplate.FavorFruit) throw new GameError(409, "job-cannot-be-abandoned");
            if (job.ParcelPocket >= 0)
            {
                if (player.Pockets[job.ParcelPocket] != JobContent.Parcel || player.IsJobItem(job.ParcelPocket)) throw new GameError(409, "parcel-binding-changed");
                player.Pockets[job.ParcelPocket] = 0;
            }
            player.ResidentJobs.EnableOnlineAdapter(now);
            player.ResidentJobs.RecordOnlineFriendship(town.Town.Jobs.Npc(job.Requester), town.Town.Jobs.TownId,
                town.Town.Residents[job.Requester].Name, JobRulesV1.AbandonFriendship);
            job = job with { Phase = 6, ParcelPocket = -1, Version = checked(job.Version + 1) }; await WriteHostedJob(c, t, job);
            return (CommandResult(command, 200, job.Version), false, true);
        }
        if (command.Kind != OnlineCommandKind.CompleteJob || job.Phase is < 1 or > 2) throw new GameError(409, "job-not-actionable");
        int npc = command.A;
        await RequireHostedConversation(c, t, authority, town, seat, npc);
        if (job.Template == JobTemplate.FavorLetter) throw new GameError(409, "send-resident-letter-first");
        bool inventory = false;
        if (job.Template == JobTemplate.RetrievalBorrowed && job.Phase == 1)
        {
            if (npc != job.Target) throw new GameError(409, "wrong-job-resident");
            var candidates = HostedJobTargets(town, npc, job.Requester).Where(i => i != job.PreviousTarget).ToList();
            bool refer = job.Borrowers < JobRulesV1.MaximumBorrowers && candidates.Count > 0 &&
                town.Town.JobState.Offers.NextInt(JobRulesV1.ContinueDenominator) == JobRulesV1.ContinueOutcome;
            if (refer)
                job = job with { PreviousTarget = npc, Target = candidates[town.Town.JobState.Offers.NextInt(candidates.Count)],
                    Borrowers = job.Borrowers + 1, Version = checked(job.Version + 1), Due = now + JobRulesV1.RetrievalStageDays * 86400L };
            else
            {
                int pocket = player.FreeSlot(); if (pocket < 0) throw new GameError(409, "visitor-pockets-full");
                player.Pockets[pocket] = JobContent.Parcel; inventory = true;
                job = job with { Phase = 2, ParcelPocket = pocket, Version = checked(job.Version + 1), Due = now + JobRulesV1.RetrievalStageDays * 86400L };
            }
            await WriteHostedJob(c, t, job);
            return (CommandResult(command, 200, job.Version, extra: w => WriteHostedJobView(w, job)), true, inventory);
        }
        int recipient = job.Template is JobTemplate.DeliveryClothing or JobTemplate.DeliveryProperty ? job.Target : job.Requester;
        if (npc != recipient) throw new GameError(409, "wrong-job-resident");
        if (job.Template == JobTemplate.FavorFruit)
        {
            int pocket = command.B;
            if ((uint)pocket >= player.Capacity || player.IsJobItem(pocket) || player.Pockets[pocket] != job.Item) throw new GameError(409, "job-supply-required");
            await RequireUnboundOnlinePocket(c, t, authority.Player, pocket); player.Pockets[pocket] = 0;
        }
        else
        {
            int pocket = job.ParcelPocket;
            if ((uint)pocket >= player.Capacity || player.Pockets[pocket] != JobContent.Parcel || player.IsJobItem(pocket)) throw new GameError(409, "parcel-binding-changed");
            player.Pockets[pocket] = 0; job = job with { ParcelPocket = -1 };
        }
        int oldOutfit = town.Town.Jobs.Outfit(npc);
        if (job.Template == JobTemplate.DeliveryClothing)
        {
            town.ResidentJobs.EnableOnlineAdapter(now);
            town.ResidentJobs.ApplyOnlineOutfit(npc, job.Item);
        }
        job = await AwardHostedJob(c, t, authority.Host, authority.Player, town, player, job, npc, oldOutfit, now);
        return (CommandResult(command, 200, job.Version, extra: w => WriteHostedJobView(w, job)), true, true);
    }

    static JobIdentity ParseJobKey(string text)
    {
        if (text.Length != 33 || text[16] != '/' || !ulong.TryParse(text.AsSpan(0, 16), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out ulong town) || !ulong.TryParse(text.AsSpan(17, 16), System.Globalization.NumberStyles.HexNumber,
            System.Globalization.CultureInfo.InvariantCulture, out ulong serial) || town == 0 || serial == 0) throw new GameError(409, "job-identity-changed");
        return new(town, serial);
    }

    static async Task<HostedJob> AwardHostedJob(NpgsqlConnection c, NpgsqlTransaction t, Guid host, Guid owner,
        GameState town, GameState player, HostedJob job, int giver, int oldOutfit, long now, int letterRank = -1)
    {
        if (job.Reward is not null) return job;
        var random = town.Town.JobState.Rewards;
        if(job.Template==JobTemplate.FavorLetter&&letterRank<0)throw new GameError(409,"letter-not-delivered");
        bool letter=job.Template==JobTemplate.FavorLetter;
        var category = letter ? JobRewardCategory.Stationery : JobSelection.RollReward(random, job.Template, job.Borrowers);
        int currency = category == JobRewardCategory.Currency ? JobSelection.RollMoney(random, JobRulesV1.MoneyBase(job.Template, job.Borrowers)) : 0;
        int item = letter || category == JobRewardCategory.Currency ? 0 : category == JobRewardCategory.WornClothing && JobContent.Matches(oldOutfit, JobRewardCategory.Clothing) ? oldOutfit :
            JobContent.PickCategory(random, category == JobRewardCategory.WornClothing ? JobRewardCategory.Clothing : category, category == JobRewardCategory.WornClothing ? player.Shirt : 0);
        if(job.Template==JobTemplate.FavorLetter)
        { town.ResidentJobs.EnableOnlineAdapter(now); item=town.ResidentJobs.OnlineLetterPresent(letterRank); }
        if (currency == 0 && item == 0)
        {
            if(job.Template!=JobTemplate.FavorLetter)throw new GameError(409,"reward-content-unavailable");
            job=job with {Phase=4,Version=checked(job.Version+1),Due=checked(job.Due+JobRulesV1.RetentionDays*86400L)};
            await WriteHostedJob(c,t,job);return job;
        }
        player.ResidentJobs.EnableOnlineAdapter(now);
        var npc = town.Town.Jobs.Npc(giver); string name = town.Town.Residents[giver].Name;
        var sourceJob=await NextHostedIdentity(c,t);var sourceReceipt=await NextHostedIdentity(c,t);
        var reward = player.ResidentJobs.RecordOnlineReward(npc, town.Town.Jobs.TownId, name, item, currency, now,sourceJob,sourceReceipt);
        if(!letter)player.ResidentJobs.RecordOnlineFriendship(npc, town.Town.Jobs.TownId, name, JobRulesV1.SuccessFriendship);
        await using(var online=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM online_travel WHERE player=$1 AND profile=$2 AND phase=1)",owner,job.Profile))
            if(await online.ExecuteScalarAsync() is true)player.ResidentJobs.CollectOnlineReward(reward);
        bool owed = player.Town.Jobs.FindReward(reward) >= 0;
        job = job with { Phase = job.Template >= JobTemplate.FavorFruit ? 4 : owed ? 5 : 6, Reward = JobKey(reward),
            ParcelPocket = -1, Version = checked(job.Version + 1),
            Due = job.Template >= JobTemplate.FavorFruit ? checked(job.Due + JobRulesV1.RetentionDays * 86400L) : job.Due };
        await WriteHostedJob(c, t, job); return job;
    }

    static async Task<bool> SuspendHostedJobParcels(NpgsqlConnection c,NpgsqlTransaction t,Guid player,int profile,GameState home)
    {
        var parcels=new List<(Guid Id,int Pocket)>();
        await using(var read=Cmd(c,t,"SELECT id,parcel_pocket FROM hosted_jobs WHERE player=$1 AND profile=$2 AND phase BETWEEN 1 AND 3 AND parcel_pocket>=0",player,profile))
        await using(var rows=await read.ExecuteReaderAsync())while(await rows.ReadAsync())parcels.Add((rows.GetGuid(0),rows.GetInt32(1)));
        foreach(var parcel in parcels)
        {
            if(home.Pockets[parcel.Pocket]!=JobContent.Parcel||home.IsJobItem(parcel.Pocket))throw new GameError(409,"parcel-binding-changed");
            home.Pockets[parcel.Pocket]=0;
            await using var update=Cmd(c,t,"UPDATE hosted_jobs SET parcel_pocket=-1,version=version+1 WHERE id=$1",parcel.Id);await update.ExecuteNonQueryAsync();
        }
        return parcels.Count>0;
    }
}
