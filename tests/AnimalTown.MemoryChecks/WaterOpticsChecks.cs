using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Timing;

internal static class WaterOpticsChecks
{
    internal static void Run(Action<bool,string> check)
    {
        const int total=4*1024*1024;
        long originalClock=Clock.Timestamp;
        bool originalFrame=GraphicsDevice.Default.IsFrameActive;
        void Observe(VisualMemoryPressure policy,int bytes=total)
            =>policy.Observe(bytes-NativeWater.Bytes,NativeWater.Failures,0,Clock.Timestamp);
        void Prepare(WaterBumpEffect effect,bool visible=true)
            =>effect.Prepare(32,255,0xffffff,visible,NativeWater.Width,NativeWater.Height);
        void Recover(VisualMemoryPressure policy)
        {
            Observe(policy);Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;Observe(policy);
            Clock.Timestamp+=VisualMemoryPressure.RecoveryGraceUs+1;Observe(policy);
        }
        try{
            GraphicsDevice.Default.IsFrameActive=false;Clock.Timestamp+=10000000;
            NativeWater.Reset();WaterTextureAssets.Reads=0;
            var memory=new VisualMemoryPressure();Observe(memory);
            var water=new WaterBumpEffect{Memory=memory};
            NativeWater.BeforeAllocation=bytes=>check(memory.ReservedBytes>=bytes,"water native allocation starts only after its peak reservation");
            Prepare(water);
            check(water.Active&&water.CaptureReady&&NativeWater.Bytes==169984,"NTSC water owns two 8KiB maps and one half-resolution RGB565 capture");
            check(memory.ReservedBytes==171008&&memory.RequiresObservation,"water completion keeps actual new ownership charged until a fresh physical sample");
            Observe(memory);check(memory.ReservedBytes==0,"fresh physical observation removes completed water debt exactly once");
            int allocations=NativeWater.Allocations;
            for(int i=0;i<30;i++)Prepare(water);
            check(NativeWater.Allocations==allocations&&memory.ReservedBytes==0&&!memory.RequiresObservation,"warm water frames neither allocate nor force allocator resampling");
            Clock.Timestamp+=100;Prepare(water,false);
            int begin=NativeWater.BeginCalls,scope=NativeWater.ScopeCalls,captures=NativeWater.CaptureCalls;
            water.Begin();water.Scope(2);water.Capture();water.Scope(0);
            check(!water.Active&&!water.CaptureReady&&NativeWater.Bytes==169984,"brief water absence retains storage without claiming drawable optics");
            check(NativeWater.BeginCalls==begin&&NativeWater.ScopeCalls==scope&&NativeWater.CaptureCalls==captures,"retained invisible optics perform no native frame/scope/copy work");
            Clock.Timestamp+=100;Prepare(water);
            check(water.CaptureReady&&NativeWater.Allocations==allocations,"a short visibility reversal reuses the capture instead of freeing and reallocating");
            Clock.Timestamp+=WaterBumpEffect.IdleGraceUs+1;Prepare(water,false);
            check(NativeWater.Bytes==0&&!water.Active,"long water absence retires immutable maps as well as capture storage");
            Observe(memory);Prepare(water);Observe(memory);
            NativeWater.Height=528;Prepare(water);
            check(water.CaptureReady&&NativeWater.Bytes==185344&&memory.ReservedBytes==169984,"a PAL-sized resize reserves the full new capture plus guard before native replacement");
            Observe(memory);allocations=NativeWater.Allocations;
            NativeWater.Width=641;Prepare(water);
            check(water.Active&&!water.CaptureReady&&NativeWater.Bytes==16384&&NativeWater.Allocations==allocations,"unsupported capture dimensions retain the native bump-only fallback without an allocation or false failure");
            check(!memory.UnderPressure,"valid bump-only fallback does not invent memory pressure");
            NativeWater.Width=640;NativeWater.Height=480;Prepare(water);Observe(memory);
            water.Suspend();check(!water.Active&&NativeWater.Bytes==16384,"save/scene suspension retires capture while leaving immutable maps reusable");
            Prepare(water);
            check(water.CaptureReady&&memory.ReservedBytes==154624,"restoring a suspended capture charges capture bytes and allocator guard without charging existing maps again");
            water.Dispose();water.Dispose();Observe(memory);
            check(NativeWater.Bytes==0&&memory.ReservedBytes==0,"water teardown is idempotent and releases all native storage and completed debt");
            allocations=NativeWater.Allocations;Prepare(water);
            check(NativeWater.Allocations==allocations,"a disposed water owner cannot reload resources");

            NativeWater.Reset();WaterTextureAssets.Reads=0;
            memory=new VisualMemoryPressure();memory.Observe(2*1024*1024,0,0,Clock.Timestamp);
            check(memory.TryReserve(1700*1024,Clock.Timestamp,true,out int essential),"an essential asynchronous owner can reserve its peak before water becomes visible");
            water=new WaterBumpEffect{Memory=memory};Prepare(water);
            check(!water.Active&&NativeWater.Allocations==0&&WaterTextureAssets.Reads==0&&memory.ReservedBytes==1700*1024,"optional optics cannot spend headroom promised to essential owners or read assets while denied");
            memory.Cancel(essential);Prepare(water);Observe(memory);
            check(water.CaptureReady,"water can restore after admission becomes available without requiring a visibility toggle");
            memory.Observe(700*1024,0,0,Clock.Timestamp);
            check(!memory.TryReserve(600*1024,Clock.Timestamp,true,out _),"an essential restore can be blocked by preexisting optional optics");
            check(water.Reclaim()&&NativeWater.Bytes==0&&!water.Active,"early residency reclamation frees optional optics even before scene drawing can run");
            memory.Observe(700*1024+169984,0,0,Clock.Timestamp);
            check(memory.TryReserve(600*1024,Clock.Timestamp,true,out essential),"actual reclaimed water bytes admit the previously blocked essential restore while preserving safety");
            memory.Cancel(essential);allocations=NativeWater.Allocations;
            for(int i=0;i<30;i++){Clock.Timestamp+=33333;Prepare(water);}
            check(NativeWater.Allocations==allocations&&!water.Active,"ongoing pressure cannot immediately rebuild reclaimed water and oscillate");
            Recover(memory);Prepare(water);
            check(water.CaptureReady&&NativeWater.Allocations==allocations+3,"stable headroom recovery restores the optional effect once");
            Observe(memory);memory.Observe(700*1024,0,0,Clock.Timestamp);Prepare(water);
            check(!water.Active&&NativeWater.Bytes==0,"draw preparation also yields an existing visible capture when pressure starts");
            water.Dispose();

            for(int failure=1;failure<=3;failure++){
                NativeWater.Reset();NativeWater.FailAtAllocation=failure;
                memory=new VisualMemoryPressure();Observe(memory);water=new WaterBumpEffect{Memory=memory};
                Prepare(water);
                check(!water.Active&&NativeWater.Bytes==0&&memory.ReservedBytes==0,"failure of each native map/capture allocation drains partial ownership before canceling admission");
                check(memory.UnderPressure&&memory.RetryAt>Clock.Timestamp,"real optional allocation failure records shared backoff");
                Observe(memory);allocations=NativeWater.Allocations;
                Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs-1;Prepare(water);
                check(NativeWater.Allocations==allocations,"a failed water allocation cannot retry every frame");
                NativeWater.FailAtAllocation=0;Recover(memory);Prepare(water);
                check(water.CaptureReady&&NativeWater.Bytes==169984,"transient map/capture failures recover without changing scenes or losing fallback rendering");
                water.Dispose();Observe(memory);
                check(memory.ReservedBytes==0&&NativeWater.Bytes==0,"recovered optional ownership has exact teardown accounting");
            }

            NativeWater.Reset();memory=new VisualMemoryPressure();Observe(memory);water=new WaterBumpEffect{Memory=memory};
            Prepare(water);Observe(memory);NativeWater.FailAtAllocation=NativeWater.Allocations+1;NativeWater.Height=528;
            Prepare(water);
            check(!water.Active&&NativeWater.Bytes==0&&memory.ReservedBytes==0,"a failed capture resize drains retained maps and replacement state while the base water remains usable");
            water.Dispose();
            NativeWater.Reset();memory=new VisualMemoryPressure();Observe(memory);water=new WaterBumpEffect{Memory=memory};
            GraphicsDevice.Default.IsFrameActive=true;bool rejected=false;
            try{Prepare(water);}catch(InvalidOperationException){rejected=true;}
            check(rejected&&NativeWater.Allocations==0,"water residency refuses allocation inside an active render frame");
            GraphicsDevice.Default.IsFrameActive=false;Prepare(water);GraphicsDevice.Default.IsFrameActive=true;rejected=false;
            try{water.Reclaim();}catch(InvalidOperationException){rejected=true;}
            check(rejected&&NativeWater.Bytes==169984,"water reclamation cannot free textures still owned by an active frame");
            GraphicsDevice.Default.IsFrameActive=false;water.Dispose();Observe(memory);
            check(NativeWater.Bytes==0&&memory.ReservedBytes==0,"frame-guard rejection preserves owner cleanup after EndFrame");
        }finally{
            GraphicsDevice.Default.IsFrameActive=originalFrame;Clock.Timestamp=originalClock;
            NativeWater.Reset();WaterTextureAssets.Reads=0;
        }
    }
}
