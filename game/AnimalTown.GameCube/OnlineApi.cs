#if ANIMALTOWN_ONLINE_PLAY
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown;
using AnimalTown.Online;
using Dolphin.Network;
using Dolphin.Security;
using Dolphin.Timing;
using Http=Dolphin.Network.Http;

internal sealed class OnlineApi:IDisposable
{
    readonly OnlineDeviceLink link;
    readonly Http.HttpClient client=new Http.HttpClient{MaximumResponseBytes=16384,MaximumStreamBytes=16384,MaximumRedirects=0};
    internal string Account=>link.AccountId;
    internal string Origin=>link.Origin;
    internal OnlineApi(OnlineDeviceLink link){this.link=link;}
    internal async Task Initialize(CancellationToken cancellation)
    {
        if(OnlinePairing.ReadOrigin()!=link.Origin)throw new IOException("The service address changed. Finish recovery using the original service.");
        if(!NetworkRuntime.IsReady)await NetworkRuntime.StartAsync(new NetworkOptions(),cancellation);
        byte[] roots=OnlinePairing.ReadBounded(ContentPaths.Asset("online-roots.pem"),16384);
        var clock=new TownClock();clock.Update(NativeCalendar.ReadClock());
        long unix=(10957L+clock.CivilDay)*86400+clock.MinuteOfDay*60;
        byte[] entropy=await PlatformEntropy.GetBytesAsync(48,cancellation);
        try{cancellation.ThrowIfCancellationRequested();TlsConfiguration.InitializeFromLocalClock(roots,entropy,unix);}
        finally{Array.Clear(entropy,0,entropy.Length);Array.Clear(roots,0,roots.Length);}
    }
    internal async Task<OnlineJson> Json(string method,string path,string? body,CancellationToken cancellation)
    {
        byte[]? bytes=body==null?null:new Dolphin.Text.UTF8Encoding(false,true).GetBytes(body);
        try{return OnlineJson.Read(new Dolphin.Text.UTF8Encoding(false,true).GetString(await Send(method,path,bytes,body==null?null:"application/json",cancellation)));}
        finally{if(bytes!=null)Array.Clear(bytes,0,bytes.Length);}
    }
    internal async Task<byte[]> Send(string method,string path,byte[]? bytes,string? type,CancellationToken cancellation)
    {
        if(path.Length==0||path[0]!='/'||path.IndexOf('\r')>=0||path.IndexOf('\n')>=0)throw new FormatException("Invalid online request path.");
        using var request=new Http.HttpRequestMessage(new Http.HttpMethod(method),link.Origin+path);
        request.Headers.Add("authorization","Bearer "+link.AccessToken);
        if(bytes!=null){request.Content=new Http.ByteArrayContent(bytes);request.Content.Headers.Add("content-type",type!);}
        cancellation.ThrowIfCancellationRequested();using var deadline=new CancellationTokenSource();
        long expires=Clock.Timestamp+20000000;bool timedOut=false;
        var sending=client.SendAsync(request,deadline.Token);
        while(!sending.IsCompleted)
        {
            if(cancellation.IsCancellationRequested||Clock.Timestamp>=expires){timedOut=!cancellation.IsCancellationRequested;deadline.Cancel();}
            await Task.Delay(1);
        }
        if(timedOut){try{using var late=sending.GetAwaiter().GetResult();}catch(OperationCanceledException){}throw new TimeoutException("The online request timed out.");}
        using var response=await sending;cancellation.ThrowIfCancellationRequested();
        if(!response.IsSuccessStatusCode)
        {
            string reason="Online service unavailable. Your card and progress are safe.";
            try{reason=OnlineJson.Read(await response.Content.ReadAsStringAsync()).Field("error").Text;}catch(FormatException){}
            throw new IOException(reason);
        }
        return await response.Content.ReadAsByteArrayAsync();
    }
    internal static string Quote(string text)
    {
        // Control requests use validated ASCII IDs, hashes and origins only.
        for(int i=0;i<text.Length;i++)if(text[i]<32||text[i]>126||text[i]=='"'||text[i]=='\\')throw new FormatException("Invalid online control value.");
        return "\""+text+"\"";
    }
    internal static async Task<string> NewId(CancellationToken cancellation)
    {
        byte[] bytes=await PlatformEntropy.GetBytesAsync(48,cancellation);
        try{bytes[7]=(byte)((bytes[7]&15)|64);bytes[8]=(byte)((bytes[8]&63)|128);return OnlineIdentifiers.Format(new OnlinePacketReader(bytes,0,16).Id());}
        finally{Array.Clear(bytes,0,bytes.Length);}
    }
    internal async Task<OnlineJson> Upload(GameState home,long? parent,CancellationToken cancellation)
    {
        string hash;int length;
        using(var card=SaveCards.Mount())
        {
            var storage=new CardSaveStorage(card,new byte[0],new byte[0],SaveCards.Device,initializeArchive:false);
            using var cursor=new SaveRepository(storage).OpenCheckpointExport(home);
            using var digest=new OnlineCheckpointHash();var buffer=new byte[16384];int count;
            while((count=cursor.Read(buffer,0,buffer.Length))!=0){digest.Append(buffer,0,count);await Task.Delay(1,cancellation);}
            hash=digest.Complete();length=cursor.Length;Array.Clear(buffer,0,buffer.Length);
        }
        string path="/v1/towns/"+Account+"/checkpoint-uploads";
        var offered=await Json("POST",path,"{\"length\":"+length+",\"sha256\":"+Quote(hash)+(parent.HasValue?",\"parentRevision\":"+parent.Value:"")+"}",cancellation);
        if(offered.Field("completed").Flag)return offered;
        string id=offered.Field("id").Text;int offset=offered.Field("offset").Int32;
        if(offset<0||offset>length)throw new FormatException("Invalid checkpoint resume offset.");
        using(var card=SaveCards.Mount())
        {
            var storage=new CardSaveStorage(card,new byte[0],new byte[0],SaveCards.Device,initializeArchive:false);
            using var cursor=new SaveRepository(storage).OpenCheckpointExport(home);var buffer=new byte[16384];
            // The server's committed offset wins after interruption.
            while(cursor.Position<offset){int count=cursor.Read(buffer,0,Math.Min(buffer.Length,offset-cursor.Position));if(count==0)throw new IOException("Checkpoint resume ended early.");}
            while(cursor.Position<length)
            {
                int start=cursor.Position,target=Math.Min(16384,length-start),used=0;
                while(used<target){int count=cursor.Read(buffer,used,target-used);if(count==0)throw new IOException("Checkpoint ended early.");used+=count;}
                byte[] page=buffer;if(used!=page.Length){page=new byte[used];Array.Copy(buffer,page,used);}
                try{offered=OnlineJson.Read(new Dolphin.Text.UTF8Encoding(false,true).GetString(await Send("PUT",path+"/"+id+"/page?offset="+start,page,"application/octet-stream",cancellation)));}
                finally{if(!ReferenceEquals(page,buffer))Array.Clear(page,0,page.Length);}
                if(offered.Field("offset").Number!=cursor.Position)throw new IOException("Checkpoint resume offset changed. Retry synchronization.");
                await Task.Delay(1,cancellation);
            }
            Array.Clear(buffer,0,buffer.Length);
        }
        if(!offered.Field("completed").Flag)throw new IOException("Checkpoint publication is incomplete.");
        return offered;
    }
    internal async Task<OnlineCheckpointDownload> Download(string reservation,OnlineJson publication,CancellationToken cancellation)
    {
        var download=new OnlineCheckpointDownload(publication.Field("length").Int32,publication.Field("sha256").Text);
        try
        {
            while(download.Position<publication.Field("length").Number)
            {
                byte[] page=await Send("GET","/v1/online/reservations/"+reservation+"/checkpoint?offset="+download.Position+"&count=16384",null,null,cancellation);
                try{if(page.Length==0)throw new IOException("Return checkpoint ended early.");download.Append(page,0,page.Length);}
                finally{Array.Clear(page,0,page.Length);}
                await Task.Delay(1,cancellation);
            }
            download.Verify();return download;
        }
        catch{download.Dispose();throw;}
    }
    public void Dispose(){client.Dispose();TlsConfiguration.Reset();if(NetworkRuntime.IsReady)NetworkRuntime.Stop();}
}
#endif
