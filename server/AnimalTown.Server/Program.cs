using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.RateLimiting;
using AnimalTown;
using Npgsql;

var builder=WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(k=>k.Limits.MaxRequestBodySize=128*1024);
builder.Services.AddRateLimiter(o=>
{
    o.RejectionStatusCode=429;
    o.GlobalLimiter=PartitionedRateLimiter.Create<HttpContext,string>(c=>RateLimitPartition.GetFixedWindowLimiter(c.Connection.RemoteIpAddress?.ToString()??"unknown",_=>new FixedWindowRateLimiterOptions{PermitLimit=90,Window=TimeSpan.FromMinutes(1),QueueLimit=0}));
});
await using var data=NpgsqlDataSource.Create(builder.Configuration.GetConnectionString("Game")??throw new InvalidOperationException("Set ConnectionStrings__Game"));
var db=new GameDatabase(data);await db.Initialize();var app=builder.Build();app.UseRateLimiter();
app.Use(async(context,next)=>
{
    try
    {
        if(context.Request.Path.StartsWithSegments("/v1")&&!context.Request.Path.StartsWithSegments("/v1/auth"))
        {
            string bearer=context.Request.Headers.Authorization.ToString();
            if(!bearer.StartsWith("Bearer ",StringComparison.Ordinal)||bearer.Length>200){context.Response.StatusCode=401;return;}
            Guid? player=await db.Authenticate(bearer[7..]);if(player==null){context.Response.StatusCode=401;return;}context.Items["player"]=player.Value;
        }
        await next();
    }
    catch(GameError e){context.Response.StatusCode=e.Status;await context.Response.WriteAsJsonAsync(new{error=e.Message});}
    catch(PostgresException e) when(e.SqlState=="23505"){context.Response.StatusCode=409;await context.Response.WriteAsJsonAsync(new{error="conflict"});}
});
Guid Me(HttpContext c)=>(Guid)c.Items["player"]!;
app.MapGet("/health",()=>new{service="AnimalTown.Server",protocol=1,catalog=Catalog.Version});
app.MapPost("/v1/auth/register",async(Credentials c)=>await db.Register(c));
app.MapPost("/v1/auth/token",async(Credentials c)=>await db.Login(c));
app.MapGet("/v1/me",(Delegate)(async(HttpContext c)=>await db.Profile(Me(c))));
app.MapGet("/v1/inventory",(Delegate)(async(HttpContext c)=>await db.Profile(Me(c))));
app.MapPost("/v1/inventory/ops",async(HttpContext c,InventoryOp op)=>await db.Apply(Me(c),op));
app.MapGet("/v1/towns/{id:guid}",async(HttpContext c,Guid id)=>
{
    if(Me(c)!=id)throw new GameError(403,"owner-only");var state=await db.State(id);c.Response.Headers.ETag="\""+state.Revision+"\"";return Results.Bytes(SaveCodec.Encode(state.State),"application/octet-stream");
});
app.MapPut("/v1/towns/{id:guid}",(HttpContext c,Guid id)=>
{
    if(Me(c)!=id)throw new GameError(403,"owner-only");
    // Never accept an edited offline save as an authoritative online wallet.
    throw new GameError(409,"offline-import-not-yet-supported");
});
app.MapGet("/v1/friends",(Delegate)(async(HttpContext c)=>await db.FriendList(Me(c))));
app.MapPost("/v1/friends/{id:guid}",async(HttpContext c,Guid id)=>await db.Friend(Me(c),id,true));
app.MapDelete("/v1/friends/{id:guid}",async(HttpContext c,Guid id)=>await db.Friend(Me(c),id,false));
app.MapPost("/v1/blocks/{id:guid}",async(HttpContext c,Guid id)=>await db.Block(Me(c),id,true));
app.MapDelete("/v1/blocks/{id:guid}",async(HttpContext c,Guid id)=>await db.Block(Me(c),id,false));
app.MapPost("/v1/gate",(Delegate)(async(HttpContext c)=>await db.OpenGate(Me(c))));
app.MapPost("/v1/visits",async(HttpContext c,VisitRequest v)=>await db.Visit(Me(c),v.Host));
app.MapPost("/v1/visits/{id:guid}/end",async(HttpContext c,Guid id)=>await db.EndVisit(Me(c),id));
app.MapPost("/v1/shops/listings",async(HttpContext c,ListingRequest l)=>await db.List(Me(c),l));
app.MapPost("/v1/shops/listings/{id:guid}/purchase",async(HttpContext c,Guid id)=>await db.Purchase(Me(c),id));
app.MapDelete("/v1/shops/listings/{id:guid}",async(HttpContext c,Guid id)=>await db.CancelListing(Me(c),id));
app.MapGet("/v1/grants",(Delegate)(async(HttpContext c)=>await db.Grants(Me(c))));
app.MapPost("/v1/grants/{id:guid}/claim",async(HttpContext c,Guid id)=>await db.Claim(Me(c),id));
app.MapPost("/v1/reports",async(HttpContext c,ReportRequest r)=>await db.Report(Me(c),r));
app.Run();

record Credentials(string Name,string Password);
record InventoryOp(Guid Id,string Kind,int Amount=0,int Slot=0,int Item=0);
record VisitRequest(Guid Host);
record ListingRequest(Guid Id,int Slot,int Price);
record ReportRequest(Guid Reported,string[] Lines);
sealed class GameError(int status,string message):Exception(message){public int Status=>status;}

sealed class GameDatabase(NpgsqlDataSource source)
{
    public async Task Initialize()
    {await using var c=await source.OpenConnectionAsync();await using var command=new NpgsqlCommand(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,"Schema.sql")),c);await command.ExecuteNonQueryAsync();}
    static NpgsqlCommand Cmd(NpgsqlConnection c,NpgsqlTransaction? t,string sql,params object[] values)
    {var cmd=new NpgsqlCommand(sql,c,t);foreach(object v in values)cmd.Parameters.Add(new NpgsqlParameter{Value=v});return cmd;}
    static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    static byte[] Password(string text,byte[] salt)=>Rfc2898DeriveBytes.Pbkdf2(text,salt,210000,HashAlgorithmName.SHA256,32);
    static int Day=>(int)(DateTimeOffset.UtcNow.ToUnixTimeSeconds()/86400);
    public async Task<object> Register(Credentials request)
    {
        if(string.IsNullOrWhiteSpace(request.Name)||request.Name.Length>16||request.Password==null||request.Password.Length<12||request.Password.Length>128)throw new GameError(400,"name-or-password-invalid");
        Guid id=Guid.NewGuid();var state=new GameState(RandomNumberGenerator.GetInt32(1,int.MaxValue)){Name=request.Name};state.Rollover(Day,DateTime.UtcNow.Year*12+DateTime.UtcNow.Month);
        byte[] salt=RandomNumberGenerator.GetBytes(16);await using var c=await source.OpenConnectionAsync();
        await using(var cmd=Cmd(c,null,"INSERT INTO players(id,name,salt,password,state) VALUES($1,$2,$3,$4,$5)",id,request.Name,salt,Password(request.Password,salt),SaveCodec.Encode(state)))await cmd.ExecuteNonQueryAsync();
        return new{id,token=await Token(c,id)};
    }
    async Task<string> Token(NpgsqlConnection c,Guid id)
    {string token=Convert.ToHexString(RandomNumberGenerator.GetBytes(32));await using var cmd=Cmd(c,null,"INSERT INTO sessions VALUES($1,$2,now()+interval '7 days')",Hash(token),id);await cmd.ExecuteNonQueryAsync();return token;}
    public async Task<object> Login(Credentials request)
    {
        if(request.Name==null||request.Password==null||request.Name.Length>16||request.Password.Length>128)throw new GameError(401,"invalid-login");await using var c=await source.OpenConnectionAsync();Guid id;
        await using(var cmd=Cmd(c,null,"SELECT id,salt,password FROM players WHERE name=$1",request.Name))await using(var r=await cmd.ExecuteReaderAsync())
        {if(!await r.ReadAsync())throw new GameError(401,"invalid-login");id=r.GetGuid(0);if(!CryptographicOperations.FixedTimeEquals(Password(request.Password,r.GetFieldValue<byte[]>(1)),r.GetFieldValue<byte[]>(2)))throw new GameError(401,"invalid-login");}
        return new{id,token=await Token(c,id)};
    }
    public async Task<Guid?> Authenticate(string token)
    {await using var c=await source.OpenConnectionAsync();await using var cmd=Cmd(c,null,"SELECT player FROM sessions WHERE token_hash=$1 AND expires>now()",Hash(token));return await cmd.ExecuteScalarAsync() is Guid id?id:null;}
    async Task<(GameState State,long Revision)> Load(NpgsqlConnection c,NpgsqlTransaction? t,Guid id,bool locked)
    {await using var cmd=Cmd(c,t,"SELECT state,revision FROM players WHERE id=$1"+(locked?" FOR UPDATE":""),id);await using var r=await cmd.ExecuteReaderAsync();if(!await r.ReadAsync())throw new GameError(404,"player-not-found");return(SaveCodec.Decode(r.GetFieldValue<byte[]>(0)),r.GetInt64(1));}
    async Task Save(NpgsqlConnection c,NpgsqlTransaction t,Guid id,GameState state)
    {await using var cmd=Cmd(c,t,"UPDATE players SET state=$2,revision=revision+1 WHERE id=$1",id,SaveCodec.Encode(state));await cmd.ExecuteNonQueryAsync();}
    public async Task<(GameState State,long Revision)> State(Guid id)
    {await using var c=await source.OpenConnectionAsync();return await Load(c,null,id,false);}
    public async Task<object> Profile(Guid id)
    {var s=await State(id);return new{id,name=s.State.Name,species=s.State.Species,pockets=s.State.Pockets,capacity=s.State.Capacity,wallet=s.State.Wallet,savings=s.State.Savings,debt=s.State.Debt,revision=s.Revision};}
    public async Task<object> Apply(Guid id,InventoryOp op)
    {
        if(op.Id==Guid.Empty)throw new GameError(400,"operation-id-required");string fingerprint=Hash(JsonSerializer.Serialize(op));await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();var loaded=await Load(c,t,id,true);
        await using(var cmd=Cmd(c,t,"SELECT fingerprint,result::text FROM operations WHERE player=$1 AND id=$2",id,op.Id))await using(var r=await cmd.ExecuteReaderAsync())if(await r.ReadAsync())
        {if(r.GetString(0)!=fingerprint)throw new GameError(409,"idempotency-key-reused");return JsonSerializer.Deserialize<JsonElement>(r.GetString(1));}
        var s=loaded.State;s.Rollover(Day,DateTime.UtcNow.Year*12+DateTime.UtcNow.Month);
        bool ok=op.Kind switch{"buy"=>s.Buy(op.Slot),"sell"=>s.Sell(op.Slot),"deposit"=>s.Deposit(op.Amount),"withdraw"=>s.Withdraw(op.Amount),"repay"=>s.Repay(op.Amount),"expand-house"=>s.Expand(false,Day),"expand-garden"=>s.Expand(true,Day),"order"=>s.Order(op.Item),_=>false};
        if(op.Kind=="upgrade-backpack")ok=s.UpgradeBackpack();
        if(!ok)throw new GameError(409,"operation-rejected");await Save(c,t,id,s);var result=new{ok=true,revision=loaded.Revision+1,wallet=s.Wallet,savings=s.Savings,debt=s.Debt,capacity=s.Capacity};
        await using(var cmd=Cmd(c,t,"INSERT INTO operations VALUES($1,$2,$3,$4::jsonb)",id,op.Id,fingerprint,JsonSerializer.Serialize(result)))await cmd.ExecuteNonQueryAsync();await t.CommitAsync();return result;
    }
    public async Task<object> FriendList(Guid id)
    {await using var c=await source.OpenConnectionAsync();await using var cmd=Cmd(c,null,"SELECT f.recipient,p.name,EXISTS(SELECT 1 FROM friends r WHERE r.sender=f.recipient AND r.recipient=$1) FROM friends f JOIN players p ON p.id=f.recipient WHERE f.sender=$1",id);await using var r=await cmd.ExecuteReaderAsync();var result=new List<object>();while(await r.ReadAsync())result.Add(new{id=r.GetGuid(0),name=r.GetString(1),mutual=r.GetBoolean(2)});return result;}
    public async Task<object> Friend(Guid me,Guid other,bool add)
    {if(me==other)throw new GameError(400,"self-friend");await using var c=await source.OpenConnectionAsync();await using var cmd=Cmd(c,null,add?"INSERT INTO friends VALUES($1,$2) ON CONFLICT DO NOTHING":"DELETE FROM friends WHERE sender=$1 AND recipient=$2",me,other);await cmd.ExecuteNonQueryAsync();return new{ok=true};}
    public async Task<object> Block(Guid me,Guid other,bool add)
    {
        if(me==other)throw new GameError(400,"self-block");await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();await using(var cmd=Cmd(c,t,add?"INSERT INTO blocks VALUES($1,$2) ON CONFLICT DO NOTHING":"DELETE FROM blocks WHERE blocker=$1 AND blocked=$2",me,other))await cmd.ExecuteNonQueryAsync();
        if(add){await using(var cmd=Cmd(c,t,"UPDATE visits SET ended=true WHERE (host=$1 AND guest=$2) OR (host=$2 AND guest=$1)",me,other))await cmd.ExecuteNonQueryAsync();await using(var cmd=Cmd(c,t,"DELETE FROM tents WHERE (host=$1 AND guest=$2) OR (host=$2 AND guest=$1)",me,other))await cmd.ExecuteNonQueryAsync();}await t.CommitAsync();return new{ok=true};
    }
    public async Task<object> OpenGate(Guid me)
    {await using var c=await source.OpenConnectionAsync();await using var cmd=Cmd(c,null,"INSERT INTO gates VALUES($1,now()+interval '5 minutes') ON CONFLICT(host) DO UPDATE SET expires=excluded.expires",me);await cmd.ExecuteNonQueryAsync();return new{ok=true,leaseSeconds=300};}
    async Task CheckVisit(NpgsqlConnection c,NpgsqlTransaction t,Guid guest,Guid host)
    {await using var cmd=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM visits WHERE host=$1 AND guest=$2 AND NOT ended AND expires>now()) AND NOT EXISTS(SELECT 1 FROM blocks WHERE (blocker=$1 AND blocked=$2) OR (blocker=$2 AND blocked=$1))",host,guest);if(await cmd.ExecuteScalarAsync() is not true)throw new GameError(403,"active-visit-required");}
    public async Task<object> Visit(Guid guest,Guid host)
    {
        if(host==guest)throw new GameError(400,"own-town");await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();await Load(c,t,host,true);
        await using(var cmd=Cmd(c,t,"SELECT EXISTS(SELECT 1 FROM friends WHERE sender=$1 AND recipient=$2) AND EXISTS(SELECT 1 FROM friends WHERE sender=$2 AND recipient=$1) AND NOT EXISTS(SELECT 1 FROM blocks WHERE (blocker=$1 AND blocked=$2) OR (blocker=$2 AND blocked=$1)) AND EXISTS(SELECT 1 FROM gates WHERE host=$1 AND expires>now())",host,guest))if(await cmd.ExecuteScalarAsync() is not true)throw new GameError(403,"gate-or-friendship-required");
        await using(var cmd=Cmd(c,t,"UPDATE visits SET ended=true WHERE expires<=now()"))await cmd.ExecuteNonQueryAsync();
        await using(var cmd=Cmd(c,t,"SELECT count(*) FROM visits WHERE host=$1 AND NOT ended",host))if((long)(await cmd.ExecuteScalarAsync())!>=3)throw new GameError(409,"town-full");
        Guid id=Guid.NewGuid();await using(var cmd=Cmd(c,t,"INSERT INTO visits VALUES($1,$2,$3,now()+interval '2 minutes',false)",id,host,guest))await cmd.ExecuteNonQueryAsync();await t.CommitAsync();return new{id,host,guest,connectWithinSeconds=120,status="reserved"};
    }
    public async Task<object> EndVisit(Guid player,Guid id)
    {await using var c=await source.OpenConnectionAsync();await using var cmd=Cmd(c,null,"UPDATE visits SET ended=true WHERE id=$1 AND (host=$2 OR guest=$2)",id,player);if(await cmd.ExecuteNonQueryAsync()!=1)throw new GameError(404,"visit-not-found");return new{ok=true};}
    public async Task<object> List(Guid seller,ListingRequest l)
    {
        if(l.Id==Guid.Empty||l.Slot<0||l.Slot>=GameState.MaximumPockets||l.Price<1||l.Price>999999)throw new GameError(400,"invalid-listing");await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();var s=(await Load(c,t,seller,true)).State;
        if(l.Slot>=s.Capacity)throw new GameError(409,"backpack-slot-locked");
        await using(var cmd=Cmd(c,t,"SELECT seller FROM listings WHERE id=$1",l.Id))if(await cmd.ExecuteScalarAsync() is Guid existing){if(existing!=seller)throw new GameError(409,"listing-id-used");return new{id=l.Id};}
        int item=s.Pockets[l.Slot];if(!Catalog.Has(item,ItemFlags.Gift)||item==18||item==19)throw new GameError(409,"item-not-tradable");
        await using(var cmd=Cmd(c,t,"SELECT count(*) FROM listings WHERE seller=$1 AND buyer IS NULL AND NOT cancelled",seller))if((long)(await cmd.ExecuteScalarAsync())!>=4)throw new GameError(409,"stall-full");
        s.Pockets[l.Slot]=0;await Save(c,t,seller,s);await using(var cmd=Cmd(c,t,"INSERT INTO listings(id,seller,item,price) VALUES($1,$2,$3,$4)",l.Id,seller,item,l.Price))await cmd.ExecuteNonQueryAsync();await t.CommitAsync();return new{id=l.Id};
    }
    public async Task<object> Purchase(Guid buyer,Guid listing)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();Guid seller;int item,price;Guid? bought;
        await using(var cmd=Cmd(c,t,"SELECT seller,item,price,buyer,cancelled FROM listings WHERE id=$1 FOR UPDATE",listing))await using(var r=await cmd.ExecuteReaderAsync())
        {if(!await r.ReadAsync())throw new GameError(404,"listing-not-found");seller=r.GetGuid(0);item=r.GetInt32(1);price=r.GetInt32(2);bought=r.IsDBNull(3)?null:r.GetGuid(3);if(r.GetBoolean(4))throw new GameError(409,"cancelled");}
        if(bought==buyer)return new{ok=true};if(bought!=null)throw new GameError(409,"sold-out");if(seller==buyer)throw new GameError(400,"own-listing");await CheckVisit(c,t,buyer,seller);
        Guid first=buyer.CompareTo(seller)<0?buyer:seller,second=first==buyer?seller:buyer;var a=(await Load(c,t,first,true)).State;var b=(await Load(c,t,second,true)).State;var bs=first==buyer?a:b;var ss=first==seller?a:b;
        if(bs.Wallet<price||ss.Savings>long.MaxValue-price)throw new GameError(409,"insufficient-funds-or-overflow");bs.Wallet-=price;ss.Savings+=price;await Save(c,t,buyer,bs);await Save(c,t,seller,ss);
        await using(var cmd=Cmd(c,t,"UPDATE listings SET buyer=$2 WHERE id=$1",listing,buyer))await cmd.ExecuteNonQueryAsync();
        await using(var cmd=Cmd(c,t,"INSERT INTO grants VALUES($1,$2,$3,false,$4)",Guid.NewGuid(),buyer,item,"purchase:"+listing))await cmd.ExecuteNonQueryAsync();await t.CommitAsync();return new{ok=true};
    }
    public async Task<object> CancelListing(Guid seller,Guid id)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();int item;
        await using(var cmd=Cmd(c,t,"SELECT item FROM listings WHERE id=$1 AND seller=$2 AND buyer IS NULL AND NOT cancelled FOR UPDATE",id,seller))item=await cmd.ExecuteScalarAsync() is int i?i:throw new GameError(404,"listing-unavailable");
        await using(var cmd=Cmd(c,t,"UPDATE listings SET cancelled=true WHERE id=$1",id))await cmd.ExecuteNonQueryAsync();
        await using(var cmd=Cmd(c,t,"INSERT INTO grants VALUES($1,$2,$3,false,$4)",Guid.NewGuid(),seller,item,"cancel:"+id))await cmd.ExecuteNonQueryAsync();await t.CommitAsync();return new{ok=true};
    }
    public async Task<object> Grants(Guid player)
    {await using var c=await source.OpenConnectionAsync();await using var cmd=Cmd(c,null,"SELECT id,item FROM grants WHERE player=$1 AND NOT claimed",player);await using var r=await cmd.ExecuteReaderAsync();var list=new List<object>();while(await r.ReadAsync())list.Add(new{id=r.GetGuid(0),item=r.GetInt32(1)});return list;}
    public async Task<object> Claim(Guid player,Guid id)
    {
        await using var c=await source.OpenConnectionAsync();await using var t=await c.BeginTransactionAsync();var s=(await Load(c,t,player,true)).State;int item;
        await using(var cmd=Cmd(c,t,"SELECT item,claimed FROM grants WHERE id=$1 AND player=$2 FOR UPDATE",id,player))await using(var r=await cmd.ExecuteReaderAsync()){if(!await r.ReadAsync())throw new GameError(404,"grant-not-found");if(r.GetBoolean(1))return new{ok=true};item=r.GetInt32(0);}
        if(!s.Receive(item))throw new GameError(409,"pockets-full");await Save(c,t,player,s);await using(var cmd=Cmd(c,t,"UPDATE grants SET claimed=true WHERE id=$1",id))await cmd.ExecuteNonQueryAsync();await t.CommitAsync();return new{ok=true};
    }
    public async Task<object> Report(Guid reporter,ReportRequest report)
    {
        if(report.Lines.Length>50||report.Lines.Any(l=>l.Length>160)||report.Reported==reporter)throw new GameError(400,"invalid-report");await using var c=await source.OpenConnectionAsync();await using var cmd=Cmd(c,null,"INSERT INTO reports SELECT $1,$2,$3,$4::jsonb,now() WHERE EXISTS(SELECT 1 FROM visits WHERE (host=$2 AND guest=$3) OR (host=$3 AND guest=$2))",Guid.NewGuid(),reporter,report.Reported,JsonSerializer.Serialize(report.Lines));if(await cmd.ExecuteNonQueryAsync()!=1)throw new GameError(403,"shared-visit-required");return new{ok=true};
    }
}
