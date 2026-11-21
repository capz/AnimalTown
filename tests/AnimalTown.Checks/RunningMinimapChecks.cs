using AnimalTown;

internal static class RunningMinimapChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var m=new RunningMinimap();
        m.Update(0,true,true);m.Update(1_999_999,true,true);
        check(m.Opacity==0,"the minimap stays hidden before two continuous running seconds");
        m.Update(2_000_000,true,true);m.Update(2_125_000,true,true);
        check(m.Opacity==128,"the minimap fades in after two seconds of running");
        m.Update(2_250_000,true,true);
        check(m.Opacity==255,"the minimap reaches full opacity");
        m.Update(3_000_000,false,true);m.Update(6_000_000,true,true);
        check(m.Opacity==255,"resuming during the grace period keeps the minimap visible");
        m.Update(7_000_000,false,true);m.Update(10_999_999,false,true);
        check(m.Opacity==255,"the minimap remains fully visible throughout the stopping grace period");
        m.Update(11_000_000,false,true);m.Update(11_125_000,false,true);
        check(m.Opacity==128,"the minimap fades out after the four-second grace period");
        m.Update(11_250_000,false,true);
        check(m.Opacity==0,"the minimap finishes fading out");

        m.Update(12_000_000,true,true);m.Update(13_000_000,false,true);
        m.Update(14_000_000,true,true);m.Update(15_500_000,true,true);
        check(m.Opacity==0,"separate short running bursts do not accumulate");
        m.Update(16_000_000,true,true);m.Update(16_250_000,true,true);
        m.Update(17_000_000,false,false);
        check(m.Opacity==0,"leaving the outdoor HUD clears the minimap");
        m.Update(18_000_000,true,true);m.Update(19_999_999,true,true);
        check(m.Opacity==0,"returning outdoors requires a fresh streak");
        m.Update(20_000_000,true,true);m.Update(20_250_000,true,true);
        check(m.Opacity==255,"a fresh outdoor streak reveals the minimap");
        m.Update(100,true,true);
        check(m.Opacity==0,"a clock rollback clears stale visibility");

        var clock=new HudFade();
        clock.Update(0,true);clock.Update(250_000,true);
        check(clock.Opacity==255,"the clock fades in when the player is stationary");
        clock.Update(300_000,false);clock.Update(425_000,true);
        check(clock.Opacity==128,"stopping partway through the clock fade keeps the current opacity");
        clock.Update(487_500,true);
        check(clock.Opacity==191,"the clock fades back in from the current opacity");
        clock.Update(550_000,true);clock.Update(600_000,false);clock.Update(850_000,false);
        check(clock.Opacity==0,"the clock fully fades out when the player keeps running");
    }
}
