using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Timing;

int checks=0;void Check(bool result,string message){checks++;if(!result)throw new Exception(message);}
var memory=new VisualMemoryPressure();var trees=new LowPolyTrees(new Transform()){Memory=memory};
memory.Observe(VisualMemoryPressure.SafetyBytes+LowPolyTrees.ColdPeakBytes-1,0,0,0);
Check(!trees.Prepare(0,false)&&GraphicsMemory.Allocations==0,"cold tree allocation is denied before any native owner exists");
memory.Observe(4*1024*1024,0,0,1);GraphicsMemory.FailAfter=2;
Check(!trees.Prepare(0,false)&&GraphicsMemory.Bytes==0&&memory.ReservedBytes==0,"partial tree texture/mesh failure disposes every new native owner and its reservation");
int attempts=GraphicsMemory.Allocations;
Check(!trees.Prepare(0,false)&&GraphicsMemory.Allocations==attempts,"failed cold trees honor retry backoff");
Clock.Timestamp=VisualMemoryPressure.FailureBackoffUs+1;memory.Observe(4*1024*1024,GraphicsMemory.Failures,0,Clock.Timestamp);
Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;
Check(trees.Prepare(0,false)&&GraphicsMemory.Bytes>0&&GraphicsMemory.Bytes<=LowPolyTrees.ColdPeakBytes,"recovered tree creation fits its derived native allocation peak");
int warmBytes=GraphicsMemory.Bytes;attempts=GraphicsMemory.Allocations;
memory.Observe(0,GraphicsMemory.Failures,0,Clock.Timestamp);
Check(trees.Prepare(0,false)&&trees.Prepare(2,false)&&GraphicsMemory.Allocations==attempts&&GraphicsMemory.Bytes==warmBytes,"warm tree draw/season updates preserve existing native owners with zero free headroom");
trees.Dispose();Check(GraphicsMemory.Bytes==0,"tree teardown returns every native byte");

var state=new GameState(7319){X=30,Z=38,Space=0};Array.Fill(state.Town.Tiles,(byte)Terrain.Grass);Array.Clear(state.Town.Elevation);
state.Town.GroundCount=1;var drop=state.Town.Ground[0];drop.Kind=0;drop.Item=5;drop.X=30;drop.Z=32;
var items=new DroppedItemRenderer{Memory=memory};Clock.Timestamp+=10000000;
memory.Observe(0,GraphicsMemory.Failures,0,Clock.Timestamp);
Check(!items.Prepare(state,0)&&GraphicsMemory.Bytes==0,"cold dropped-item texture/geometry waits before allocation when headroom is unavailable");
Clock.Timestamp+=100001;memory.Observe(4*1024*1024,GraphicsMemory.Failures,0,Clock.Timestamp);
Check(items.Prepare(state,0)&&GraphicsMemory.Bytes>0,"item admission resumes when shared headroom returns");
warmBytes=GraphicsMemory.Bytes;memory.Observe(4*1024*1024-warmBytes,GraphicsMemory.Failures,0,Clock.Timestamp);
state.Town.GroundCount=20;for(int i=0;i<20;i++){var g=state.Town.Ground[i];g.Kind=0;g.Item=5;g.X=30;g.Z=32;}
GraphicsMemory.FailAfter=0;
Check(!items.Prepare(state,0)&&GraphicsMemory.Bytes==warmBytes&&memory.ReservedBytes==0,"failed item capacity growth preserves old geometry/texture instead of disposing before adoption");
attempts=GraphicsMemory.Allocations;Check(!items.Prepare(state,0)&&GraphicsMemory.Allocations==attempts,"failed item growth does not retry every frame");
Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;memory.Observe(4*1024*1024,GraphicsMemory.Failures,0,Clock.Timestamp);
Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;
Check(items.Prepare(state,0)&&GraphicsMemory.Bytes>warmBytes,"item growth retries and atomically replaces the previous native buffer");
warmBytes=GraphicsMemory.Bytes;memory.Observe(0,GraphicsMemory.Failures,0,Clock.Timestamp);attempts=GraphicsMemory.Allocations;
Check(items.Prepare(state,0)&&GraphicsMemory.Allocations==attempts,"stationary warm drops continue drawing without new allocation or admission");
Clock.Timestamp+=10000000;memory.Observe(4*1024*1024,GraphicsMemory.Failures,0,Clock.Timestamp);
for(int i=0;i<20;i++)state.Town.Ground[i].Item=4;GraphicsMemory.FailAfter=1;
Check(!items.Prepare(state,0)&&GraphicsMemory.Bytes==warmBytes&&memory.ReservedBytes==0,"new-category mesh failure cleans its newly created texture and retains previously warm art");
Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;memory.Observe(4*1024*1024,GraphicsMemory.Failures,0,Clock.Timestamp);
Clock.Timestamp+=VisualMemoryPressure.FailureBackoffUs+1;Check(items.Prepare(state,0),"new category recovers after partial allocation failure");
var renderer=new Renderer();items.Draw(renderer);Check(renderer.Draws>0,"only valid adopted native buffers are submitted after recovery");
items.Dispose();Check(GraphicsMemory.Bytes==0,"all grown/replaced/category native allocations return exactly once at teardown");
Console.WriteLine(checks+" visual allocation checks passed");
