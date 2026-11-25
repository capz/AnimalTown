using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Timing;

int checks=0;
void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
void Prepare(VisualMemoryCoordinator coordinator,GameState state,PortalSystem doors,bool outdoors,LowPolyTrees trees,DroppedItemRenderer items,BuildingRenderer buildings,long now,long deadline)
{
    coordinator.RefreshDemand(state,doors,buildings,now);
    coordinator.Prepare(state,doors,outdoors,trees,items,buildings,now,deadline);
}
var memory=new VisualMemoryPressure();
memory.Observe(10*1024*1024,0,0,0);
Check(memory.TryReserve(3*1024*1024,0,false,out int first)&&memory.TryReserve(3*1024*1024,0,false,out _),"independent owners reserve shared physical headroom");
Check(!memory.TryReserve(4*1024*1024,0,true,out _),"essential allocation still respects other owners and safety reserve");
memory.Complete(first);
Check(memory.ReservedBytes==6*1024*1024,"completion cannot spend stale physical sample");
memory.Observe(7*1024*1024,0,0,1);
bool thirdAdmitted=memory.TryReserve(3*1024*1024,1,true,out int third);
Check(memory.ReservedBytes==6*1024*1024&&thirdAdmitted,"committed owner is counted by physical headroom without duplicate reservation");
memory.Cancel(third);
Check(memory.ReservedBytes==3*1024*1024,"failed/canceled fully drained owner returns only its reservation");
var low=new VisualMemoryPressure();low.Observe(1024*1024,2,500000,0);
Check(low.UnderPressure&&!low.CanAdmit(1,0,true),"first allocator-failure observation enters backoff");
Check(low.CanAdmit(0,0)&&low.TryReserve(0,0,false,out int zero)&&zero==0,"zero byte work needs no memory ticket or backoff");
Check(low.CanAdmit(272*1024,VisualMemoryPressure.FailureBackoffUs,true)&&!low.CanAdmit(1,VisualMemoryPressure.FailureBackoffUs),"essential scene restore can proceed under low memory after backoff");
Check(!low.CanAdmit(800*1024,VisualMemoryPressure.FailureBackoffUs,true),"essential admission preserves physical safety bytes");
low.Observe(4*1024*1024,2,500000,1000001);low.Observe(2*1024*1024,2,500000,3000001);low.Observe(4*1024*1024,2,500000,4000001);
Check(low.UnderPressure,"interrupted recovery restarts pressure hysteresis");
low.Observe(4*1024*1024,2,500000,7000001);Check(!low.UnderPressure,"stable restored headroom exits pressure");
low.AllocationFailed(8000,8000000);Check(low.UnderPressure&&!low.CanAdmit(1,8000000,true),"owner budget rejections also back off");
var bounded=new VisualMemoryPressure();bounded.Observe(int.MaxValue,0,0,0);var tickets=new int[32];
for(int i=0;i<tickets.Length;i++)Check(bounded.TryReserve(1,0,false,out tickets[i]),"bounded simultaneous-owner reservation");
Check(!bounded.TryReserve(1,0,true,out _),"ticket capacity cannot overwrite a live owner");
for(int i=0;i<1000;i++)Check(bounded.TryReserve(0,0,false,out int empty)&&empty==0,"zero byte work never consumes ticket slots");
for(int i=0;i<tickets.Length;i++)bounded.Cancel(tickets[i]);
Check(bounded.ReservedBytes==0,"canceling all drained owners releases every ticket byte");
var waiting=new VisualMemoryPressure();waiting.Observe(3*1024*1024,0,0,0);
Check(!waiting.TryReserve(3*1024*1024,0,true,out _)&&waiting.UnderPressure&&waiting.WaitingPeakBytes==3*1024*1024,"denied essential admission requests reclamation even above the low watermark");
Check(waiting.FailureCount==0&&waiting.RetryAt==0&&!waiting.TryReserve(1,0,false,out _),"headroom denial blocks speculative allocation without inventing allocator failure or retry latency");
waiting.Observe(5*1024*1024,0,0,1);
Check(waiting.TryReserve(3*1024*1024,1,true,out int admitted),"reclaimed headroom admits essential work while recovery hysteresis remains active");
waiting.Cancel(admitted);waiting.Observe(5*1024*1024,0,0,3000001);
Check(!waiting.UnderPressure,"fulfilled essential demand does not leave permanent pressure");
var abandoned=new VisualMemoryPressure();abandoned.Observe(3*1024*1024,0,0,0);abandoned.TryReserve(4*1024*1024,0,true,out _);
abandoned.Observe(3*1024*1024,0,0,1000001);abandoned.Observe(3*1024*1024,0,0,4000001);
Check(!abandoned.UnderPressure&&abandoned.WaitingPeakBytes==0,"a doorway reversal expires canceled demand instead of pinning pressure forever");

var state=new GameState(4){HousePlaced=true,Species=0,Space=0,X=140,Z=140};var doors=new PortalSystem();
var buildings=new BuildingRenderer(new Transform());
while(!buildings.AdvanceLoading(state)){}
Check(Model.Live==BuildingCatalog.Count*2&&ModelInstance.Live==Model.Live,"outdoor startup creates exterior kit without unused room assets");
Check(buildings.Ready(0)&&!buildings.ReadyForSpace(state,1),"art availability remains distinct from lazy room readiness");
var manager=new VisualMemoryCoordinator();var trees=new LowPolyTrees();var items=new DroppedItemRenderer();
state.Space=1;Clock.Timestamp=10000000;
for(int i=0;i<12;i++){Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);Clock.Timestamp++;}
Check(buildings.ReadyForSpace(state,1),"essential current-room floor and walls restore completely");
int warm=Model.Live;
Check(!buildings.ReleaseUnused(state,doors,Clock.Timestamp,true),"pressure never releases current indoor room parts");
state.Space=0;state.X=140;state.Z=140;
doors.Actors[0].Phase=DoorPhase.Waiting;doors.Actors[0].Source=new ActorLocation(0,140,140,0);doors.Actors[0].Destination=new ActorLocation(1,2,2,0);
Check(!buildings.ReleaseUnused(state,doors,Clock.Timestamp,true),"doorway destination stays pinned before commit");
doors.Actors[0].Phase=DoorPhase.Idle;
Check(buildings.ReleaseUnused(state,doors,Clock.Timestamp,true)&&Model.Live==warm-1,"pressure releases one unused room part per grant");
while(buildings.ReleaseUnused(state,doors,Clock.Timestamp,true)){}
Check(Model.Live==BuildingCatalog.Count*2,"unused interiors return real owner allocations");

state.Space=1;Model.FailInstanceNext=true;int before=Model.Live;
Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);Clock.Timestamp++;
Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);
Check(Model.Live==before&&ModelInstance.Live==before&&manager.Pressure.ReservedBytes==0,"instance allocation failure disposes adopted model and cancels reservation");
int loads=Model.Loads;
Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);
Check(Model.Loads==loads,"failed room restore does not read again during retry backoff");
Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;
for(int i=0;i<12;i++){Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);Clock.Timestamp++;}
Check(buildings.ReadyForSpace(state,1),"essential room recovers after allocator backoff without waiting for optional pressure recovery");

state.Space=0;while(buildings.ReleaseUnused(state,doors,Clock.Timestamp,true)){}
state.Space=1;Model.DeferNext=true;
Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);
Check(manager.Pressure.ReservedBytes>0,"in-flight disc/native owner retains a memory reservation");
state.Space=0;Model.CompleteDeferred();
Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);
Check(manager.Pressure.ReservedBytes==0&&Model.Live==BuildingCatalog.Count*2,"reversed doorway drops completed unused result and its ticket without a leak");
NativeMemory.FreeBytes=1024*1024;TextureCache.Unused=1;items.Eligible=trees.Eligible=true;
Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);
Check(TextureCache.Unused==0&&items.Eligible&&trees.Eligible,"pressure reclaims one owner with cache before category priority");
Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);
Check(!items.Eligible&&trees.Eligible,"a subsequent bounded grant retires an unused outdoor category");
buildings.Dispose();Check(Model.Live==0&&ModelInstance.Live==0&&Model.Bytes==0,"teardown releases every model and instance exactly once");
NativeMemory.FreeBytes=10*1024*1024;state.Space=0;Clock.Timestamp+=10000000;
var startup=new BuildingRenderer(new Transform()){Memory=new VisualMemoryPressure()};startup.Memory.Observe(NativeMemory.FreeBytes,NativeMemory.Failures,0,Clock.Timestamp);
Model.FailNativeNext=true;startup.AdvanceLoading(state);startup.AdvanceLoading(state);
Check(Model.Live==0&&startup.LoadedParts==0&&startup.Memory.ReservedBytes==0,"generic SDK startup error with native allocator evidence retries without leaking or marking art unavailable");
loads=Model.Loads;startup.AdvanceLoading(state);Check(Model.Loads==loads,"startup allocation retry observes backoff");
Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;
bool startupReady=false;for(int i=0;i<64&&!startupReady;i++){startup.Memory.Observe(NativeMemory.FreeBytes-Model.Bytes,NativeMemory.Failures,0,Clock.Timestamp);startupReady=startup.AdvanceLoading(state);Clock.Timestamp++;}
Check(startupReady&&startup.Ready(0)&&startup.Ready(1)&&startup.Ready(KobanSite.Definition)&&Model.Live==BuildingCatalog.Count*2,"startup can recover all compact definitions after memory returns");
startup.Dispose();Check(Model.Live==0&&ModelInstance.Live==0,"retrying startup preserves teardown ownership");
state.Space=1;state.Town.Residents[0].Space=24;
var indoorStartup=new BuildingRenderer(new Transform());while(!indoorStartup.AdvanceLoading(state)){}
Check(indoorStartup.ReadyForSpace(state,1)&&!indoorStartup.ReadyForSpace(state,24)&&Model.Live==BuildingCatalog.Count*2+5,"indoor save loads its own room kit while an unseen NPC shop interior stays cold");
indoorStartup.Dispose();Check(Model.Live==0&&ModelInstance.Live==0,"indoor startup has exact owner teardown");
state.Space=KobanSite.Room;state.BuildingTypes[KobanSite.Building]=KobanSite.Definition;
var kobanStartup=new BuildingRenderer(new Transform());while(!kobanStartup.AdvanceLoading(state)){}
Check(kobanStartup.ReadyForSpace(state,KobanSite.Room)&&!kobanStartup.ReadyForSpace(state,1)&&Model.Live==BuildingCatalog.Count*2+5,"koban save restores its own seven-part definition without obsolete closed-model copies or unrelated interiors");
Check(!kobanStartup.ReleaseUnused(state,doors,Clock.Timestamp,true),"global pressure preserves the active koban interior");
kobanStartup.Dispose();Check(Model.Live==0&&ModelInstance.Live==0,"koban native owner slots retain correct seven-part indexing at teardown");
var catalogBytes=new byte[16+ModelMemoryCatalog.Count*ModelMemoryCatalog.Stride];
void Put(int at,int value){for(int i=0;i<4;i++)catalogBytes[at+i]=(byte)(value>>(i*8));}
Put(0,ModelMemoryCatalog.Magic);Put(4,ModelMemoryCatalog.Version);Put(8,ModelMemoryCatalog.Count);Put(12,ModelMemoryCatalog.Stride);
for(int i=0;i<ModelMemoryCatalog.Count;i++){
    int at=16+i*ModelMemoryCatalog.Stride;Put(at,160);Put(at+8,8192);Put(at+12,1024);
    Put(at+16,8192+192+1024+ModelMemoryPlan.LoadGuardBytes);Put(at+20,8192+192+2048+ModelMemoryPlan.LoadGuardBytes);
}
var catalog=new ModelMemoryCatalog(catalogBytes);NativeMemory.Failures=0;
Check(catalog.StartupPeak(0,0)==catalog.StartupPeak(0,false)-1024&&catalog.StartupPeak(0,4)==catalog.StartupPeak(0,false)+3*1024,"cooked per-instance upper bound charges every same-species visitor/player instance without duplicating shared model cost");
var startupMemory=new VisualMemoryPressure();startupMemory.Observe(NativeMemory.FreeBytes,0,0,Clock.Timestamp);
var admission=new StartupModelAdmission(startupMemory,catalog);
Check(admission.Begin(0,true,Clock.Timestamp)&&admission.ReservedPeak==catalog.StartupPeak(0,true),"startup reserves its actual selected-species two-instance package plan before native load");
var loaded=Model.LoadAsync("fox").GetAwaiter().GetResult();admission.Validate(loaded);
var npc=loaded.CreateInstance(512*1024);var player=loaded.CreateInstance(512*1024);
Check(startupMemory.ReservedBytes==admission.ReservedPeak,"startup admission holds through both instance allocations");
admission.Complete();Check(admission.Active<0&&startupMemory.ReservedBytes>0,"published startup retains debt until physical sample");
npc.Dispose();player.Dispose();loaded.Dispose();startupMemory.Observe(NativeMemory.FreeBytes,0,0,Clock.Timestamp);
Check(startupMemory.ReservedBytes==0,"successful startup debt becomes real physical ownership exactly once");
Check(admission.Begin(1,false,Clock.Timestamp),"next startup species admits after physical refresh");
Model.FailNativeNext=true;Exception? failure=null;
try{Model.LoadAsync("bunny").GetAwaiter().GetResult();}catch(Exception error){failure=error;}
Check(failure!=null&&admission.Fail(failure,Clock.Timestamp)&&startupMemory.ReservedBytes==0&&admission.Active<0,"evidenced generic SDK allocation failure retries after fully drained owner releases its ticket");
Check(!admission.Begin(1,false,Clock.Timestamp),"startup model retry cannot churn during failure backoff");
Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;startupMemory.Observe(NativeMemory.FreeBytes,NativeMemory.Failures,0,Clock.Timestamp);
Check(admission.Begin(1,false,Clock.Timestamp),"essential startup retry does not wait for optional pressure hysteresis");
Check(!admission.Fail(new InvalidDataException("Malformed model"),Clock.Timestamp)&&startupMemory.ReservedBytes==0,"malformed model with no allocator failure remains fatal instead of an endless memory retry");
Check(admission.Begin(2,false,Clock.Timestamp),"a distinct startup owner can be admitted after nonallocation failure");
admission.Cancel();Check(startupMemory.ReservedBytes==0&&Model.Live==0&&ModelInstance.Live==0,"drained startup cancellation leaves no reservation or native owner");
Check(admission.Begin(3,4,Clock.Timestamp)&&admission.ReservedPeak==catalog.StartupPeak(3,4),"startup reserves a complete same-species actor cohort before admitting native work");
admission.Cancel();Check(startupMemory.ReservedBytes==0,"canceled cohort admission returns every reserved instance byte");
state.Space=0;BuildingWorld.Entrance(state,0,out float entryX,out float entryZ);state.X=entryX;state.Z=entryZ;
var prefetchBuildings=new BuildingRenderer(new Transform());while(!prefetchBuildings.AdvanceLoading(state)){}
NativeMemory.FreeBytes=10*1024*1024;NativeMemory.Failures=0;Clock.Timestamp+=10000000;
var prefetchMemory=new VisualMemoryCoordinator();loads=Model.Loads;
Prepare(prefetchMemory,state,doors,true,trees,items,prefetchBuildings,Clock.Timestamp,Clock.Timestamp+1000);
Check(Model.Loads==loads,"required coordinator phase cannot spend headroom on speculative nearby rooms");
prefetchMemory.PrepareOptionalBuildings(state,doors,prefetchBuildings,Clock.Timestamp,Clock.Timestamp+1000,essentialWorkPending:true);
Check(Model.Loads==loads,"nearby room prefetch yields while another owner has essential work");
Model.DeferNext=true;prefetchMemory.PrepareOptionalBuildings(state,doors,prefetchBuildings,Clock.Timestamp,Clock.Timestamp+1000);
Check(Model.Loads==loads+1&&prefetchMemory.Pressure.ReservedBytes>0,"last-priority nearby-room read owns its allocation ticket");
Model.CompleteDeferred();NativeMemory.FreeBytes=600*1024;Clock.Timestamp+=1000000;
Prepare(prefetchMemory,state,doors,true,trees,items,prefetchBuildings,Clock.Timestamp,Clock.Timestamp);
Check(prefetchMemory.Pressure.ReservedBytes==0&&Model.Live==BuildingCatalog.Count*2,"completed speculative read drains under pressure with zero adoption credit and never allocates an instance");
prefetchBuildings.Dispose();Check(Model.Live==0&&ModelInstance.Live==0,"speculative cancellation retains exact source-model ownership");

// Real coordinator/building code with deterministic slow bookkeeping/native
// operations: demand and sampling can exceed a grant before work even begins.
state.Space=0;state.X=140;state.Z=140;doors.Actors[0].Phase=DoorPhase.Idle;
var scheduledBuildings=new BuildingRenderer(new Transform());while(!scheduledBuildings.AdvanceLoading(state)){}
var scheduler=new VisualMemoryCoordinator();var credit=new VisualResidencyBudget();
state.Space=1;NativeMemory.FreeBytes=1024*1024;NativeMemory.ReadCostUs=2500;
TextureCache.Unused=2;TextureCache.TrimCostUs=2000;
Clock.Timestamp+=10000000;long preparationAt=Clock.Timestamp;
Clock.Timestamp+=3000;scheduler.RefreshDemand(state,doors,scheduledBuildings,preparationAt);
long workDeadline=credit.Begin(afterDraw:false);
Check(Clock.Timestamp-preparationAt==5500&&credit.Remaining==1000,"expensive demand and physical sampling precede the executable work grant");
int scheduledLoads=Model.Loads,scheduledInstances=ModelInstance.Live;Model.DeferNext=true;
scheduler.Prepare(state,doors,false,trees,items,scheduledBuildings,preparationAt,workDeadline);credit.End();
Check(Model.Loads==scheduledLoads+1&&ModelInstance.Live==scheduledInstances,"one required part starts despite bookkeeping longer than the work budget");
Check(credit.Remaining==0&&TextureCache.Unused==1,"optional reclaim consumes only remaining credit after required work");

// A stuck worker and an adoption denied CPU credit are distinguishable without
// repeating a diagnostic every presentation.
Clock.Timestamp+=1000001;scheduler.RefreshDemand(state,doors,scheduledBuildings,Clock.Timestamp);
workDeadline=credit.Begin(afterDraw:true);
int waits=Dolphin.Diagnostics.DebugOverlay.Lines.Count(line=>line.Contains("BUILDING PART WAIT"));
scheduler.Prepare(state,doors,false,trees,items,scheduledBuildings,Clock.Timestamp,workDeadline);credit.End();
Check(Dolphin.Diagnostics.DebugOverlay.Lines.Count(line=>line.Contains("BUILDING PART WAIT"))==waits+1&&
    Dolphin.Diagnostics.DebugOverlay.Lines.Last(line=>line.Contains("BUILDING PART WAIT")).Contains("completed 0 deadlineDebt 0"),"one delayed-load diagnostic identifies an incomplete worker");
scheduler.Prepare(state,doors,false,trees,items,scheduledBuildings,Clock.Timestamp,workDeadline);
Check(Dolphin.Diagnostics.DebugOverlay.Lines.Count(line=>line.Contains("BUILDING PART WAIT"))==waits+1,"pending wait diagnostics remain once per load");

Model.CompleteDeferred();Clock.Timestamp+=1000001;
preparationAt=Clock.Timestamp;Clock.Timestamp+=3000;scheduler.RefreshDemand(state,doors,scheduledBuildings,preparationAt);
workDeadline=credit.Begin(afterDraw:false);Model.InstanceCostUs=1100;
scheduler.Prepare(state,doors,false,trees,items,scheduledBuildings,preparationAt,workDeadline);credit.End();
Check(ModelInstance.Live==scheduledInstances+1&&Model.Loads==scheduledLoads+1&&!scheduledBuildings.ReadyForSpace(state,1),"completed required part adopts before optional reclamation and no second part starts in that call");
Check(TextureCache.Unused==1&&credit.Remaining==0,"an indivisible required adoption overrun prevents optional reclaim work");
Clock.Timestamp+=200000;scheduler.RefreshDemand(state,doors,scheduledBuildings,Clock.Timestamp);
workDeadline=credit.Begin(afterDraw:true);
scheduler.Prepare(state,doors,false,trees,items,scheduledBuildings,Clock.Timestamp,workDeadline);credit.End();
Check(Model.Loads==scheduledLoads+1&&TextureCache.Unused==1,"after-draw opportunity cannot replenish exhausted CPU credit");

Model.InstanceCostUs=0;TextureCache.TrimCostUs=0;TextureCache.Unused=0;NativeMemory.ReadCostUs=0;
Clock.Timestamp+=1000001;scheduler.RefreshDemand(state,doors,scheduledBuildings,Clock.Timestamp);
workDeadline=credit.Begin(afterDraw:false);
scheduler.Prepare(state,doors,false,trees,items,scheduledBuildings,Clock.Timestamp,workDeadline);credit.End();
Check(Model.Loads==scheduledLoads+2&&ModelInstance.Live==scheduledInstances+1,"the next frame starts only one more part and leaves adoption to a subsequent grant");
Clock.Timestamp+=1000001;scheduler.RefreshDemand(state,doors,scheduledBuildings,Clock.Timestamp);
workDeadline=credit.Begin(afterDraw:true);Clock.Timestamp=workDeadline+500;
scheduler.Prepare(state,doors,false,trees,items,scheduledBuildings,Clock.Timestamp,workDeadline);credit.End();
Check(Dolphin.Diagnostics.DebugOverlay.Lines.Last(line=>line.Contains("BUILDING PART WAIT")).Contains("completed 1 deadlineDebt 500"),"delayed completed-load diagnostic exposes CPU adoption starvation separately");
for(int i=0;i<10;i++){
    Clock.Timestamp+=1000001;scheduler.RefreshDemand(state,doors,scheduledBuildings,Clock.Timestamp);
    workDeadline=credit.Begin(afterDraw:false);
    scheduler.Prepare(state,doors,false,trees,items,scheduledBuildings,Clock.Timestamp,workDeadline);credit.End();
}
Check(scheduledBuildings.ReadyForSpace(state,1),"required floor and walls eventually complete after expensive preparation and queued reclamation");
scheduledBuildings.Dispose();Check(Model.Live==0&&ModelInstance.Live==0,"bounded scheduler and delayed diagnostics retain exact owner cleanup");
credit.Begin(afterDraw:false);Clock.Timestamp+=350;credit.End();Clock.Timestamp+=1000000;
workDeadline=credit.Begin(afterDraw:true);
Check(credit.Remaining==650&&workDeadline-Clock.Timestamp==650,"draw and preparation gaps preserve only the unused first-opportunity CPU credit");
Clock.Timestamp+=200;credit.End();Clock.Timestamp+=1000000;credit.Begin(afterDraw:true);
Check(credit.Remaining==450,"subsequent opportunities spend the same presentation credit rather than resetting it");

GraphicsDevice.Default.IsFrameActive=true;bool rejected=false;
try{Prepare(manager,state,doors,false,trees,items,buildings,Clock.Timestamp,Clock.Timestamp+1000);}catch(InvalidOperationException){rejected=true;}
GraphicsDevice.Default.IsFrameActive=false;Check(rejected,"global reclamation rejects a frame with live GPU owners");
TownLightsChecks.Run(Check);
WaterOpticsChecks.Run(Check);
Console.WriteLine(checks+" visual memory checks passed");
