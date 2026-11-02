using AnimalTown;

int checks=0;
void Check(bool value,string message){checks++;if(!value)throw new Exception(message);}
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
Check(Town.ScreenTiles==20&&Town.Width==Town.Height&&Town.Width==20*Town.ScreenTileSize,"20 x 20 square screen tiles");
double focusDepth=273/Math.Sqrt(337);
double focusWidth=2*focusDepth*Math.Tan(CylinderWorld.CameraFieldOfView*Math.PI/360)*4/3;
Check(Math.Abs(focusWidth-Town.ScreenTileSize)<.05,"each square world tile spans one viewport at the player");
Check(CylinderWorld.ViewHalfWidth>70*Math.Tan(CylinderWorld.CameraFieldOfView*Math.PI/360)*4/3,"cylinder extends past far-plane viewport sides");
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
Console.WriteLine(checks+" gameplay checks passed");
