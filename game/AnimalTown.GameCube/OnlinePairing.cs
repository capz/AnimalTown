using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown;
using Dolphin.Input;
using Dolphin.Network;
using ConsoleHttp = Dolphin.Network.Http;
using Dolphin.Security;
using Dolphin.Serialization.Json;
using Dolphin.Timing;

// Runs after the town renderer has been disposed, so DHCP, entropy collection
// and TLS handshakes use the loading screen's memory and frame budget.
internal sealed class OnlinePairing
{
    private const string VerifyPath="/link";
    private const string StartPath="/v1/auth/link/start";
    private const string PollPath="/v1/auth/link/poll";
    private const string RenewPath="/v1/auth/device/renew";
    private const int ResponseLimit=1024;
    private const long MicrosecondsPerSecond=1000000;
    private readonly TownSession session;
    private readonly TextPages text=new TextPages(42);
    private readonly string town;
    private string origin="",status="Connecting to AnimalTown...\nB: cancel";
    private string secret="",displayedStatus="",display="";
    private long expires,nextPoll;
    private int interval;
    private bool finished,renewing,relinkOffered,cardWriteFailed;
    private OnlineCredentialStore.Snapshot? remembered;

    private OnlinePairing(TownSession session)
    {
        this.session=session;
        town=TownIdentity(session.State.Town.Jobs.TownId);
    }

    internal static void Run(TownSession session)
    {
        LoadingScreen.Finish();
        TextureCache.TrimUnused();
        AnimalTown.GameCube.NativeTexturePages.Collect();
        var font=UiFont.LoadSystem();
        try{new OnlinePairing(session).Show(font);}
        finally{LoadingScreen.Finish();font.Release();}
    }

    private void Show(UiFont font)
    {
        using var cancel=new CancellationTokenSource();
        using var client=new ConsoleHttp.HttpClient{MaximumResponseBytes=ResponseLimit,MaximumStreamBytes=ResponseLimit,MaximumRedirects=0};
        Task<string?>? request=null;
        bool leaving=false,networkUsed=false;
        try
        {
            byte[]? roots=null;
            try
            {
                origin=ReadOrigin();
                roots=ReadBounded(ContentPaths.Asset("online-roots.pem"),16*1024);
            }
            catch(IOException){Complete("Online service is unavailable on this installation.");}
            catch(FormatException){Complete("The online service address is invalid.");}
            if(roots!=null)
            {
                try{remembered=OnlineCredentialCard.Load(session.State);}
                catch(Exception){Complete("The selected town card could not be checked. Reload your town and retry.");}
                var active=session.OnlineCredentials;
                if(!finished&&active!=null&&remembered!=null&&remembered.Credential.RotationId.Length==0&&
                    active.Origin==origin&&active.Town==town&&active.DeviceId==remembered.Credential.DeviceId&&
                    active.Generation==remembered.Credential.Generation&&
                    active.AccessExpiresAt>Clock.Timestamp+5*MicrosecondsPerSecond)
                {
                    Complete("This GameCube is linked.");
                }
                else if(!finished)
                {
                    session.OnlineCredentials=null;
                    networkUsed=true;
                    request=Begin(client,roots,cancel.Token);
                }
            }
            if(finished&&(roots==null||remembered==null))session.OnlineCredentials=null;

            while(true)
            {
                long now=Clock.Timestamp;
                if(request!=null&&request.IsCompleted)
                {
                    try
                    {
                        string? reply=request.GetAwaiter().GetResult();
                        if(!leaving)
                        {
                            if(reply!=null)ReadReply(reply);
                            else OfferRelink();
                        }
                    }
                    catch(Exception)
                    {
                        if(!leaving)Complete(cardWriteFailed?"Could not save the online link. Check the memory card and retry.":
                            "Could not link. Check the adapter, network and console clock.");
                    }
                    request=null;
                }
                if(leaving&&request==null)return;
                if(!finished&&!leaving&&secret.Length!=0&&request==null)
                {
                    if(now>=expires)Complete("The code expired. Reopen Online for a new code.");
                    else if(now>=nextPoll)request=Post(client,PollPath,"{\"secret\":\""+secret+"\"}",cancel.Token);
                }

                if(displayedStatus!=status){
                    text.Set(status);display=text.Window(0,text.Lines);displayedStatus=status;
                }
                LoadingScreen.Draw(display,font);
                RuntimeOptions.Wait();
                Controller.Update();
                var pad=Controller.GetState(0);
                if(finished&&(pad.A.WasPressed||pad.B.WasPressed))return;
                if(relinkOffered&&pad.A.WasPressed)
                {
                    relinkOffered=false;renewing=false;
                    status="Requesting a new link code...\nB: cancel";
                    request=Post(client,StartPath,"{\"town\":\""+town+"\"}",cancel.Token);
                }
                if(!leaving&&pad.B.WasPressed)
                {
                    leaving=true;
                    status="Leaving online setup...";
                    cancel.Cancel();
                }
            }
        }
        finally
        {
            client.Dispose();
            if(networkUsed){
                TlsConfiguration.Reset();
                // DHCP may still be configuring after cancellation. The SDK
                // will adopt the result on the next pairing attempt.
                if(NetworkRuntime.IsReady)NetworkRuntime.Stop();
            }
        }
    }

    private async Task<string?> Begin(ConsoleHttp.HttpClient client,byte[] roots,CancellationToken cancellation)
    {
        if(!NetworkRuntime.IsReady)await NetworkRuntime.StartAsync(new NetworkOptions(),cancellation);
        cancellation.ThrowIfCancellationRequested();
        var calendar=new TownClock();
        calendar.Update(NativeCalendar.ReadClock());
        long localUnixTime=(10957L+calendar.CivilDay)*86400+calendar.MinuteOfDay*60;
        var prior=remembered?.Credential;
        renewing=prior!=null&&prior.Origin==origin&&prior.Town==town;
        bool newRotation=renewing&&prior!.RotationId.Length==0;
        byte[] entropy=await PlatformEntropy.GetBytesAsync(newRotation?64:48,cancellation);
        string rotation="";
        try{
            cancellation.ThrowIfCancellationRequested();
            if(newRotation)
            {
                // The last 16 bytes are reserved for the operation UUID. Only
                // the independent first 48 bytes are used as the TLS seed.
                rotation=RotationId(entropy);
                var seed=new byte[48];
                Array.Copy(entropy,0,seed,0,seed.Length);
                try{TlsConfiguration.InitializeFromLocalClock(roots,seed,localUnixTime);}
                finally{Array.Clear(seed,0,seed.Length);}
            }
            else TlsConfiguration.InitializeFromLocalClock(roots,entropy,localUnixTime);
        }
        finally{Array.Clear(entropy,0,entropy.Length);}
        if(renewing)
        {
            if(newRotation)Remember(prior!.WithRotation(rotation));
            var pending=remembered!.Credential;
            return await Post(client,RenewPath,
                "{\"deviceId\":\""+pending.DeviceId+"\",\"refreshToken\":\""+pending.RefreshToken+
                "\",\"rotationId\":\""+pending.RotationId+"\"}",cancellation);
        }
        return await Post(client,StartPath,"{\"town\":\""+town+"\"}",cancellation);
    }

    private async Task<string?> Post(ConsoleHttp.HttpClient client,string path,string body,CancellationToken cancellation)
    {
        cancellation.ThrowIfCancellationRequested();
        using var request=new ConsoleHttp.HttpRequestMessage(ConsoleHttp.HttpMethod.Post,origin+path);
        var bytes=new Dolphin.Text.UTF8Encoding(false,true).GetBytes(body);
        try{
            request.Content=new ConsoleHttp.ByteArrayContent(bytes);
            request.Content.Headers.Add("content-type","application/json");
            using var response=await client.SendAsync(request,cancellation);
            if(path==RenewPath&&(int)response.StatusCode==401)return null;
            if(!response.IsSuccessStatusCode)
                throw new IOException("Online pairing request failed.");
            return await response.Content.ReadAsStringAsync();
        }finally{Array.Clear(bytes,0,bytes.Length);}
    }

    private void ReadReply(string json)
    {
        var reply=LinkReply.Read(json);
        long now=Clock.Timestamp;
        if(!renewing&&secret.Length==0)
        {
            if(!OnlineCardCredential.Hex(reply.Secret,0,64)||!Digits(reply.Code,8)||reply.VerificationPath!=VerifyPath||
                reply.Interval<5||reply.Interval>60||reply.ExpiresIn<1||reply.ExpiresIn>300)
                throw new FormatException("Invalid code response.");
            secret=reply.Secret;interval=reply.Interval;
            expires=now+reply.ExpiresIn*MicrosecondsPerSecond;
            nextPoll=now+interval*MicrosecondsPerSecond;
            status="Open "+origin+VerifyPath+"\nCode: "+reply.Code+"\nB: cancel";
            return;
        }
        if(!renewing&&reply.Status=="pending"){nextPoll=now+interval*MicrosecondsPerSecond;return;}
        var prior=remembered?.Credential;
        if(reply.Status!="approved"||reply.Town!=town||!reply.Token.StartsWith("d2_")||!OnlineCardCredential.Hex(reply.Token,3,64)||
            reply.ExpiresIn<0||reply.ExpiresIn>600||
            (renewing?(prior==null||prior.Generation==int.MaxValue||reply.Generation!=prior.Generation+1||
                reply.AccountId!=prior.AccountId||reply.DeviceId!=prior.DeviceId):reply.Generation!=1))
            throw new FormatException("Invalid pairing approval.");
        Remember(new OnlineCardCredential(origin,town,reply.AccountId,reply.DeviceId,reply.RefreshToken,reply.Generation));
        if(reply.ExpiresIn==0)
        {
            Complete("Online link recovered. Reopen Online to renew access.");
            return;
        }
        session.OnlineCredentials=new OnlineDeviceLink(origin,town,reply.AccountId,reply.DeviceId,
            reply.Token,now+reply.ExpiresIn*MicrosecondsPerSecond,reply.Generation);
        Complete("Online link saved.");
    }

    private void Remember(OnlineCardCredential credential)
    {
        cardWriteFailed=true;
        remembered=OnlineCredentialCard.Save(session.State,remembered,credential);
        cardWriteFailed=false;
    }

    private void OfferRelink()
    {
        relinkOffered=true;
        status="Your saved link has expired.\nA: get a new link code\nB: return to town";
    }

    private static string RotationId(byte[] entropy)
    {
        entropy[54]=(byte)((entropy[54]&15)|64);
        entropy[56]=(byte)((entropy[56]&63)|128);
        const string hex="0123456789abcdef";
        var id=new char[36];
        int at=0;
        for(int i=0;i<16;i++){
            if(i==4||i==6||i==8||i==10)id[at++]='-';
            int value=entropy[48+i];
            id[at++]=hex[value>>4];id[at++]=hex[value&15];
        }
        return new string(id);
    }

    private void Complete(string message)
    {
        finished=true;secret="";
        status=message+"\nA / B: return to town";
    }

    private static string TownIdentity(ulong id)
    {
        const string digits="0123456789ABCDEF";
        var chars=new char[16];
        for(int i=chars.Length-1;i>=0;--i){chars[i]=digits[(int)(id&15)];id>>=4;}
        return new string(chars);
    }

    private static byte[] ReadBounded(string path,int maximum)
    {
        using var input=File.OpenRead(path);
        long length=input.Length;
        if(length<1||length>maximum)throw new IOException("Invalid online configuration size.");
        var bytes=new byte[(int)length];
        int offset=0;
        while(offset<bytes.Length)
        {
            int count=input.Read(bytes,offset,bytes.Length-offset);
            if(count<=0)throw new IOException("Incomplete online configuration.");
            offset+=count;
        }
        return bytes;
    }

    private static string ReadOrigin()
    {
        var bytes=ReadBounded(ContentPaths.Asset("online-origin.txt"),96);
        var value=new Dolphin.Text.UTF8Encoding(false,true).GetString(bytes).Trim();
        if(!OnlineCardCredential.ValidOrigin(value))throw new FormatException("Expected an HTTPS host.");
        return value;
    }

    private static bool Digits(string value,int length)
    {
        if(value.Length!=length)return false;
        for(int i=0;i<length;i++)if(value[i]<'0'||value[i]>'9')return false;
        return true;
    }

    private sealed class LinkReply
    {
        internal string Code="",Secret="",VerificationPath="",Status="",Town="",AccountId="",DeviceId="",Token="",RefreshToken="";
        internal int ExpiresIn=-1,Interval=-1,Generation=-1;

        internal static LinkReply Read(string json)
        {
            var reply=new LinkReply();
            var reader=new JsonReader(json);
            if(!reader.Read()||reader.TokenType!=JsonTokenType.StartObject)throw new FormatException("Expected a pairing reply.");
            while(reader.Read()&&reader.TokenType!=JsonTokenType.EndObject)
            {
                if(reader.TokenType!=JsonTokenType.PropertyName)throw new FormatException("Invalid pairing field.");
                string field=reader.Value;
                if(!reader.Read())throw new FormatException("Incomplete pairing reply.");
                switch(field)
                {
                    case "code":ReadField(reader,ref reply.Code);break;
                    case "secret":ReadField(reader,ref reply.Secret);break;
                    case "verificationPath":ReadField(reader,ref reply.VerificationPath);break;
                    case "status":ReadField(reader,ref reply.Status);break;
                    case "town":ReadField(reader,ref reply.Town);break;
                    case "id":ReadField(reader,ref reply.AccountId);break;
                    case "deviceId":ReadField(reader,ref reply.DeviceId);break;
                    case "token":ReadField(reader,ref reply.Token);break;
                    case "refreshToken":ReadField(reader,ref reply.RefreshToken);break;
                    case "expiresIn":ReadNumber(reader,ref reply.ExpiresIn);break;
                    case "interval":ReadNumber(reader,ref reply.Interval);break;
                    case "generation":ReadNumber(reader,ref reply.Generation);break;
                    default:throw new FormatException("Unknown pairing field.");
                }
            }
            if(reader.TokenType!=JsonTokenType.EndObject||reader.Read())throw new FormatException("Invalid pairing reply.");
            return reply;
        }

        private static void ReadField(JsonReader reader,ref string target)
        {
            if(reader.TokenType!=JsonTokenType.String||reader.Value.Length==0||target.Length!=0)
                throw new FormatException("Invalid pairing field.");
            target=reader.Value;
        }

        private static void ReadNumber(JsonReader reader,ref int target)
        {
            int value=reader.GetInt32();
            if(target!=-1||value<0)throw new FormatException("Invalid pairing interval.");
            target=value;
        }
    }
}
