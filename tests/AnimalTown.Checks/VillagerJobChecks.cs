using AnimalTown;
using AnimalTown.Jobs;

internal static class VillagerJobChecks
{
    private sealed class Draws : IJobRandom
    {
        private readonly int[] values;
        internal readonly List<int> Bounds=new();
        internal int Used;
        internal Draws(params int[] values){this.values=values;}
        public int NextInt(int maximum)
        {
            Bounds.Add(maximum);
            if(Used>=values.Length)throw new Exception("Unexpected job RNG draw");
            int n=values[Used++];if(n<0||n>=maximum)throw new Exception("Fake job RNG draw outside bound");return n;
        }
    }
    private static bool Fails(Action action){try{action();return false;}catch(ArgumentException){return true;}catch(InvalidOperationException){return true;}}
    internal static void Run(Action<bool,string> check)
    {
        DefinitionsAndDraws(check);Money(check);Availability(check);Clock(check);
        Console.WriteLine("Villager job rules, exhaustive probability draws, RNG, money, cooldown and live fixed-step clock checks passed");
    }
    private static void DefinitionsAndDraws(Action<bool,string> check)
    {
        var rng=new JobRandom(0);
        foreach(uint value in new uint[]{1013904223,1196435762,3519870697,2868466484,1649599747}){
            int draw=rng.NextInt(1);check(draw==0&&rng.State==value,"zero-seed job LCG matches published deterministic vector");
        }
        rng=new JobRandom(653637408);check(rng.NextInt(int.MaxValue)==1012239698&&rng.State==1012239698,"upper-tail rejection consumes the rejected LCG value rather than modulo-biasing it");
        uint before=rng.State;check(Fails(()=>rng.NextInt(0))&&Fails(()=>rng.NextInt(-1))&&rng.State==before,"invalid RNG bounds leave the stream unchanged");
        var streams=new TownJobState(-1);uint offers=streams.Offers.State,house=streams.HouseCache.State;
        streams.Rewards.NextInt(100);check(streams.Offers.State==offers&&streams.HouseCache.State==house,"reward draws cannot perturb offer or house-cache streams");
        var gate=new Draws(0);check(!JobSelection.RollTemplate(gate,out _)&&gate.Used==1&&gate.Bounds.SequenceEqual(new[]{4}),"one of four gate outcomes produces no job without another draw");
        for(int pass=1;pass<=3;pass++)for(int family=0;family<3;family++){
            int count=family==0?4:family==1?1:7;
            for(int subtype=0;subtype<count;subtype++){
                var draws=family==1?new Draws(pass,family):new Draws(pass,family,subtype);
                check(JobSelection.RollTemplate(draws,out var template),"every successful gate outcome selects a single template");
                var definition=JobRulesV1.Definition(template);
                check((int)definition.Family==family&&(family==1?(int)template==4:(int)template==(family==0?0:5)+subtype),"family and subtype draws are equal-weight and unfiltered");
                check(draws.Bounds.SequenceEqual(family==1?new[]{4,3}:new[]{4,3,count}),"generation consumes no eligibility retries or reward draws");
            }
        }
        int[][] expected={
            new[]{40,0,0,0,0,30,30},new[]{40,0,0,10,10,40,0},new[]{20,0,0,20,20,40,0},new[]{40,0,40,10,10,0,0},
            new[]{0,0,0,0,0,0,0},new[]{0,0,0,30,30,40,0},new[]{40,0,0,30,30,0,0},new[]{60,0,0,20,20,0,0},
            new[]{60,0,0,20,20,0,0},new[]{80,0,0,10,10,0,0},new[]{80,0,0,10,10,0,0},new[]{0,0,0,0,0,0,0}
        };
        int[] days={2,2,2,2,2,1,1,1,3,3,3,2},bases={200,1000,1000,0,0,500,0,0,0,0,0,0};
        for(int t=0;t<JobRulesV1.TemplateCount;t++){
            var template=(JobTemplate)t;var d=JobRulesV1.Definition(template);
            check(d.Template==template&&d.DeadlineDays==days[t]&&d.ParcelOnAcceptance==(t<4)&&d.RequiresTravel==(t==1||t==2),"all twelve definitions retain their deadline, parcel and travel policy");
            check(d.Season==(t==7?JobSeason.Snowman:t==8?JobSeason.Flowers:t==10?JobSeason.Insects:JobSeason.AllYear),"seasonal definitions are explicit");
            if(t==4||t==11)continue;
            var actual=new int[7];for(int draw=0;draw<100;draw++){
                var fake=new Draws(draw);actual[(int)JobSelection.RollReward(fake,template)]++;
                check(fake.Used==1&&fake.Bounds[0]==100,"ordinary category consumes exactly one percentile draw");
            }
            check(actual.SequenceEqual(expected[t])&&JobRulesV1.MoneyBase(template)==bases[t],"enumerating all 100 outcomes reproduces the exact template reward table");
        }
        int[][] chains={new[]{0,75,25,0,0,0,0},new[]{25,25,25,0,0,25,0},new[]{50,0,25,0,0,25,0},new[]{65,0,0,5,5,25,0}};
        for(int borrowers=1;borrowers<=4;borrowers++){
            var actual=new int[7];for(int draw=0;draw<100;draw++)actual[(int)JobSelection.RollReward(new Draws(draw),JobTemplate.RetrievalBorrowed,borrowers)]++;
            check(actual.SequenceEqual(chains[borrowers-1]),"retrieval category probabilities depend on borrowers rather than final owner");
        }
        var noDraw=new Draws();check(Fails(()=>JobSelection.RollReward(noDraw,JobTemplate.FavorLetter))&&Fails(()=>JobSelection.RollReward(noDraw,JobTemplate.RetrievalBorrowed,0))&&noDraw.Used==0,"letters and invalid chains cannot consume ordinary reward RNG");
        check(Fails(()=>JobRulesV1.Definition((JobTemplate)12))&&Fails(()=>JobRulesV1.Personality((JobPersonality)6)),"unknown rules IDs fail visibly");
    }
    private static void Money(Action<bool,string> check)
    {
        int[] powers={int.MinValue,0,1,99,100,199,200,299,300,399,400,499,500,599,600,601,1000,33333,33334,int.MaxValue};
        int[] rates={0,0,0,29,30,99,100,367,370,498,500,599,600,659,660,660,661,699,700,700};
        for(int i=0;i<powers.Length;i++)check(JobSelection.MoneyRate(powers[i])==rates[i],"MoneyPower uses floor arithmetic at every piecewise boundary and cannot overflow");
        check(JobSelection.RollMoney(new Draws(0),200)==180&&JobSelection.RollMoney(new Draws(2000),200)==220,"money jitter includes both 90 and 110 percent endpoints");
        check(JobSelection.RollMoney(new Draws(2000),1000,600)==8360,"money uses jitter before final integer division");
        var noDraw=new Draws();check(Fails(()=>JobSelection.RollMoney(noDraw,-1))&&Fails(()=>JobSelection.RollMoney(noDraw,int.MaxValue,int.MaxValue))&&noDraw.Used==0,"invalid or overflowing rewards do not advance RNG");
    }
    private static void Availability(Action<bool,string> check)
    {
        int[] ticks={4000,3000,4000,4000,5000,5000},mild={12,10,12,10,9,9},refusal={15,13,15,13,12,12};
        for(int personality=0;personality<6;personality++){
            var town=new TownJobState(1);var npc=town.Resident(personality);
            var rule=JobRulesV1.Personality((JobPersonality)personality);
            check(rule.CooldownTicks==ticks[personality]&&rule.MildAnnoyanceCount==mild[personality]&&rule.RefusalCount==refusal[personality],"exact personality cooldown and patience configuration");
            check(npc.StartNoJobCooldown()&&!npc.FreshGenerationEnabled,"fresh failed generation starts and locks the timer once");
            town.ObserveOutdoorAcre(0,0);town.Advance(300);
            check(!npc.StartNoJobCooldown()&&npc.RemainingTicks==ticks[personality]-300&&npc.AcreBudgetTicks==700,"locked generation attempts cannot restart cooldown");
            town.Advance(int.MaxValue);check(npc.RemainingTicks==ticks[personality]-1000&&npc.AcreBudgetTicks==0,"standing in one acre spends at most 1000 active ticks");
            check(!town.ObserveOutdoorAcre(0,0)&&!town.ObserveOutdoorAcre(1,1)&&npc.AcreBudgetTicks==0,"same-acre observations and first observation of another player do not refill");
            check(town.ObserveOutdoorAcre(0,1)&&npc.AcreBudgetTicks==1000,"actual stable acre transition refills progress");
            town.Advance(333);town.ObserveOutdoorAcre(0,2);town.ObserveOutdoorAcre(0,1);
            check(npc.AcreBudgetTicks==1000,"consecutive acre entries replace rather than accumulate progress budget");
            for(int acre=2;npc.RemainingTicks>0;acre++){town.ObserveOutdoorAcre(0,acre%Town.ScreenColumns);town.Advance(1000);}
            check(npc.FreshGenerationEnabled&&npc.ConversationCount==0&&npc.AcreBudgetTicks==0,"positive-to-zero cooldown transition re-enables generation and resets patience");
            for(int conversation=0;conversation<mild[personality];conversation++)check(npc.ConversationCompleted(),"completed conversation counts once");
            check(npc.FreshGenerationEnabled&&npc.RemainingTicks==ticks[personality]&&npc.AcreBudgetTicks==1000,"mild annoyance restarts timer without independently locking fresh generation");
            for(int conversation=mild[personality];conversation<refusal[personality];conversation++)npc.ConversationCompleted();
            check(!npc.CanStartConversation&&!npc.ConversationCompleted()&&npc.ConversationCount==refusal[personality],"refused starts cannot increment a saturated patience counter");
            town.Advance(20);check(npc.ConversationCount==refusal[personality],"patience resets only after timer completes");
            town.ForgetProfileLocation(0);check(town.LastOutdoorAcre(0)==-1&&!town.ObserveOutdoorAcre(0,3)&&npc.AcreBudgetTicks==980,"reused profile slot cannot inherit an acre crossing");
        }
        var s=new TownJobState(1);check(Fails(()=>s.Advance(-1))&&Fails(()=>s.ObserveOutdoorAcre(-1,0))&&Fails(()=>s.ObserveOutdoorAcre(0,999)),"clock and acre inputs are bounded");
        long allocated=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)s.Advance(2);
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"steady job-clock ticks allocate no managed memory");
    }
    private static void Clock(Action<bool,string> check)
    {
        foreach(string screen in new[]{"world","menu","item","map","dialogue","post","mail","lost","shop","store","sell","plot","daylight-location"}){
            var game=new TownGame(new GameState(931)){screen=screen};var npc=game.state.Town.JobState.Resident(0);npc.StartNoJobCooldown();
            for(int i=0;i<30;i++)game.Step(default);
            check(npc.RemainingTicks==(screen=="world"?3940:4000),screen+" produces exactly 60 eligible job ticks/second only during active play");
            check(game.tick==30,screen+" still advances the town's fixed simulation");
        }
        var loading=new TownGame(new GameState(931)){resourcesReady=false};var waiting=loading.state.Town.JobState.Resident(0);waiting.StartNoJobCooldown();
        for(int i=0;i<30;i++)loading.Step(default);check(waiting.RemainingTicks==4000,"initial loading cannot spend job cooldown");
        var crossing=new TownGame(new GameState(931)){screen="menu"};var jobs=crossing.state.Town.JobState;var resident=jobs.Resident(0);resident.StartNoJobCooldown();
        crossing.state.X=18;crossing.state.Z=38;crossing.Step(default);jobs.Advance(1000);
        crossing.state.X=21;crossing.Step(default);check(resident.AcreBudgetTicks==1000&&resident.RemainingTicks==3000,"actual outdoor crossing refills without spending menu time");
        jobs.Advance(100);crossing.state.Space=16;crossing.Step(default);crossing.state.Space=0;crossing.state.X=21;crossing.Step(default);
        check(resident.AcreBudgetTicks==900,"same-acre house entry and exit do not manufacture an acre refill");
        using(var draft=new OnboardingDraft(crossing.state,0,false)){
            crossing.session.Draft=draft;crossing.screen="world";int before=resident.RemainingTicks;
            for(int i=0;i<30;i++)crossing.Step(default);check(resident.RemainingTicks==before,"uncommitted onboarding preview does not advance jobs");
        }
    }
}
