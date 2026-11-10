internal static class SplashTimingChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var timing=new SplashTiming();
        check(timing.Alpha==0&&!timing.Complete,"splash starts black");
        timing.Advance(-1);check(timing.Alpha==0,"invalid clock delta cannot reverse the fade");
        timing.Advance(10000000);check(timing.Alpha>0&&timing.Alpha<30&&!timing.FullyVisible,"slow I/O cannot skip a fade");
        for(int i=1;i<13;i++)timing.Advance(50000);
        check(timing.Alpha==255&&timing.FullyVisible,"first fade reaches full opacity after 650ms of presentation");
        for(int i=0;i<23;i++)timing.Advance(50000);
        check(timing.Alpha==255&&timing.FullyVisible,"logo stays fully visible for its minimum hold");
        timing.Advance(50000);check(timing.Alpha==255&&!timing.FullyVisible,"hold transitions to fade out");
        timing.Advance(50000);check(timing.Alpha>0&&timing.Alpha<255&&!timing.Complete,"outgoing logo fades down");
        for(int i=0;i<12;i++)timing.Advance(50000);
        check(timing.Alpha==0&&timing.Complete,"splash completes on black before texture disposal");
        timing.Advance(long.MaxValue);check(timing.Alpha==0&&timing.Complete,"completed splash remains black");
        timing=new SplashTiming();check(timing.Alpha==0&&!timing.Complete,"second logo has an independent fade");
    }
}
