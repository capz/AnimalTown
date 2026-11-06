using AnimalTown;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
string[] available={"ui","menu","text","trim","terrain","movement","dialogue","generation","life","saves","buildings","core"};
if(args.Length!=0&&(args.Length!=2||args[0]!="--suites"))throw new ArgumentException("Use --suites name,name or omit for all checks");
var selected=args.Length==0?available:args[1].Split(',');
foreach(string name in selected)if(!available.Contains(name))throw new ArgumentException("Unknown check suite: "+name);
bool Wants(string name)=>selected.Contains(name);
if(Wants("ui"))UiShapePlanChecks.Run(Check);
if(Wants("menu"))MenuNavigationChecks.Run(Check);
if(Wants("trim"))TownTrimChecks.Run(Check);
if(Wants("text"))WrappedTextChecks.Run(Check);
if(Wants("terrain"))TerrainEdgeChecks.Run(Check);
if(Wants("movement"))PlayerMovementChecks.Run(Check);
if(Wants("dialogue"))DialogueVoiceChecks.Run(Check);
if(Wants("generation"))TownGenerationChecks.Run(Check);
if(Wants("life"))TerrainLifeChecks.Run(Check);
if(Wants("saves"))SplitSaveChecks.Run(Check);
if(Wants("buildings"))BuildingChecks.Run(Check);
if(!Wants("core")){Console.WriteLine(checks+" checks passed; suites: "+string.Join(",",selected));return;}
var terrainEdit=new GameState(74);int terrainVersion=terrainEdit.Town.TerrainVersion;
Check(terrainEdit.Town.SetTerrain(20,20,Terrain.Water)&&terrainEdit.Town.TerrainVersion==terrainVersion+1,"terrain edit invalidates render caches");
Check(!terrainEdit.Town.SetTerrain(20,20,Terrain.Water)&&!terrainEdit.Town.SetTerrain(-1,20,Terrain.Grass)&&terrainEdit.Town.TerrainVersion==terrainVersion+1,"unchanged/invalid terrain edits preserve cache revision");
var terrainRestored=SaveCodec.Decode(SaveCodec.Encode(terrainEdit));
Check(terrainRestored.Town.At(20,20)==(int)Terrain.Water&&terrainRestored.Town.TerrainVersion>0,"edited terrain survives save restore with render revision initialized");
terrainVersion=terrainEdit.Town.TerrainVersion;terrainEdit.Town.Tiles[20*Town.Width+20]=(byte)Terrain.Grass;terrainEdit.Town.NotifyTerrainChanged();
Check(terrainEdit.Town.TerrainVersion==terrainVersion+1&&terrainEdit.Town.At(20,20)==(int)Terrain.Grass,"bulk terrain importer can invalidate retained rendering once");
var game=new GameState(73);game.Rollover(100,100);
Check(game.PlaceHouse(12,22,false),"starter plot");
Check(!game.CanPlace(10,13,26,0,0,1),"guest placement rejected");
Check(!game.CanPlace(10,0,0,0,0,0),"outside garden rejected");
Check(!game.CanPlace(11,17,25,0,0,0),"footprint crosses fence");
game.GardenTier=1;Check(game.CanPlace(11,19,25,1,0,0),"rotation changes footprint");game.GardenTier=0;
Check(!game.Expand(false,100),"debt prevents expansion");
game.Wallet=99999;game.Savings=long.MaxValue;Check(!game.Deposit(1)&&game.Wallet==99999,"savings overflow atomic");
game.Savings=0;Check(game.Deposit(1000)&&game.Withdraw(1000),"banking");
game.Pockets[0]=22;Check(!game.Use(0,6,0,0)&&game.Pockets[0]==22,"money bag overflow atomic");
game.Wallet=1000;Check(game.Use(0,6,0,0)&&game.Wallet==2000,"money bag consumed");
for(int i=0;i<game.Capacity;i++)game.Pockets[i]=1;
game.Wallet=1000;int stock=game.Town.Stock[0];Check(!game.Buy(0)&&game.Wallet==1000&&game.Town.Stock[0]==stock,"full pockets purchase atomic");
game.Pockets[0]=0;Check(game.Buy(0),"stock purchase");
Check(!game.Buy(0),"sold stock cannot buy twice");
game.Debt=0;Check(game.Expand(false,100),"expansion order");game.Rollover(100,100);Check(game.HouseTier==0,"not early");game.Rollover(101,100);Check(game.HouseTier==1&&game.PendingKind==0,"overnight construction");
game.Savings=10000;game.Rollover(102,101);Check(game.Savings==10050,"monthly interest");game.Rollover(101,100);game.Rollover(102,101);Check(game.Savings==10050,"time reversal no duplicate interest");
game.Sequence=17;var bytes=SaveCodec.Encode(game);var restored=SaveCodec.Decode(bytes);Check(restored.Sequence==17&&restored.Wallet==game.Wallet&&restored.Savings==game.Savings,"save round trip");
bytes[bytes.Length-1]^=1;bool rejected=false;try{SaveCodec.Decode(bytes);}catch{rejected=true;}Check(rejected,"corrupt save rejected");
game.Town.AddGround(10,30,40,0,102);game.Rollover(103,101);int lost=game.Town.FindGround(30,40);Check(lost>=0&&game.Town.Ground[lost].Kind==4,"rollover sweeps drops");Check(!game.ClaimLost(lost,1),"lost property owner lock");
for(int seed=0;seed<20;seed++)
{
    var town=new Town(seed);var seen=new bool[Town.Cells];var queue=new int[Town.Cells];int front=0,back=0;queue[back++]=38*Town.Width+18;seen[queue[0]]=true;int[] dx={-1,1,0,0},dz={0,0,-1,1};
    while(front<back){int cell=queue[front++],x=cell%Town.Width,z=cell/Town.Width;for(int d=0;d<4;d++){int nx=x+dx[d],nz=z+dz[d];if(town.Walkable(nx,nz)&&!seen[nz*Town.Width+nx]){seen[nz*Town.Width+nx]=true;queue[back++]=nz*Town.Width+nx;}}}
    Check(seen[28*Town.Width+32]&&seen[28*Town.Width+50]&&seen[10*Town.Width+36]&&seen[50*Town.Width+65],"mandatory areas connected");
    for(int i=0;i<6;i++)Check(seen[town.Residents[i].Z*Town.Width+town.Residents[i].X],"resident reachable");
}
Check(Town.ScreenTiles==8&&Town.Width==Town.Height&&Town.Width==8*Town.ScreenTileSize&&Town.Cells==25600,"8 x 8 square screen tiles");
double previousFocusDepth=-6*RenderView.ForwardY-8*RenderView.ForwardZ;
double focusDepth=-CylinderWorld.CameraHeight*RenderView.ForwardY-CylinderWorld.CameraBack*RenderView.ForwardZ;
Check(Math.Abs(focusDepth/previousFocusDepth-.9)<.00001,"closer camera shows ninety percent of the previous horizontal span at the player");
Check(Math.Abs(Math.Atan2(-RenderView.ForwardY,-RenderView.ForwardZ)*180/Math.PI-36.8699)<.001,"outdoor camera tilts down to the player while retaining its lens and eye position");
Check(CylinderWorld.ViewHalfWidth>CylinderWorld.Far*Math.Tan(CylinderWorld.CameraFieldOfView*Math.PI/360)*4/3,"cylinder extends past far-plane viewport sides");
double oldRadius=400/(2*Math.PI),oldEye=Math.Sqrt((oldRadius+CylinderWorld.CameraHeight)*(oldRadius+CylinderWorld.CameraHeight)+CylinderWorld.CameraBack*CylinderWorld.CameraBack);
double eye=Math.Sqrt((CylinderWorld.Radius+CylinderWorld.CameraHeight)*(CylinderWorld.Radius+CylinderWorld.CameraHeight)+CylinderWorld.CameraBack*CylinderWorld.CameraBack);
double oldHorizon=oldRadius*(Math.Acos(oldRadius/oldEye)-Math.Atan2(CylinderWorld.CameraBack,oldRadius+CylinderWorld.CameraHeight));
double horizon=CylinderWorld.Radius*(Math.Acos(CylinderWorld.Radius/eye)-Math.Atan2(CylinderWorld.CameraBack,CylinderWorld.Radius+CylinderWorld.CameraHeight));
Check(CylinderWorld.Radius<oldRadius&&horizon<oldHorizon-1,"stronger cylinder brings the forward ground horizon over a metre closer");
Check(TerrainTiles.GroundRowsAhead>=Math.Ceiling(horizon)+4&&TerrainTiles.GroundRowsAhead<48&&CylinderWorld.Far<70,"reduced draw windows retain a generous margin beyond the curved horizon");
var curved=CylinderWorld.Surface(-12,0);Check(curved.Y<oldRadius*Math.Cos(12/oldRadius)-oldRadius,"nearby ground bends more strongly at the same logical coordinate");
for(int i=-50;i<=50;i++)
{
    var pose=CylinderWorld.Surface(i,0);double radiusSquared=(pose.Y+CylinderWorld.Radius)*(pose.Y+CylinderWorld.Radius)+pose.Z*pose.Z;
    Check(Math.Abs(radiusSquared-CylinderWorld.Radius*CylinderWorld.Radius)<.01,"true cylinder radius");
    Check(Math.Abs(pose.UpY*pose.UpY+pose.UpZ*pose.UpZ-1)<.00001,"unit radial object normal");
    Check(Math.Abs((pose.Y+CylinderWorld.Radius)*pose.UpY+pose.Z*pose.UpZ-CylinderWorld.Radius)<.0001,"object down points toward cylinder axis");
}
game.X=Town.Width-5;game.Z=Town.Height-5;var wide=SaveCodec.Decode(SaveCodec.Encode(game));Check(wide.X==game.X&&wide.Z==game.Z,"full square world coordinates save");
Check(game.Capacity==10,"starter backpack has ten slots");game.Wallet=5999;Check(!game.UpgradeBackpack()&&game.Capacity==10&&game.Wallet==5999,"backpack upgrade rejects insufficient funds atomically");
game.Wallet=6000;Check(game.UpgradeBackpack()&&game.Capacity==20&&game.Wallet==0,"first backpack upgrade");
game.Wallet=18000;Check(game.UpgradeBackpack()&&game.Capacity==30&&game.Wallet==0,"second backpack upgrade");Check(!game.UpgradeBackpack(),"backpack never exceeds thirty slots");
game.Pockets[29]=10;var backpackSave=SaveCodec.Decode(SaveCodec.Encode(game));Check(backpackSave.Capacity==30&&backpackSave.Pockets[29]==10,"backpack tier and last slot persist");
int[] expectedSeasons={3,3,0,0,0,1,1,1,2,2,2,3};
for(int m=0;m<12;m++){Check(TerrainTiles.Season(2026*12+m)==expectedSeasons[m],"RTC terrain season");Check(TerrainTiles.Season(m,2)==2,"forced developer terrain season");}
int variantsSeen=0;
for(int x=-20;x<20;x++)for(int z=-20;z<20;z++)
{int variant=TerrainTiles.Variant(x,z,TerrainTiles.Grass,4);variantsSeen|=1<<variant;Check(variant>=0&&variant<4&&variant==TerrainTiles.Variant(x,z,TerrainTiles.Grass,4),"stable bounded floor variation including negative viewport coordinates");}
Check(variantsSeen==15,"every authored repeat variant is reachable");
Check(TerrainTiles.Surface((int)Terrain.Water)==TerrainTiles.Water&&TerrainTiles.Surface((int)Terrain.Beach)==TerrainTiles.Sand,"terrain material independent of collision IDs");
Check(TerrainTiles.PatchCoordinate(0,8,1)==0&&TerrainTiles.PatchCoordinate(1,8,1)==1&&TerrainTiles.PatchCoordinate(7,8,1)==2&&TerrainTiles.PatchCoordinate(8,8,1)==3,"nine-slice fixed corners");
Check(Math.Abs(TerrainTiles.PatchCoordinate(4,8,1)-1.5f)<.00001f,"nine-slice center stretches without repeating");
for(int row=0;row<TerrainTiles.GroundRows;row++)for(int offset=0;offset<=4;offset++)
{
    float focus=offset*.5f;var pose=CylinderWorld.Surface(-TerrainTiles.GroundRowsAhead+row,focus);
    double depth=(pose.Y-CylinderWorld.CameraHeight)*RenderView.ForwardY+(pose.Z-focus-CylinderWorld.CameraBack)*RenderView.ForwardZ;
    double projectedHalfWidth=Math.Max(0,depth)*Math.Tan(CylinderWorld.CameraFieldOfView*Math.PI/360)*4/3;
    Check(TerrainTiles.RowHalfWidth(row)>projectedHalfWidth+4,"textured cylinder rows extend beyond the padded viewport including sub-cell camera motion");
}
Check(TerrainTiles.GroundVertexCount<40000,"raised-camera padded terrain envelope remains bounded");
var arrival=new GameState(7319);arrival.Town.AddGround(3,18,39,3,0);
Check(arrival.PlaceHouse(15,35,false),"house placement with a blocked doorway finds a clear arrival");
Check(arrival.CanWalk(arrival.X,arrival.Z,0)&&arrival.Z>=40.5f,"new house arrival is outside its collision footprint and avoids trees");
arrival.X=18;arrival.Z=38;Check(!arrival.CanWalk(arrival.X,arrival.Z,0),"old saved arrival reproduces house collision trap");
Check(arrival.MoveToHouseEntrance()&&arrival.CanWalk(arrival.X,arrival.Z,0),"old trapped save gets a clear arrival without deleting objects");
Check(Math.Abs(Math.Sqrt(CylinderWorld.CameraHeight*CylinderWorld.CameraHeight+CylinderWorld.CameraBack*CylinderWorld.CameraBack)-9)<.001,"camera eye distance reduced ten percent from ten to nine metres");
Check(RenderView.Sphere(0,1,0,1,0,0)&&!RenderView.Sphere(100,1,0,1,0,0)&&!RenderView.Sphere(0,1,100,1,0,0),"view keeps player and rejects side/behind camera");
for(int edge=0;edge<4;edge++)for(int depth=2;depth<CylinderWorld.Far-2;depth+=3)
{
    float x=0,up=0;
    if(edge<2)x=(edge==0?-1:1)*(depth*RenderView.TanX+.5f);
    else up=(edge==2?-1:1)*(depth*RenderView.TanY+.5f);
    float y=CylinderWorld.CameraHeight+depth*RenderView.ForwardY-up*RenderView.ForwardZ;
    float z=CylinderWorld.CameraBack+depth*RenderView.ForwardZ+up*RenderView.ForwardY;
    Check(RenderView.Sphere(x,y,z,1,0,0),"sphere whose center is outside a viewport edge retains its visible portion");
}
var edgeBuilding=CylinderWorld.Surface(-25,0,2);
Check(RenderView.Sphere(0,edgeBuilding.Y,edgeBuilding.Z,6,0,0)&&RenderView.AboveHorizon(edgeBuilding.Y,edgeBuilding.Z,6,0),"building outside old 22-unit square can remain partly visible");
var nearHorizon=CylinderWorld.Surface(0,0,1);
Check(RenderView.AboveHorizon(nearHorizon.Y,nearHorizon.Z,1,0),"nearby player stays above horizon");
var behindHorizon=CylinderWorld.Surface(-50,0,1);
Check(RenderView.Sphere(0,behindHorizon.Y,behindHorizon.Z,1,0,0)&&!RenderView.AboveHorizon(behindHorizon.Y,behindHorizon.Z,1,0),"horizon rejects objects still inside camera frustum");
var tallHorizon=CylinderWorld.Surface(-30,0,8);
Check(RenderView.AboveHorizon(tallHorizon.Y,tallHorizon.Z,1,0),"tall silhouette peeking over horizon remains visible");
var shiftedHorizon=CylinderWorld.Surface(50,100,1);
Check(!RenderView.AboveHorizon(shiftedHorizon.Y,shiftedHorizon.Z,1,100),"horizon follows player world position");
bool RayBlocked(double y,double z)
{
    double ey=CylinderWorld.Radius+CylinderWorld.CameraHeight,ez=CylinderWorld.CameraBack;
    double dy=y+CylinderWorld.Radius-ey,dz=z-ez,a=dy*dy+dz*dz,b=2*(ey*dy+ez*dz),c=ey*ey+ez*ez-CylinderWorld.Radius*CylinderWorld.Radius;
    double discriminant=b*b-4*a*c;if(a==0||discriminant<=0)return false;
    double entry=(-b-Math.Sqrt(discriminant))/(2*a);return entry>=0&&entry<1-.000001;
}
int occludedSpheres=0,visibleSpheres=0;
for(int arc=-100;arc<=100;arc+=2)for(int height=0;height<=12;height+=2)foreach(float radius in new[]{0f,.5f,1.5f,4f})
{
    var center=CylinderWorld.Surface(arc,0,height);
    if(RenderView.AboveHorizon(center.Y,center.Z,radius,0)){visibleSpheres++;continue;}
    occludedSpheres++;
    for(int sample=0;sample<32;sample++)
    {
        double angle=sample*Math.PI/16;
        Check(RayBlocked(center.Y+Math.Cos(angle)*radius,center.Z+Math.Sin(angle)*radius),"culled sphere has no visible silhouette sample through cylinder");
    }
}
Check(occludedSpheres>500&&visibleSpheres>500,"horizon sweep exercises rejection and conservative retention");
for(int offset=0;offset<16;offset++)
{
    float fx=100+offset*.137f,fz=100+offset*.113f;int visible=0;
    int minX=(int)fx/2*2-CylinderWorld.ViewHalfWidth,minZ=(int)fz/2*2-TerrainTiles.GroundRowsAhead;
    for(int row=0;row<TerrainTiles.GroundRows;row++)
    {
        int z=minZ+row,half=TerrainTiles.RowHalfWidth(row);
        var a=CylinderWorld.Surface(z,fz);var b=CylinderWorld.Surface(z+1,fz);
        if(!RenderView.SurfaceFront(z,fz)&&!RenderView.SurfaceFront(z+1,fz))continue;
        for(int x=minX+CylinderWorld.ViewHalfWidth-half;x<minX+CylinderWorld.ViewHalfWidth+half;x++)
            if(RenderView.Sphere(x+.5f,(a.Y+b.Y)*.5f,(a.Z+b.Z)*.5f,TerrainTiles.GroundVisibilityRadius,fx,fz))visible+=6;
    }
    Check(visible>0&&visible<=8190,"closer curved terrain fits bounded mesh across sub-cell camera motion");
}
byte[] Package(int format,int width,int height,byte[] payload)
{
    var package=new byte[32+payload.Length];int[] header={0x58544e44,1,format,width,height,payload.Length,0,0};
    for(int i=0;i<8;i++)for(int b=0;b<4;b++)package[i*4+b]=(byte)(header[i]>>(8*b));
    payload.CopyTo(package,32);TexturePixels.Validate(package);return package;
}
var colors=new byte[8*8*4];for(int i=0;i<64;i++){colors[i*4]=(byte)(i*3);colors[i*4+1]=(byte)(255-i*2);colors[i*4+2]=(byte)i;colors[i*4+3]=(byte)(i%2==0?255:128);}
foreach(int format in new[]{3,4,5,6})
{
    var package=Package(format,8,8,TexturePixels.Encode(colors,8,8,format));var decoded=new byte[256];TexturePixels.Resample(package,0,0,8,8,decoded,8,8);
    for(int i=0;i<64;i++)
    {
        Check(Math.Abs(decoded[i*4]-colors[i*4])<=17,"native format red/swizzle roundtrip");
        if(format!=3)Check(Math.Abs(decoded[i*4+1]-colors[i*4+1])<=17&&Math.Abs(decoded[i*4+2]-colors[i*4+2])<=17,"native format green/blue roundtrip");
        Check(format==4||Math.Abs(decoded[i*4+3]-colors[i*4+3])<=24,"native format alpha roundtrip");
    }
}
foreach(int format in new[]{0,1,2,14})
{
    var payload=new byte[32];Array.Fill(payload,(byte)255);var package=Package(format,8,format==1||format==2?4:8,payload);
    Check(TexturePixels.Pixel(package,0,0)==(format==14?0:0xffffffff),"packed intensity/CMPR transparent selector fixture");
}
var transparent=new byte[4*4*4];for(int i=0;i<16;i++){transparent[i*4+2]=255;if(i%2==0){transparent[i*4]=255;transparent[i*4+2]=0;transparent[i*4+3]=255;}}
var alphaPackage=Package(6,4,4,TexturePixels.Encode(transparent,4,4,6));var filtered=new byte[4];TexturePixels.Resample(alphaPackage,0,0,4,4,filtered,1,1);
Check(filtered[0]==255&&filtered[2]==0&&filtered[3]==127,"resampling avoids transparent colour fringes");
Check(TexturePixels.FitWidth(1024,256)==256&&TexturePixels.FitHeight(1024,256)==64,"resampling preserves aspect ratio");
foreach(var resolution in new[]{TextureResolution.Size128,TextureResolution.Size256,TextureResolution.Size512,TextureResolution.Size1024}){
    int edge=TexturePolicy.Edge(resolution);
    Check(TexturePixels.FitWidth(1024,512,resolution)==edge&&TexturePixels.FitHeight(1024,512,resolution)==edge/2,"selected texture limit preserves aspect");
    Check(TexturePixels.FitWidth(64,32,resolution)==64,"texture policy never upscales");
    Check(TexturePolicy.ModelPath("fox",resolution)=="dvd:/fox"+(edge==256?"":"-tex"+edge)+".dndmodel","load selects original-derived variant");
}
bool invalidResolution=false;try{TexturePolicy.Edge((TextureResolution)192);}catch(ArgumentOutOfRangeException){invalidResolution=true;}Check(invalidResolution,"invalid texture resolution rejected");
uint identity=TexturePixels.Hash(alphaPackage);alphaPackage[32]^=1;Check(identity!=TexturePixels.Hash(alphaPackage),"source fingerprint notices changed original bytes");
bool badTexture=false;try{TexturePixels.Validate(new byte[32]);}catch(InvalidOperationException){badTexture=true;}Check(badTexture,"invalid texture rejected before decoding");
for(int kind=0;kind<3;kind++){
    var tree=LowPolyTree.Build(kind);Check(tree.Triangles>24&&tree.Triangles<=LowPolyTree.TriangleBudget,"generated tree triangle budget");float minY=10,maxY=-10;var parts=new int[4];
    for(int t=0;t<tree.Triangles;t++){
        Check(tree.Parts[t]<4,"tree material range");parts[tree.Parts[t]]++;int at=t*9;
        float ax=tree.Positions[at+3]-tree.Positions[at],ay=tree.Positions[at+4]-tree.Positions[at+1],az=tree.Positions[at+5]-tree.Positions[at+2],bx=tree.Positions[at+6]-tree.Positions[at],by=tree.Positions[at+7]-tree.Positions[at+1],bz=tree.Positions[at+8]-tree.Positions[at+2];
        float nx=ay*bz-az*by,ny=az*bx-ax*bz,nz=ax*by-ay*bx;Check(nx*nx+ny*ny+nz*nz>1e-10f,"generated faces are nondegenerate");
        for(int v=0;v<3;v++){float y=tree.Positions[at+v*3+1];minY=Math.Min(minY,y);maxY=Math.Max(maxY,y);}
    }
    Check(minY==0&&maxY>.9f&&maxY<=1.01f,"tree ground pivot and normalized height");for(int part=0;part<4;part++)Check(parts[part]>0,"every generated material has geometry");
    Console.WriteLine("Generated tree "+kind+": "+tree.Triangles+" triangles");
}
var floorPixels=new byte[128*128*4];
for(int y=0;y<128;y++)for(int x=0;x<128;x++){int at=(y*128+x)*4;floorPixels[at]=(byte)(x<64?255:0);floorPixels[at+2]=(byte)(x>=64?255:0);floorPixels[at+3]=255;}
var floorSource=Package(4,128,128,TexturePixels.Encode(floorPixels,128,128,4));uint floorHash=TexturePixels.Hash(floorSource);var optimized=FloorTexture.Prepare(floorSource);
Check(TexturePixels.Width(optimized)==128&&TexturePixels.Height(optimized)==128&&TexturePixels.Int(optimized,8)==14&&TexturePixels.Int(optimized,24)==2&&optimized.Length==32+10240,"floor preserves bounded source resolution, compresses and builds base plus one native mip");
Check(TexturePixels.Pixel(optimized,0,0)==0xff0000ff&&TexturePixels.Pixel(optimized,127,0)==0x0000ffff&&TexturePixels.Pixel(optimized,127,127)==0x0000ffff,"floor CMPR tile/subblock addressing and opaque alpha");
Check(TexturePixels.Hash(floorSource)==floorHash,"load-time optimization preserves original fingerprint");
var halfFloor=new byte[optimized.Length-32-8192];Array.Copy(optimized,32+8192,halfFloor,0,halfFloor.Length);
var halfFloorPackage=Package(14,64,64,halfFloor);
Check(TexturePixels.Pixel(halfFloorPackage,0,0)==0xff0000ff&&TexturePixels.Pixel(halfFloorPackage,63,0)==0x0000ffff,"second floor level has valid half-size GX texels");
int[] palette={0xff0000,0x00ff00,0x0000ff,0xffff00,0x00ffff,0xff00ff,0xffffff,0};
for(int y=0;y<128;y++)for(int x=0;x<128;x++){int at=(y*128+x)*4,c=palette[(x/2+y/2*4)%8];floorPixels[at]=(byte)(c>>16);floorPixels[at+1]=(byte)(c>>8);floorPixels[at+2]=(byte)c;}
optimized=FloorTexture.Prepare(Package(4,128,128,TexturePixels.Encode(floorPixels,128,128,4)));
Check(TexturePixels.Int(optimized,8)==4&&TexturePixels.Int(optimized,24)==2&&optimized.Length==32+40960,"high-error CMPR falls back to two RGB565 levels");
var solidFloor=FloorTexture.Prepare(Package(4,4,4,TexturePixels.Encode(new byte[4*4*4],4,4,4)));
Check(TexturePixels.Int(solidFloor,24)==1&&solidFloor.Length==32+32,"minimum-size solid floor keeps its one valid hardware level");
var floorMeta=new byte[FloorStrips.Records];int[] floorHeader={0x32525441,1,13,13,FloorTexture.Maximum};
void SetFloor(int at,int value){for(int b=0;b<4;b++)floorMeta[at+b]=(byte)(value>>(b*8));}
for(int i=0;i<floorHeader.Length;i++)SetFloor(i*4,floorHeader[i]);
for(int s=0;s<13;s++){SetFloor(20+s*20,1);SetFloor(24+s*20,s);for(int i=1;i<4;i++)SetFloor(24+s*20+i*4,-1);}FloorStrips.Validate(floorMeta);
var floorGame=new GameState(3);Array.Fill(floorGame.Town.Tiles,(byte)0);floorGame.X=100.25f;floorGame.Z=100.5f;var strips=new FloorStrips();strips.Prepare(floorGame,0,floorMeta);
Check(strips.Count>0&&strips.Count<100,"uniform repeating ground merges cells into bounded strips");
for(int n=0;n<strips.Count;n++){Check(strips.X1[n]>strips.X0[n]&&strips.Z1[n]==strips.Z0[n]+1&&strips.Material[n]==0,"merged strips retain curved rows and material");}
int fineCells=0,coveredCells=0;for(int n=0;n<strips.Count;n++)coveredCells+=strips.X1[n]-strips.X0[n];
int floorMinZ=(int)floorGame.Z/2*2-TerrainTiles.GroundRowsAhead,floorCenterX=(int)floorGame.X/2*2;
for(int row=0;row<TerrainTiles.GroundRows;row++){
    int z=floorMinZ+row,half=TerrainTiles.RowHalfWidth(row);var a=CylinderWorld.Surface(z,floorGame.Z);var b=CylinderWorld.Surface(z+1,floorGame.Z);
    if(!RenderView.SurfaceFront(z,floorGame.Z)&&!RenderView.SurfaceFront(z+1,floorGame.Z))continue;
    for(int x=floorCenterX-half;x<floorCenterX+half;x++)if(RenderView.Sphere(x+.5f,(a.Y+b.Y)*.5f,(a.Z+b.Z)*.5f,TerrainTiles.GroundVisibilityRadius,floorGame.X,floorGame.Z)){fineCells++;int hits=0;for(int n=0;n<strips.Count;n++)if(strips.Z0[n]==z&&x>=strips.X0[n]&&x<strips.X1[n])hits++;Check(hits==1,"merged floor coverage has no holes or overlapping cells");}
}
Check(fineCells==coveredCells,"merged floor exactly covers the visible fine-cell set");strips.Prepare(floorGame,1,floorMeta);Check(strips.Count==1&&strips.Material[0]==7&&strips.X1[0]==4&&strips.Z1[0]==4,"indoor repeating floor is one flat quad");
Console.WriteLine(checks+" gameplay/render/texture checks passed");
Console.WriteLine("Textured cylinder: "+TerrainTiles.GroundVertexCount/3+" triangles");
