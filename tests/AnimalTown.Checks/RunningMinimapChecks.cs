using AnimalTown;

internal static class RunningMinimapChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var m=new RunningMinimap();
        m.Update(0,true,true);m.Update(6000000,true,true);
        check(!m.Visible,"minimap needs more than six continuous running seconds");
        m.Update(6000001,true,true);check(m.Visible,"continuous running reveals minimap");
        m.Update(7000000,false,true);m.Update(10999999,false,true);
        check(m.Visible,"minimap remains throughout the four-second stopping grace");
        m.Update(11000000,false,true);check(!m.Visible,"minimap hides four seconds after the first non-running update");
        m.Update(12000000,true,true);m.Update(16000000,false,true);
        m.Update(17000000,true,true);m.Update(22000000,true,true);
        check(!m.Visible,"separate short running bursts do not accumulate");
        m.Update(23000001,true,true);check(m.Visible,"a new uninterrupted streak can reveal the map");
        m.Update(24000000,false,true);m.Update(26000000,true,true);m.Update(30000000,true,true);
        check(m.Visible,"resuming during the grace period keeps the visible map");
        m.Update(31000000,false,false);check(!m.Visible,"menus and interiors hide and reset the outdoor minimap");
        m.Update(32000000,true,true);m.Update(38000000,true,true);check(!m.Visible,"returning outdoors requires a fresh full streak");
        m.Update(38000001,true,true);check(m.Visible,"outdoor running resumes normal timing");
        m.Update(100,true,true);check(!m.Visible,"a clock rollback starts a new streak without stale visibility");
        m.Update(6000100,true,true);check(!m.Visible,"rollback streak also uses the strict six-second boundary");
        m.Update(6000101,true,true);check(m.Visible,"clock rollback recovers");
    }
}
