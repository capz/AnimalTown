#if ANIMALTOWN_ONLINE_PLAY
using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AnimalTown;
using AnimalTown.Online;
using Dolphin.Input;
using Dolphin.Diagnostics;
using Dolphin.Timing;

internal sealed class OnlinePlay:IDisposable
{
    const string Empty="00000000-0000-0000-0000-000000000000";
    readonly TownSession session;readonly DevConfig config;
    OnlineApi api=null!;OnlineMenuUi? ui;
    OnlineTravelStore.Snapshot? journal;
    GameState home;
    Action? serviceTick;
    bool toTitle;
    bool closingTown;
    bool erasingTown;
    bool closureCompleted;
    CancellationTokenSource stopping=new CancellationTokenSource();
    OnlinePlay(DevConfig config,TownSession session){this.config=config;this.session=session;home=session.State;}
    internal static bool Recover(DevConfig config,TownSession session)
    {
        if(!SaveCards.Enabled||!session.Loaded||session.Damaged)return true;
        OnlineTravelStore.Snapshot? saved;
        try{saved=OnlineTravelCard.Load(session.State);}
        catch(Exception)
        {
            using var notice=new OnlinePlay(config,session);notice.Message("Heading home","This card’s recovery record could not be verified. Keep the card and return to title.");return false;
        }
        if(saved==null)return true;
        session.State.HostedCommerceLocked=true;
        if(saved.Record.Phase==OnlineTravelPhase.Ready)
        {
            bool resident=false;for(int p=0;p<GameState.ProfileCount;p++)if(session.State.Profiles[p].Created)resident=true;
            if(!resident)return true; // A safely closed empty town may create its next profile offline.
        }
        return Run(config,session,true);
    }
    internal static bool CloseLastResident(DevConfig config,TownSession session,bool eraseTown=false)
    {
        using var flow=new OnlinePlay(config,session){closingTown=true,erasingTown=eraseTown};
        try{return flow.ConfirmClosure()&&flow.Show(false)&&flow.closureCompleted;}
        catch(Exception error){flow.ReleaseUi();flow.Message(eraseTown?"Town closure":"Last resident",Describe(error),eraseTown?"Your town has been kept.":"The resident has been kept.");return false;}
    }
    internal static bool Run(DevConfig config,TownSession session,bool recoveryOnly=false)
    {
        using var flow=new OnlinePlay(config,session);
        try{return flow.Show(recoveryOnly);}
        catch(Exception error)
        {
            DebugOverlay.WriteLine("ANIMALTOWN ONLINE FLOW FAILED "+FailureKind(error));
            flow.session.Online=null;flow.session.AdoptTravel(flow.home);
            flow.ReleaseUi();
            flow.Message("Online play",Describe(error),"Your card and accepted progress are safe.");
            // An unsettled journal must never fall through into offline play.
            try{return OnlineTravelCard.Load(flow.home)?.Record.Phase==OnlineTravelPhase.Ready;}
            catch(Exception){return false;}
        }
    }
    static string FailureKind(Exception error)=>error is OperationCanceledException?"cancelled":error is TimeoutException?"timeout":error is FormatException?"reply":error is OutOfMemoryException?"memory":error is IOException?"io":error is Dolphin.Network.SocketException?"socket":error is InvalidOperationException?"operation":error is ArgumentException?"argument":error is IndexOutOfRangeException?"index":error is NullReferenceException?"null-reference":error is NotSupportedException?"unsupported":"runtime";
    OnlineMenuUi Ui=>ui??=new OnlineMenuUi();
    T Await<T>(Task<T> request,string title="Travelling",string detail="Connecting…")
    {
        PumpRequest(request,title,detail);
        return request.GetAwaiter().GetResult();
    }
    void Await(Task request,string title,string detail)
    {
        PumpRequest(request,title,detail);
        request.GetAwaiter().GetResult();
    }
    void PumpRequest(Task request,string title,string detail)
    {
        var cancellation=stopping;
        Ui.Set(title,"Keep your home card in "+SaveCards.Slot,detail,null,"B: cancel safely");
        while(!request.IsCompleted)
        {
            serviceTick?.Invoke();Ui.Draw();RuntimeOptions.Wait();Controller.Update();
            if(Controller.GetState(0).B.WasPressed&&!cancellation.IsCancellationRequested)
            {cancellation.Cancel();Ui.Set("Returning safely","Your accepted progress is safe.","Finishing the interrupted step…",null,"");}
        }
        if(cancellation.IsCancellationRequested){stopping=new CancellationTokenSource();cancellation.Dispose();}
    }
    OnlineJson Request(string method,string path,string? body=null,string title="Travelling",string detail="Connecting…")
    {
        EnsureAccess();return Await(api.Json(method,path,body,stopping.Token),title,detail);
    }
    void EnsureAccess()
    {
        if(session.OnlineCredentials!.AccessExpiresAt>Clock.Timestamp+20000000)return;
        session.Online?.Disconnect();ReleaseUi();api.Dispose();session.OnlineCredentials=null;
        OnlinePairing.Run(session);
        if(session.OnlineCredentials==null)throw new IOException("device-authorization-expired");
        api=new OnlineApi(session.OnlineCredentials);
        Await(api.Initialize(stopping.Token),"Online","Renewing your secure connection…");
    }
    void Message(string title,string detail,string subtitle="")
    {Ui.Set(title,subtitle,detail,new[]{"Continue"},"A: continue");Ui.Choose(serviceTick,false);}
    bool Show(bool recoveryOnly)
    {
        journal=OnlineTravelCard.Load(home);
        try{OnlinePairing.ReadOrigin();}
        catch(Exception)
        {
            string? service=Ui.EditService(journal?.Record.Origin??"");if(service==null)return journal==null||journal.Record.Phase==OnlineTravelPhase.Ready;
            OnlineServiceOrigin.Save(service);
        }
        string configuredOrigin=OnlinePairing.ReadOrigin();
        if(journal!=null&&journal.Record.Phase!=OnlineTravelPhase.Ready&&journal.Record.Origin!=configuredOrigin)
        {
            Ui.Set("Recover online progress","The service address changed.","Relink the same account. The new service must prove it still holds this town’s pending return.",new[]{"Link this service","Choose another address"},"A: choose   B: return to title");
            int choice=Ui.Choose();if(choice<0)return false;
            if(choice==1){string? address=Ui.EditService(journal.Record.Origin);if(address==null)return false;OnlineServiceOrigin.Save(address);configuredOrigin=address;}
        }
        if(journal!=null&&journal.Record.Phase!=OnlineTravelPhase.Ready)
        {
            int profile=journal.Record.Profile;
            if(!home.Profiles[profile].Created||home.ProfileLocked(profile))throw new IOException("selected-villager-changed");
            if(home.ActiveProfile!=profile)session.SelectProfile(profile);
        }
        if(session.OnlineCredentials?.Origin!=configuredOrigin)session.OnlineCredentials=null;
        if(session.OnlineCredentials==null||session.OnlineCredentials.AccessExpiresAt<Clock.Timestamp+30000000)
        {
            ReleaseUi();OnlinePairing.Run(session);
            if(session.OnlineCredentials==null)return journal==null||journal.Record.Phase==OnlineTravelPhase.Ready;
        }
        api=new OnlineApi(session.OnlineCredentials!);
        Await(api.Initialize(stopping.Token),"Online","Checking the adapter and secure connection…");
        if(journal!=null&&journal.Record.Origin!=api.Origin&&journal.Record.Phase!=OnlineTravelPhase.Ready)
        {
            var recovery=Request("GET","/v1/online/reservations/"+journal.Record.Reservation,title:"Recover online progress",detail:"Verifying the existing reservation on this service…");
            var previous=journal.Record;
            bool publishing=previous.Phase>=OnlineTravelPhase.Publishing;
            if(api.Account!=previous.Account||session.OnlineCredentials!.Town!=previous.Town||recovery.Field("profile").Int32!=previous.Profile||
                recovery.Field("parentRevision").Number!=(publishing?previous.Revision-1:previous.Revision)||
                (publishing?recovery.Field("revision").Number!=previous.Revision||recovery.Field("sha256").Text!=previous.Fingerprint:recovery.Field("parentFingerprint").Text!=previous.Fingerprint))
                throw new IOException("original-service-required");
            journal=OnlineTravelCard.Rebind(home,journal,api.Origin);
        }
        if(journal!=null&&journal.Record.Phase!=OnlineTravelPhase.Ready)
        {
            if(!FinishReturn())return false;
            if(recoveryOnly)return true;
        }
        if(recoveryOnly)return journal==null||Sync();
        if(closingTown)return closureCompleted=CloseTown();
        while(!toTitle)
        {
            bool open=false;
            try{open=Request("GET","/v1/online/gate",title:"Online",detail:"Checking your gate…").Field("open").Flag;}
            catch(IOException){/* Service configuration must remain reachable when the old tunnel is gone. */}
            Ui.Set("Online","Linked as "+home.Name,"",new[]{open?"Join my open town":"Open gate","Visit a friend","Sync town","Link account / service"},"A: choose   B: back");
            int choice=Ui.Choose();if(choice<0)break;
            if(choice==3)
            {
                Ui.Set("Link account / service","This card stays bound to the exact service.","Changing the address requires linking again.",new[]{"Link account","Change service"},"A: choose   B: back");
                int linkAction=Ui.Choose();if(linkAction<0)continue;
                if(linkAction==1)
                {
                    if(journal!=null&&journal.Record.Phase!=OnlineTravelPhase.Ready)throw new IOException("original-service-required");
                    string? changed=Ui.EditService(api.Origin);if(changed==null)continue;OnlineServiceOrigin.Save(changed);session.OnlineCredentials=null;
                }
                ReleaseUi();api.Dispose();OnlinePairing.Run(session);return true;
            }
            if(choice==2){if(!Sync())return false;Message("Town synchronized","Your home card has the latest reviewed changes and earnings.");continue;}
            if(choice==0&&open)
            {
                Ui.Set("Your gate","Guests may play while your console is off.","The backend and tunnel must stay running.",new[]{"Play in my town","Close gate"},"A: choose   B: back");
                int action=Ui.Choose();if(action<0)continue;
                if(action==1){Request("DELETE","/v1/online/gate",title:"Your gate",detail:"Closing the gate…");continue;}
            }
            if(!Sync())return false;
            string host=api.Account,visit=Empty;
            if(choice==1)
            {
                string? friend=ChooseFriend();if(friend==null)continue;host=friend;
                var admission=Request("POST","/v1/online/visits","{\"host\":"+OnlineApi.Quote(host)+"}","Travelling","Reserving your place in the town…");visit=admission.Field("id").Text;
            }
            else Request("POST","/v1/online/gate/open","{}","Your gate","Opening your town to mutual friends…");
            Reserve(host,visit);
            Play(host,visit);
            if(!FinishReturn())return false;
        }
        return !toTitle&&(journal==null||journal.Record.Phase==OnlineTravelPhase.Ready);
    }
    bool ConfirmClosure()
    {
        string title=erasingTown?"Town closure":"Last resident";
        var bytes=new byte[OnlineTownProjection.MaximumBytes];int length=OnlineTownProjection.Write(home,bytes);
        var scene=OnlineTownProjection.Read(bytes,length);Array.Clear(bytes,0,bytes.Length);Ui.ReviewScene(scene,config);
        Ui.Set(title,erasingTown?"Review before erasing this town":"Review before deleting this profile","Close the gate and settle activity.\nKeep confirmed guest earnings.\nReview placements and mail.\nDeletion waits for safe closure.",new[]{"Review and close online town",erasingTown?"Keep town for now":"Keep resident for now"},"D-pad / A: choose   B: keep");
        Ui.ReviewFocus(0,home.HomeX(home.ActiveProfile)+3.5f,home.HomeZ(home.ActiveProfile)+4.5f,"Current town · closure review");
        try{return Ui.Choose()==0;}
        finally{Ui.EndReview();}
    }
    bool CloseTown()
    {
        string title=erasingTown?"Town closure":"Last resident";
        var state=Request("GET","/v1/online/card",title:title,detail:"Checking pending online activity…");
        if(erasingTown&&state.Field("retired").Flag)return true;
        if(state.Field("closed").Flag)return !erasingTown||RetireTown();
        Request("DELETE","/v1/online/gate",title:title,detail:"Closing admission and allowing guests to return home…");
        if(!Sync())return false;
        state=Request("GET","/v1/online/card",title:title,detail:"Checking the final verified card head…");
        string id=Await(OnlineApi.NewId(stopping.Token),title,"Preparing a durable closure…");
        var closed=Request("POST","/v1/online/closure","{\"id\":"+OnlineApi.Quote(id)+",\"revision\":"+state.Field("revision").Number+",\"fingerprint\":"+OnlineApi.Quote(state.Field("sha256").Text)+",\"wholeTown\":"+(erasingTown?"true":"false")+"}",title,"Preserving confirmed guest progress before closure…");
        return closed.Field("closed").Flag&&closed.Field("sha256").Text==Fingerprint()&&(!erasingTown||RetireTown());
    }
    bool RetireTown()
    {
        string id=Await(OnlineApi.NewId(stopping.Token),"Town closure","Preparing recoverable town retirement…");
        var result=Request("POST","/v1/online/retire","{\"id\":"+OnlineApi.Quote(id)+"}","Town closure","Keeping the old checkpoint and protecting guest earnings…");
        return result.Field("retired").Flag&&result.Field("town").Text==session.OnlineCredentials!.Town;
    }
    string? ChooseFriend()
    {
        int page=0;
        while(true)
        {
            var listed=Request("GET","/v1/online/friends?page="+page,title:"Visit a friend",detail:"Looking for open towns…");
            var friends=listed.Field("friends").Children;
            if(friends.Count==0){Message("Visit a friend","No mutual friends have an open town. Open the account page to add each other as friends.");return null;}
            // Keep the approved four-row list and page through all server rows.
            for(int start=0;start<friends.Count;start+=3)
            {
                int count=Math.Min(3,friends.Count-start);bool more=start+count<friends.Count||listed.Field("more").Flag;
                var labels=new string[count+1];
                for(int i=0;i<count;i++)labels[i]=friends[start+i].Field("name").Text+"   "+friends[start+i].Field("guests").Number+" / 2";
                labels[count]=more?"More towns":"Refresh";
                Ui.Set("Visit a friend","Open towns · mutual friends","",labels,"A: visit   B: back");int choice=Ui.Choose();if(choice<0)return null;
                if(choice<count)
                {if(friends[start+choice].Field("guests").Number>=2){Message("Town full","Choose a town with a free guest place.");start-=3;continue;}return friends[start+choice].Field("id").Text;}
            }
            if(listed.Field("more").Flag)page++;else page=0;
        }
    }
    bool Sync()
    {
        DebugOverlay.WriteLine("ANIMALTOWN ONLINE SYNC BEGIN");
        var status=Request("GET","/v1/online/card",title:"Sync town",detail:"Checking this card’s acknowledged save…");
        var pending=status.Field("pending");
        if(!pending.Null)
        {
            if(journal==null||journal.Record.Reservation!=pending.Field("id").Text)throw new IOException("card-lineage-changed");
            // The server may have reserved just before a card-write failure.
            if(journal.Record.Phase==OnlineTravelPhase.Ready)
                Remember(OnlineTravelPhase.Reserved,journal.Record.Revision,journal.Record.Fingerprint,journal.Record.Reservation);
            return FinishReturn();
        }
        long revision=status.Field("revision").Number;
        if(journal==null)
        {
            string fingerprint=Fingerprint();
            // Persist and read back the first lineage before importing it.
            // An interrupted older first import is recoverable only when its
            // sole acknowledged root is byte-identical to this selected card.
            if(revision>0&&(revision!=1||fingerprint!=status.Field("sha256").Text))throw new IOException("card-lineage-changed");
            string intent=Await(OnlineApi.NewId(stopping.Token),"Sync town","Recording the first card checkpoint before upload…");
            Remember(OnlineTravelPhase.Ready,1,fingerprint,intent);
        }
        OnlineJson head=status;
        long parent=journal!.Record.Revision;
        if(revision!=parent||Fingerprint()!=status.Field("sha256").Text)
        {
            if(revision==0&&parent!=1)throw new IOException("card-lineage-changed");
            var uploaded=Await(api.Upload(home,revision==0?null:parent,stopping.Token),"Sync town","Uploading and verifying this card’s offline progress…");
            head=Request("GET","/v1/online/card",title:"Sync town",detail:"Verifying the acknowledged checkpoint…");
            if(head.Field("revision").Number!=uploaded.Field("revision").Number)throw new IOException("card-lineage-changed");
        }
        if(!head.Field("residentReview").Null)
        {
            if(head.Field("residentReview").Field("sha256").Text!=Fingerprint())throw new IOException("card-lineage-changed");
            if(!Review())return false;
            head=Request("GET","/v1/online/card",title:"Sync town",detail:"Verifying your reviewed resident changes…");
            if(!head.Field("residentReview").Null)throw new IOException("resident-review-required");
        }
        string id=Await(OnlineApi.NewId(stopping.Token),"Sync town","Preparing a recoverable card reservation…");
        Remember(OnlineTravelPhase.Ready,head.Field("revision").Number,head.Field("sha256").Text,id);
        Reserve(api.Account,Empty);
        return FinishReturn();
    }
    void Remember(OnlineTravelPhase phase,long revision,string fingerprint,string reservation)
    {
        var value=new OnlineTravelRecord(api.Origin,session.OnlineCredentials!.Town,api.Account,reservation,revision,home.ActiveProfile,phase,fingerprint);
        journal=OnlineTravelCard.Save(home,journal,value);home.HostedCommerceLocked=true;
        DebugOverlay.WriteLine("ANIMALTOWN ONLINE JOURNAL phase "+(int)phase+" revision "+revision);
    }
    void Reserve(string host,string visit)
    {
        if(journal==null||journal.Record.Phase!=OnlineTravelPhase.Ready)throw new IOException("card-reservation-required");
        string id=Await(OnlineApi.NewId(stopping.Token),"Travelling","Preparing your home card…");
        Remember(OnlineTravelPhase.Ready,journal.Record.Revision,journal.Record.Fingerprint,id);
        Request("POST","/v1/online/reservations","{\"id\":"+OnlineApi.Quote(id)+",\"host\":"+OnlineApi.Quote(host)+",\"visit\":"+OnlineApi.Quote(visit)+",\"revision\":"+journal.Record.Revision+",\"fingerprint\":"+OnlineApi.Quote(journal.Record.Fingerprint)+",\"profile\":"+home.ActiveProfile+"}");
        Remember(OnlineTravelPhase.Reserved,journal.Record.Revision,journal.Record.Fingerprint,id);
        Request("POST","/v1/online/reservations/"+id+"/activate","{}");
        Remember(OnlineTravelPhase.Active,journal.Record.Revision,journal.Record.Fingerprint,id);
    }
    bool Review()
    {
        while(true)
        {
            EnsureAccess();int outcome=ReviewSession();if(outcome<2)return outcome==1;
            // Release the old-generation lease before rotating credentials.
            // Committed choices remain durable; reload only remaining items.
            session.Online?.Disconnect();session.OnlineCredentials=null;
            ReleaseUi();api.Dispose();OnlinePairing.Run(session);
            if(session.OnlineCredentials==null)throw new IOException("device-authorization-expired");
            api=new OnlineApi(session.OnlineCredentials);Await(api.Initialize(stopping.Token),"Town review","Renewing your secure connection…");
        }
    }
    int ReviewSession()
    {
        OnlineJson started;
        try{started=Request("POST","/v1/review","{}","Town review","Pausing guests while you review the latest placements…");}
        catch(IOException error){if(error.Message=="review-empty")return 1;throw;}
        string lease=started.Field("lease").Text;
        long nextBeat=Clock.Timestamp+3000000;Task<OnlineJson>? heartbeat=null;Action? previous=serviceTick;
        void Tick()
        {
            previous?.Invoke();
            if(heartbeat!=null&&heartbeat.IsCompleted){try{heartbeat.GetAwaiter().GetResult();}catch(IOException){}catch(OperationCanceledException){}heartbeat=null;}
            if(heartbeat==null&&Clock.Timestamp>=nextBeat){nextBeat=Clock.Timestamp+3000000;heartbeat=api.Json("POST","/v1/review/"+lease+"/heartbeat","{}",stopping.Token);}
        }
        serviceTick=Tick;
        try
        {
            var status=Request("GET","/v1/online/card",title:"Town review",detail:"Checking for offline resident changes…");
            if(!status.Field("residentReview").Null)return ReviewResidents(lease);
            while(true)
            {
                var page=Request("GET","/v1/review/"+lease+"/placements",title:"Town review",detail:"Reading the latest remaining placements…");
                var entries=page.Field("entries").Children;if(entries.Count==0)return 1;
                var entry=entries[0];int item=entry.Field("item").Int32;
                if(!Catalog.Valid(item))throw new FormatException("Invalid reviewed item.");
                LoadReviewScene(lease);
                Ui.Set("Town review",Catalog.Items[item].Name,"Spot "+entry.Field("x").Number+", "+entry.Field("z").Number+" · room "+entry.Field("room").Number+"\nGuests wait during review.\nEach decision is saved.",new[]{"Keep in town","Send to koban"},"D-pad / A: choose   B: later");
                Ui.ReviewFocus(entry.Field("room").Int32,entry.Field("x").Int32+.5f,entry.Field("z").Int32+1.5f,Catalog.Items[item].Name);
                int choice=Ui.Choose(serviceTick,true,session.OnlineCredentials!.AccessExpiresAt-30000000);if(choice<0)return choice==-2?2:0;
                string kind=choice==0?"keep":"koban";
                try{Decide(entry,kind);}
                catch(IOException error)
                {
                    if(error.Message!="placement-conflict")throw;
                    Ui.Set("Choose this spot","Placements overlap here","The displaced item goes to the koban.\nYour purchases and earnings stay yours.",new[]{"Keep my placement","Keep guest placement"},"D-pad / A: choose   B: later");
                    choice=Ui.Choose(serviceTick,true,session.OnlineCredentials!.AccessExpiresAt-30000000);if(choice<0)return choice==-2?2:0;Decide(entry,choice==0?"koban":"keep-guest");
                }
            }
        }
        finally
        {
            serviceTick=previous;
            if(heartbeat!=null)try{Await(heartbeat,"Town review","Finishing the review heartbeat…");}catch(IOException){}
            try{Request("DELETE","/v1/review/"+lease,title:"Town review",detail:"Resuming guests…");}catch(IOException){/* The bounded lease expires if release cannot reach the service. */}
            Ui.EndReview();
        }
        void Decide(OnlineJson entry,string kind)
        {
            string op=Await(OnlineApi.NewId(stopping.Token),"Town review","Preparing this saved decision…");
            Request("POST","/v1/review/"+lease+"/placements/ops","{\"id\":"+OnlineApi.Quote(op)+",\"placement\":"+OnlineApi.Quote(entry.Field("id").Text)+",\"version\":"+entry.Field("version").Number+",\"kind\":"+OnlineApi.Quote(kind)+"}","Town review","Saving your decision…");
        }
    }
    void LoadReviewScene(string lease)
    {
        var scene=Await(api.ReviewScene(lease,stopping.Token),"Town review","Loading the current town for inspection…");
        Ui.ReviewScene(scene,config);
    }
    int ReviewResidents(string lease)
    {
        bool reconsider=false;
        while(true)
        {
            var page=Request("GET","/v1/review/"+lease+"/residents",title:"Resident review",detail:"Reading saved resident changes and online effects…");
            OnlineJson? selected=null;
            var entries=page.Field("entries").Children;
            for(int i=0;i<entries.Count;i++)if(!entries[i].Field("approved").Flag){selected=entries[i];break;}
            if(selected==null&&reconsider)
                for(int i=0;i<entries.Count;i++)if(entries[i].Field("kind").Text=="profile"&&entries[i].Field("previous").Text.Length>0){selected=entries[i];break;}
            reconsider=false;
            if(selected==null)
            {
                string op=Await(OnlineApi.NewId(stopping.Token),"Resident review","Preparing the completed review…");
                try
                {
                    Request("POST","/v1/review/"+lease+"/residents/complete","{\"id\":"+OnlineApi.Quote(op)+",\"review\":"+OnlineApi.Quote(page.Field("id").Text)+",\"version\":"+page.Field("version").Number+"}","Resident review","Applying reviewed changes and preserving earned online value…");return 1;
                }
                catch(IOException error)
                {
                    if(error.Message!="resident-review-capacity")throw;
                    Ui.Set("Make room first","Your choices are saved","Pending progress is held safely.\nChoose another recipient or finish later and make room.",new[]{"Choose another resident","Finish later"},"D-pad / A: choose   B: later");
                    int retry=Ui.Choose(serviceTick,true,session.OnlineCredentials!.AccessExpiresAt-30000000);
                    if(retry<0)return retry==-2?2:0;if(retry!=0)return 0;reconsider=true;continue;
                }
            }
            LoadReviewScene(lease);
            var entry=selected!;bool profile=entry.Field("kind").Text=="profile",removed=profile&&entry.Field("previous").Text.Length>0;
            string name=entry.Field("name").Text,previous=entry.Field("previousName").Text;
            string title=profile?(removed?previous+" was removed":name+" joined"):previous+" moved";
            string detail=profile?(removed?entry.Field("proceeds").Number+" Bells · "+entry.Field("letters").Number+" letters\n"+entry.Field("rewards").Number+" earned rewards\nOnly new online value moves.\nOffline-discarded possessions stay discarded.":"Keep this saved home, pockets and offline progress.\nAdd this resident to the online directory."):
                name+" now lives here.\n"+entry.Field("jobs").Number+" unfinished errands · "+entry.Field("letters").Number+" letters\nKeep earned rewards.\nReturn undelivered mail.\nKeep the completed move.";
            Ui.Set(title,"Saved offline resident change",detail,new[]{removed?"Choose who keeps pending value":profile?"Add to online town":"Accept move and settle","Finish later"},"D-pad / A: choose   B: later");
            Ui.ReviewFocus(entry.Field("room").Int32,entry.Field("x").Int32/1024f,entry.Field("z").Int32/1024f,profile?(removed?previous+"’s former home":name+"’s current home"):"Current resident: "+name);
            int choice=Ui.Choose(serviceTick,true,session.OnlineCredentials!.AccessExpiresAt-30000000);if(choice<0)return choice==-2?2:0;if(choice!=0)return 0;
            string recipient="";
            if(removed)
            {
                var recipients=page.Field("recipients").Children;if(recipients.Count<1)throw new IOException("resident-recipient-required");
                var labels=new string[recipients.Count];for(int i=0;i<labels.Length;i++)labels[i]=recipients[i].Field("name").Text;
                Ui.Set("Choose a resident",previous+"’s pending online value","The selected resident receives the new proceeds, letters and earned rewards.",labels,"D-pad / A: choose   B: back");
                Ui.ReviewSelection(index=>{var p=recipients[index];Ui.ReviewFocus(0,p.Field("x").Int32/1024f,p.Field("z").Int32/1024f,p.Field("name").Text+" · selected recipient");});
                choice=Ui.Choose(serviceTick,true,session.OnlineCredentials!.AccessExpiresAt-30000000);if(choice==-2)return 2;if(choice<0)continue;
                recipient=recipients[choice].Field("identity").Text;
            }
            string id=Await(OnlineApi.NewId(stopping.Token),"Resident review","Preparing this saved decision…");
            Request("POST","/v1/review/"+lease+"/residents/ops","{\"id\":"+OnlineApi.Quote(id)+",\"review\":"+OnlineApi.Quote(page.Field("id").Text)+",\"version\":"+page.Field("version").Number+",\"index\":"+entry.Field("index").Number+",\"recipient\":"+OnlineApi.Quote(recipient)+"}","Resident review","Saving your choice…");
        }
    }
    bool FinishReturn()
    {
        if(journal==null)return true;
        while(true)
        {
            try
            {
                if(journal!.Record.Phase==OnlineTravelPhase.Published)
                {Acknowledge();return true;}
                if(journal.Record.Phase==OnlineTravelPhase.Reserved||journal.Record.Phase==OnlineTravelPhase.Active)
                {
                    if(!Review())return false;
                    var staged=Request("POST","/v1/online/reservations/"+journal.Record.Reservation+"/settlement","{}","Heading home","Preparing your home, purchases and earnings…");
                    Remember(OnlineTravelPhase.Publishing,staged.Field("revision").Number,staged.Field("sha256").Text,journal.Record.Reservation);
                }
                if(journal.Record.Phase!=OnlineTravelPhase.Publishing)throw new IOException("card-reservation-required");
                var publication=Request("GET","/v1/online/reservations/"+journal.Record.Reservation,title:"Heading home",detail:"Recovering your verified home checkpoint…");
                if(publication.Field("revision").Number!=journal.Record.Revision||publication.Field("sha256").Text!=journal.Record.Fingerprint)throw new IOException("publication-fingerprint-mismatch");
                // If the new bank won immediately before a cut, its exact
                // fingerprint permits the same acknowledgment without saving a
                // second generation. Every other case uses the guarded writer.
                bool already=Fingerprint()==journal.Record.Fingerprint;
                if(!already)
                {
                    using var download=Await(api.Download(journal.Record.Reservation,publication,stopping.Token),"Heading home","Downloading and checking your home checkpoint…");
                    Ui.Set("Heading home","Keep your home card in "+SaveCards.Slot,"Saving and reading back your home…",null,"");Ui.Draw();RuntimeOptions.Wait();
                    Publish(download);
                }
                Remember(OnlineTravelPhase.Published,journal.Record.Revision,journal.Record.Fingerprint,journal.Record.Reservation);
                Acknowledge();return true;
            }
            catch(Exception error)
            {
                DebugOverlay.WriteLine("ANIMALTOWN ONLINE RETURN FAILED "+FailureKind(error));
                Ui.Set("Heading home","Your accepted progress is waiting safely.",Describe(error)+"\nReinsert your home card, then try again.",new[]{"Retry saving","Change service","Return to title"},"A: choose");
                int choice=Ui.Choose(null,false);
                if(choice==0){ReloadHome();journal=OnlineTravelCard.Load(home)??throw new IOException("The recovery record is missing. Keep this card.");continue;}
                if(choice==1)
                {
                    string? changed=Ui.EditService(api.Origin);if(changed==null)continue;
                    OnlineServiceOrigin.Save(changed);session.OnlineCredentials=null;
                    Message("Recover online progress","Open this same town again and relink your account. The new service must prove it still holds this pending return.");
                }
                toTitle=true;return false;
            }
        }
    }
    string Fingerprint()
    {
        using var card=SaveCards.Mount();var storage=new CardSaveStorage(card,new byte[0],new byte[0],SaveCards.Device,initializeArchive:false);
        using var export=new SaveRepository(storage).OpenCheckpointExport(home);using var hash=new OnlineCheckpointHash();var buffer=new byte[16384];int count;
        while((count=export.Read(buffer,0,buffer.Length))!=0){hash.Append(buffer,0,count);serviceTick?.Invoke();Ui.Draw();RuntimeOptions.Wait();}
        Array.Clear(buffer,0,buffer.Length);return hash.Complete();
    }
    void Publish(OnlineCheckpointDownload download)
    {
        using var card=SaveCards.Mount();
        var storage=new CardSaveStorage(card,File.ReadAllBytes(ContentPaths.Asset("savebanner.rgb5a3")),File.ReadAllBytes(ContentPaths.Asset("saveicon.rgb5a3")),SaveCards.Device,home);
        home=download.Publish(new SaveRepository(storage),home);home.HostedCommerceLocked=true;session.AdoptTravel(home);
    }
    void ReloadHome()
    {
        using var card=SaveCards.Mount();var storage=new CardSaveStorage(card,new byte[0],new byte[0],SaveCards.Device,home,initializeArchive:false);
        var loaded=new SaveRepository(storage).Load(home)??throw new IOException("The home card could not be read.");
        if(loaded.Town.Jobs.TownId!=home.Town.Jobs.TownId)throw new IOException("The home card changed.");home=loaded;home.HostedCommerceLocked=true;session.AdoptTravel(home);
    }
    void Acknowledge()
    {
        Request("POST","/v1/online/reservations/"+journal!.Record.Reservation+"/published","{\"fingerprint\":"+OnlineApi.Quote(journal.Record.Fingerprint)+"}","Heading home","Confirming the card readback…");
        Remember(OnlineTravelPhase.Ready,journal.Record.Revision,journal.Record.Fingerprint,journal.Record.Reservation);
    }
    void Connect(OnlinePlayVisit visit,bool reconnect=false)
    {
        if(session.OnlineCredentials!.AccessExpiresAt<Clock.Timestamp+30000000)
        {
            visit.Connection?.Dispose();ReleaseUi();api.Dispose();session.OnlineCredentials=null;OnlinePairing.Run(session);
            if(session.OnlineCredentials==null)throw new IOException("device-authorization-expired");
            api=new OnlineApi(session.OnlineCredentials);Await(api.Initialize(stopping.Token),"Travelling","Renewing the secure connection…");
        }
        var issued=Request("POST","/v1/gameplay/tickets","{\"host\":"+OnlineApi.Quote(visit.Host)+",\"visit\":"+OnlineApi.Quote(visit.Visit)+"}","Travelling",reconnect?"Reconnecting… Your accepted actions will be recovered.":"Connecting to the town…");
        string hex=issued.Field("ticket").Text;if(!OnlineCardCredential.Hex(hex,0,64))throw new FormatException("Invalid gameplay ticket.");
        var ticket=new byte[32];const string digits="0123456789abcdef";
        for(int i=0;i<32;i++){char a=hex[i*2],b=hex[i*2+1];if(a>='A'&&a<='F')a=(char)(a+32);if(b>='A'&&b<='F')b=(char)(b+32);int high=digits.IndexOf(a),low=digits.IndexOf(b);ticket[i]=(byte)(high*16+low);}
        var connection=Await(OnlineGameplayConnection.ConnectAsync(api.Origin,ticket,visit.State,stopping.Token),"Travelling","Loading the town and your travel pockets…");
        if(reconnect)visit.Reconnect(connection);else visit.Connection=connection;
        visit.CredentialExpiresAt=session.OnlineCredentials!.AccessExpiresAt;
        long deadline=Clock.Timestamp+30000000;
        while(visit.World==null||visit.State.NeedsSnapshot||visit.Recovering||connection.Epoch==0||!connection.TryGetActor(connection.LocalSlot,out _))
        {
            visit.Tick(default);if(!visit.Connected||Clock.Timestamp>=deadline)throw new IOException("online-connection-interrupted");Ui.Draw();RuntimeOptions.Wait();Controller.Update();
        }
    }
    void Play(string host,string membership)
    {
        using var visit=new OnlinePlayVisit(api.Account,host,membership,home.ActiveProfile);Connect(visit);
        session.Online=visit;
        try
        {
            while(!visit.ReturnRequested)
            {
                session.ShowOnlineWorld(visit.World!);ReleaseUi();
                Program.RunSession(config,session);
                session.AdoptTravel(home);
                if(visit.ControlRequested!=0)
                {
                    int control=visit.ControlRequested;visit.ControlRequested=0;serviceTick=()=>visit.Tick(default);
                    try
                    {
                        if(control==1)Review();
                        else if(control==2)
                        {
                            var status=Request("GET","/v1/online/gate",title:"Your guests",detail:"Checking the current guests…");var guests=status.Field("guests").Children;
                            if(guests.Count==0)Message("Your guests","There are no guests to remove.");
                            else
                            {
                                var labels=new string[guests.Count];for(int i=0;i<labels.Length;i++)labels[i]="Remove "+guests[i].Field("name").Text;
                                Ui.Set("Your guests","Removed guests cannot rejoin this gate.","Their accepted purchases and earnings stay safe.",labels,"A: remove   B: back");
                                int choice=Ui.Choose(serviceTick);if(choice>=0)Request("POST","/v1/online/visits/"+guests[choice].Field("id").Text+"/end","{}","Your guests","Removing this guest…");
                            }
                        }
                        else{Request("DELETE","/v1/online/gate",title:"Your gate",detail:"Closing the gate…");visit.ReturnRequested=true;}
                    }
                    finally{serviceTick=null;}
                }
                if(!visit.Connected&&!visit.ReturnRequested)
                {
                    while(true)
                    {
                        try{Connect(visit,true);break;}
                        catch(Exception error)
                        {
                            Ui.Set("Travelling","Your accepted progress is safe.",Describe(error),new[]{"Reconnect","Return home"},"A: choose");
                            if(Ui.Choose(null,false)==0)continue;visit.ReturnRequested=true;break;
                        }
                    }
                }
            }
        }
        finally
        {
            session.Online=null;session.AdoptTravel(home);
            if(membership!=Empty)try{Request("POST","/v1/online/visits/"+membership+"/end","{}","Heading home","Leaving the visited town…");}catch(IOException){}
        }
    }
    static string Describe(Exception error)
    {
        string code=error.Message??"";
        if(code=="town-full")return "That town is full. Choose another open town.";
        if(code=="town-in-review")return "The owner is reviewing the town. Your visit will resume shortly.";
        if(code=="card-lineage-changed"||code=="offline-reconciliation-required")return "This card needs reconciliation. Keep the original card and return to title.";
        if(code=="original-service-required")return "This service could not prove the existing return. Keep this card and reconnect to the backend holding its progress.";
        if(code=="device-authorization-expired")return "Your online link needs renewal. Reopen Online with this same card.";
        if(code=="placement-conflict")return "That spot no longer fits. Review it again or finish later.";
        if(code=="koban-capacity-exceeded")return "Make room in the koban before completing this review.";
        if(code=="online-closure-pending")return "Guests or queued mail still need to finish returning. The gate is closed and your resident is kept. Try deletion again after their progress settles.";
        if(code=="online-town-closed")return "This online town was closed before its last resident was removed. Synchronize the new resident changes before reopening.";
        return "The connection or card could not be verified. Your accepted progress is safe.";
    }
    void ReleaseUi(){ui?.Dispose();ui=null;LoadingScreen.Finish();TextureCache.TrimUnused();AnimalTown.GameCube.NativeTexturePages.Collect();}
    public void Dispose(){serviceTick=null;stopping.Cancel();stopping.Dispose();ReleaseUi();api?.Dispose();}
}
#endif
