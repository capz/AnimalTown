using AnimalTown;
// Disposable emulator fixture only. Never invoke on a user's card.
var folder=args[0];BuildingCatalog.Configure(File.ReadAllBytes("out/disc/buildings.bin"));
var parts=Enumerable.Range(0,4).Select(n=>File.ReadAllBytes(Path.Combine(folder,"part"+n))).ToArray();
var state=SplitSaveCodec.DecodeCommitted(File.ReadAllBytes(Path.Combine(folder,"commit")),parts);
state.Space=0;state.X=19.5f;state.Z=38.5f;state.Facing=3.1415927f;state.HousePlaced=true;state.HouseTier=4;state.GardenTier=4;state.Town.HouseX=12;state.Town.HouseZ=22;
for(int z=16;z<46;z++)for(int x=6;x<42;x++){state.Town.Tiles[z*Town.Width+x]=(byte)Terrain.Grass;state.Town.Elevation[z*Town.Width+x]=0;}
state.Town.GroundCount=32;
for(int n=0;n<32;n++){var g=state.Town.Ground[n];g.X=9+n%8*4;g.Z=27+n/8*2;g.Kind=3;g.Item=n%3+1;g.Owner=0;g.Day=state.Town.Day;}
// Stable indoor dwell: camera routes compare the same outdoor props/trees.
for(int n=0;n<6;n++){var v=state.Town.Residents[n];v.Space=16;v.PosX=2;v.PosZ=2;v.JourneyPhase=3;v.Dwell=36000;v.JourneyBuilding=-1;v.JourneyNextX=v.JourneyNextZ=-1;}
state.Town.NotifyTerrainChanged();var next=SplitSaveCodec.Encode(state);var commit=SplitSaveCodec.EncodeCommit(state,next);var restored=SplitSaveCodec.DecodeCommitted(commit,next);
if(restored.Town.GroundCount!=32||!restored.HousePlaced||restored.GardenTier!=4||restored.Space!=0)throw new Exception("Submission fixture verification failed");
for(int n=0;n<4;n++)File.WriteAllBytes(Path.Combine(folder,"next"+n),next[n]);File.WriteAllBytes(Path.Combine(folder,"nextcommit"),commit);
Console.WriteLine("Validated disposable fixture: 32 mixed trees, maximum garden, closed cottage, clear foreground travel lane");
