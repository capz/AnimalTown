internal static class UiShapePlanChecks
{
    internal static void Run(Action<bool,string> check)
    {
        RunningMinimapChecks.Run(check);
        var plan=new UiShapePlan();plan.BeginFrame("world",-1,true);
        int hud=plan.Request(282,36,18,false);
        check(plan.Request(282,36,18,false)==hud&&plan.PendingCount==1,"shared shape misses queue once");
        check(plan.TakePending()==hud,"visible HUD request reaches preparation");plan.Complete(hud);
        plan.BeginFrame("world",-1,true);
        check(!plan.HasWork&&plan.Request(282,36,18,false)==hud&&!plan.HasWork,"warm HUD frames require no preparation pass");
        plan.BeginFrame("menu",0,true);plan.Request(338,182,30,true);
        plan.BeginFrame("world",-1,true);
        check(!plan.HasWork&&plan.TakePending()==-1,"closing a cold menu cancels queued geometry before building");
        check(plan.Request(282,36,18,false)==hud&&!plan.HasWork,"closing a menu retains shared resident HUD geometry");
        plan.BeginFrame("menu",0,true);int backpack=plan.Request(338,182,30,true);
        check(plan.TakePending()==backpack,"reopened menu requests its visible geometry again");plan.Complete(backpack);
        plan.BeginFrame("menu",1,true);plan.Request(550,350,30,true);
        plan.BeginFrame("menu",0,true);
        check(!plan.HasWork&&plan.Request(338,182,30,true)==backpack,"tab changes cancel cold work but preserve resident shapes");
        plan.Request(168,141,30,true);plan.BeginFrame("menu",0,false);
        check(!plan.HasWork&&plan.TakePending()==-1,"disabling batches cancels pending native creation");

        var collisions=new UiShapePlan();collisions.BeginFrame("menu",0,true);var ids=new int[64];
        for(int i=0;i<64;i++)ids[i]=collisions.Request(10+64*i,40,10,false);
        check(collisions.PendingCount==64,"colliding shape keys retain the bounded full capacity");
        for(int n=collisions.TakePending();n>=0;n=collisions.TakePending())collisions.Complete(n);
        check(collisions.Request(9999,40,10,false)==-1&&collisions.TakeOverflow()&&!collisions.TakeOverflow(),"full cache defers reclamation without growing or corrupting work");
        int released=ids[32];collisions.Remove(released);
        for(int i=0;i<64;i++)if(i!=32)check(collisions.Request(10+64*i,40,10,false)==ids[i],"hash deletion preserves colliding resident lookups");
        check(collisions.Request(10+64*64,40,10,false)==released&&collisions.PendingCount==1,"a deleted hash slot can be reused for a distinct shape");
        collisions.BeginFrame("world",-1,true);
        check(!collisions.HasWork,"cancellation also clears overflow/reused pending slots");

        var fractional=new UiShapePlan();fractional.BeginFrame("world",-1,true);
        int a=fractional.Request(100.25f,40,10,false),b=fractional.Request(100.26f,40,10,false);
        check(a!=b&&fractional.Request(100.25f,40,10,false)==a,"fractional hash collisions retain exact geometric keys");
        for(int n=fractional.TakePending();n>=0;n=fractional.TakePending())fractional.Complete(n);
        // Repeated cold open/close cycles exercise tombstones without native builds.
        for(int i=0;i<256;i++){
            fractional.BeginFrame("menu",i,true);fractional.Request(200+i,60,20,true);
            fractional.BeginFrame("world",-1,true);
            check(!fractional.HasWork&&fractional.Request(100.25f,40,10,false)==a,"rapid view changes never leave dormant build work or lose warm shapes");
        }
    }
}
