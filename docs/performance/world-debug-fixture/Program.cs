using AnimalTown;
var folder=args[0];BuildingCatalog.Configure(File.ReadAllBytes("out/disc/buildings.bin"));
bool residency=args.Contains("--residency");
var parts=Enumerable.Range(0,4).Select(n=>File.ReadAllBytes(Path.Combine(folder,"part"+n))).ToArray();
var state=SplitSaveCodec.DecodeCommitted(File.ReadAllBytes(Path.Combine(folder,"commit")),parts);
state.Space=16;state.X=2;state.Z=3.3f;state.Facing=3.1415927f;
var occupied=new List<(float x,float z)>();
for(int n=0;n<6;n++){
 var v=state.Town.Residents[n];v.Space=16;v.JourneyPhase=3;v.Dwell=36000;v.JourneyBuilding=-1;v.JourneyNextX=v.JourneyNextZ=-1;
 var points=new[]{(1.7f,1.1f),(2.4f,1.3f),(1.6f,2.0f),(2.3f,2.1f),(1.7f,2.6f),(2.5f,2.7f)};
 var p=points[n];
 var options=from iz in Enumerable.Range(0,11) from ix in Enumerable.Range(0,11) let x=.5f+ix*.25f let z=.5f+iz*.25f where BuildingWorld.CanStand(state,16,x,z)&&(!residency||z<=2||Math.Abs(x-2)>.7f)&&occupied.All(o=>(o.x-x)*(o.x-x)+(o.z-z)*(o.z-z)>.25f) orderby (x-p.Item1)*(x-p.Item1)+(z-p.Item2)*(z-p.Item2) select (x,z);
 p=options.First();occupied.Add(p);Console.WriteLine($"Fixture NPC {n}: {p}");
 v.PosX=p.Item1;v.PosZ=p.Item2;v.Yaw=0;
}
if(residency){
 var candidates=from z in Enumerable.Range(20,Town.Height-40) from x in Enumerable.Range(20,Town.Width-40) where state.CanWalk(x+.5f,z+.5f,0)&&Enumerable.Range(0,BuildingWorld.Count).All(b=>{BuildingWorld.Entrance(state,b,out float ex,out float ez);return !BuildingWorld.Exists(state,b)||(ex-x)*(ex-x)+(ez-z)*(ez-z)>900;}) select (x,z);
 var spot=candidates.First();state.Space=0;state.X=spot.x+.5f;state.Z=spot.z+.5f;state.Facing=0;
}
var next=SplitSaveCodec.Encode(state);var commit=SplitSaveCodec.EncodeCommit(state,next);
var restored=SplitSaveCodec.DecodeCommitted(commit,next);
if(restored.Space!=(residency?0:16)||restored.Town.Residents.Any(v=>v.Space!=16||v.Dwell!=36000))throw new Exception("Fixture verification failed");
for(int n=0;n<4;n++){if(next[n].Length!=parts[n].Length)throw new Exception("Fixture length changed");File.WriteAllBytes(Path.Combine(folder,"next"+n),next[n]);}
File.WriteAllBytes(Path.Combine(folder,"nextcommit"),commit);
Console.WriteLine("Validated test-only indoor fixture: six villagers at safe positions, long dwell, committed schema unchanged");
