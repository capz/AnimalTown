#if ANIMALTOWN_ONLINE_PLAY
using System;
using AnimalTown;
using AnimalTown.Jobs;
using AnimalTown.Online;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    sealed class OnlineJobRow
    {
        internal OnlineId Id;internal int Requester,Target,Template,Item,Phase,Version,Pocket;internal long Due;
        internal static OnlineJobRow Read(OnlinePacketReader r)=>new OnlineJobRow{Id=r.Id(),Requester=r.Int(),Target=r.Int(),Template=r.Int(),Item=r.Int(),Phase=r.Int(),Version=r.Int(),Due=r.Long(),Pocket=r.Int()};
    }
    readonly string[] onlineLabels=new string[40];int onlineRows,onlineSelected,onlineMode,onlineReply,onlinePage,onlinePocket,onlineRotation;
    readonly WrappedText onlineDescription=new WrappedText(48),onlineStatusText=new WrappedText(60);
    readonly TextPages[] onlineChatText={new TextPages(54),new TextPages(54),new TextPages(54)};
    readonly string[] onlineChatBodies=new string[3],onlineChatSpeakers=new string[3];
    OnlineId onlinePlacementId;
    readonly int[] onlineValues=new int[40];
    readonly OnlineJobRow?[] onlineJobs=new OnlineJobRow?[5];OnlineJobRow? onlineJob;
    int onlineNpc=-1,onlinePrice=100,onlineDigit=2,onlineShopFixture,onlineShopSource;bool onlineReprice;
    string onlineTitle="Online",onlineDetail="",onlineStatus="",onlinePausedScreen="world";
    OnlineMovementInput onlineMovement;
    bool OnlineBusy=>session.Online!.Pending!=null&&session.Online.Pending.Kind!=OnlineCommandKind.Snapshot;
    void OnlineList(int mode,string title,string detail,int rows)
    {onlineMode=mode;onlineTitle=title;onlineDetail=detail;onlineRows=rows;onlineSelected=0;screen="online-list";onlineStatus="";}
    void OnlineNotice(string text){OnlineList(0,"Online",text,1);onlineLabels[0]="Continue";}
    bool OnlineSend(OnlineCommand command)
    {if(!session.Online!.Send(command)){onlineStatus="A moment, please…";return false;}onlineStatus="Waiting for confirmation…";return true;}
    internal void OnlineFrame(ControllerState pad)
    {
        var visit=session.Online;if(visit==null)return;
        visit.Tick(screen=="world"?onlineMovement:default);
        if(visit.Reviewing&&screen!="online-wait"){onlinePausedScreen=screen;screen="online-wait";}
        if(!visit.Reviewing&&screen=="online-wait")screen=onlinePausedScreen;
        if(visit.ReplySerial!=onlineReply){onlineReply=visit.ReplySerial;OnlineReply(visit);}
    }
    void OnlineReply(OnlinePlayVisit visit)
    {
        var kind=visit.RepliedKind;if(kind==OnlineCommandKind.Snapshot||kind==OnlineCommandKind.Replay)return;
        bool ok=visit.Status>=200&&visit.Status<300;
        if(!ok&&(visit.Error=="shop-changed"||visit.Error=="visitor-inventory-changed"||visit.Error=="placement-changed"||visit.Error=="guest-placement-not-found"))visit.State.ExpectSnapshot();
        string failure=visit.Error=="visitor-pockets-full"||visit.Error=="job-capacity-required"?"Make a free pocket, then try again.":
            visit.Error=="shop-changed"?"The display changed. Please check it again.":visit.Error=="resident-out-of-reach"||visit.Error=="start-resident-conversation"?"Speak to that resident nearby first.":
            visit.Error=="chat-too-fast"?"Please wait a moment before sending another message.":visit.Error=="town-in-review"?"The owner is reviewing the town. Please wait.":visit.Error=="placement-occupied-by-player"?"Choose a spot clear of players.":"That action could not be confirmed. Check the town and try again.";
        if(kind==OnlineCommandKind.Chat)
        {
            if(ok){visit.ChatDraft="";ReleaseChatEntry();screen="world";}
            else if(chatEntry!=null)chatEntry.Status=failure;return;
        }
        if(kind==OnlineCommandKind.SendMail)
        {
            if(ok){visit.MailDraft="";ReleaseLetterEntry();OnlineNotice("Your letter is queued for the next post round.");}
            else if(letterEntry!=null){letterEntry.Status="Send failed. Your draft is still here. "+failure;screen="online-letter";}
            return;
        }
        if(!ok){onlineStatus=failure;return;}
        var r=new OnlinePacketReader(visit.Extra,0,visit.ExtraCount);
        if(kind==OnlineCommandKind.Jobs)
        {
            int count=r.Int();if(count<0||count>5)throw new InvalidOperationException("Invalid online job page.");
            for(int i=0;i<count;i++){onlineJobs[i]=OnlineJobRow.Read(r);onlineLabels[i]=JobTitle((JobTemplate)onlineJobs[i]!.Template);}
            for(int i=count;i<5;i++)onlineJobs[i]=null;
            OnlineList(3,"Town jobs","Only requests you can finish on this visit appear here.",count+1);onlineLabels[count]=count==5?"More requests":"Refresh";r.End();return;
        }
        if(kind==OnlineCommandKind.CollectReward)
        {
            int outcome=r.Int();r.Int();r.Int();r.End();
            OnlineNotice(outcome==(int)JobResult.Ok?"Your reward is in your travel pockets or balance.":"The reward is still owed. Make room and try again.");return;
        }
        if(kind==OnlineCommandKind.AbandonJob){OnlineNotice("The request has ended.");return;}
        if(kind==OnlineCommandKind.AskJob||kind==OnlineCommandKind.AcceptJob||kind==OnlineCommandKind.CompleteJob)
        {
            if(visit.Status==204||visit.ExtraCount==0){OnlineNotice("No eligible request is available right now. Try another resident.");return;}
            onlineJob=OnlineJobRow.Read(r);r.End();ShowOnlineJob();return;
        }
        if(kind==OnlineCommandKind.EnterRoom)
        {
            room=r.Int();state.X=r.Int()/65536f;state.Z=r.Int()/65536f;r.End();indoorCamera.Reset();screen="world";return;
        }
        onlineStatus="Confirmed.";
        if(kind==OnlineCommandKind.Place||kind==OnlineCommandKind.MovePlacement||kind==OnlineCommandKind.PickupPlacement)screen="world";
    }
    void ShowOnlineJob()
    {
        var job=onlineJob!;string npc=(uint)job.Requester<Town.SavedVillagerCount?state.Town.Residents[job.Requester].Name:"Resident";
        string target=(uint)job.Target<Town.SavedVillagerCount?state.Town.Residents[job.Target].Name:npc;
        string detail=job.Phase==0?npc+" has a request for you.":job.Phase==5?"Your earned reward is waiting. Make room to collect it.":job.Phase>=4?"This request has been handled. Your earnings travel home.":
            job.Template==(int)JobTemplate.FavorLetter?"Send a real letter to "+npc+". Your reply follows the next post round.":job.Template==(int)JobTemplate.FavorFruit?"Bring "+npc+" a "+Catalog.Items[job.Item].Name+" from your pockets.":
            job.Template==(int)JobTemplate.RetrievalBorrowed?job.Phase==2?"Return the borrowed possession to "+npc+".":"Ask "+target+" about the borrowed possession.":"Deliver the bound parcel to "+target+".";
        OnlineList(4,JobTitle((JobTemplate)job.Template),detail,job.Phase>=4&&job.Phase!=5?2:3);
        onlineLabels[0]=job.Phase==0?"Accept request":job.Phase==5||job.Phase==4?"Collect owed reward":job.Phase>=6?"Continue":"Hand over / ask about item";
        if(job.Phase>=4&&job.Phase!=5){onlineLabels[1]="Back";return;}
        onlineLabels[1]=job.Phase==0?"Not now":"Abandon request";onlineLabels[2]="Back";
    }
    void OpenOnlineMenu()
    {OnlineList(1,"Online play","Your accepted purchases and earnings stay yours.",4);onlineLabels[0]="Town jobs";onlineLabels[1]="Write a letter";onlineLabels[2]=session.Online!.Owner?"Gate / guests":"Continue playing";onlineLabels[3]="Return home";}
    void OpenOnlinePockets()
    {
        OnlineList(2,"Travel pockets","Choose furniture to place or paper to write a letter.",state.Capacity);
        for(int i=0;i<state.Capacity;i++){onlineValues[i]=i;onlineLabels[i]=state.Pockets[i]==0?"Empty pocket":Catalog.Items[state.Pockets[i]].Name;}
    }
    void OpenOnlineRecipients()
    {
        onlineRows=0;
        for(int npc=0;npc<Town.SavedVillagerCount;npc++)if(state.NpcPresent(npc)){onlineValues[onlineRows]=npc;onlineLabels[onlineRows++]=state.Town.Residents[npc].Name;}
        for(int profile=0;profile<GameState.ProfileCount;profile++)if(state.Profiles[profile].Created&&state.OnlineResidents!=null&&!state.OnlineResidents[profile].IsEmpty){onlineValues[onlineRows]=6+profile;onlineLabels[onlineRows++]=state.Profiles[profile].Name;}
        OnlineList(5,"Write a letter","Residents of the visited town. Letters cannot carry presents.",onlineRows);
    }
    void OpenOnlineLetter()
    {
        var visit=session.Online!;letterEntry=new ChatEntry(JobRulesV1.LetterMaximumScalars,true);letterEntry.RestoreDraft(visit.MailDraft);
        letterDraft=letterEntry.Text;RefreshLetterPreview();screen="online-letter";
    }
    void OpenOnlineShop(int fixture)
    {
        onlineShopFixture=fixture;onlineShopSource=fixture==ShopLayout.Cabinet?(int)ShopSource.Cabinet:fixture<TownShop.DisplayCount?(int)ShopSource.Display:(int)ShopSource.Consignment;
        if(onlineShopSource==(int)ShopSource.Cabinet)
        {
            OnlineList(6,"Essentials cabinet","Always in stock.",TownShop.CabinetCount);
            for(int i=0;i<onlineRows;i++){int item=TownShop.CabinetItem(i);onlineValues[i]=i;onlineLabels[i]=Catalog.Items[item].Name+" · "+UiFont.Money(Catalog.Items[item].Buy);}
            return;
        }
        int slot=onlineShopSource==(int)ShopSource.Display?fixture:fixture-TownShop.DisplayCount;
        int id=state.Town.Shop.Item(state,(ShopSource)onlineShopSource,slot);bool own=session.Online!.Owner&&onlineShopSource==(int)ShopSource.Consignment&&state.Town.Shop.SaleOwners[slot]==session.Online.Profile;
        onlineValues[0]=slot;OnlineList(7,id==0?"Empty display":Catalog.Items[id].Name,id==0?"Only the town's residents may stock this display.":"Price: "+UiFont.Money(state.Town.Shop.Price(state,(ShopSource)onlineShopSource,slot)),id==0?1:own?2:1);
        onlineLabels[0]=id==0?session.Online.Owner?"List from my pockets":"Back":own?"Change price":"Buy";onlineLabels[1]="Retrieve listing";
    }
    void OnlineInput(ControllerState p)
    {
        var visit=session.Online!;
        if(visit.Recovering)return;
        if(visit.Reviewing){if(p.A.WasPressed)visit.ReturnRequested=true;return;}
        if(OnlineBusy)return;
        if(screen==ChatEntry.Screen)
        {
            if(p.B.WasPressed){visit.ChatDraft=chatEntry!.Text;ReleaseChatEntry();screen="world";return;}
            if(p.Start.WasPressed){if(chatEntry!.Text.Length==0){chatEntry.Status="Write a message first.";return;}OnlineSend(new OnlineCommand{Kind=OnlineCommandKind.Chat,Text=chatEntry.Text});return;}
            KeyboardInput(chatEntry!,ChatEntry.Screen,p);visit.ChatDraft=chatEntry!.Text;return;
        }
        if(screen=="online-letter")
        {
            if(p.B.WasPressed){visit.MailDraft=letterEntry!.Text;ReleaseLetterEntry();screen="world";return;}
            if(p.Start.WasPressed)
            {
                if(!JobLetterGrader.TryGrade(letterEntry!.Text,false,out _)){letterEntry.Status="Check the length and text before sending.";return;}
                OnlineSend(new OnlineCommand{Kind=OnlineCommandKind.SendMail,Entity=visit.MailRecipient,A=visit.MailPaper,B=-1,Text=letterEntry.Text});return;
            }
            KeyboardInput(letterEntry!,"online-letter",p);visit.MailDraft=letterEntry!.Text;letterDraft=letterEntry.Text;RefreshLetterPreview();return;
        }
        if(screen=="map"){if(p.B.WasPressed||p.Y.WasPressed)screen="world";return;}
        int direction=menuNavigation.Read(screen,onlineMode,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,screen!="world");
        if(screen=="online-price")
        {
            if(p.B.WasPressed){screen="world";return;}
            if(direction==MenuNavigation.Left)onlineDigit=Math.Min(4,onlineDigit+1);if(direction==MenuNavigation.Right)onlineDigit=Math.Max(0,onlineDigit-1);
            int step=1;for(int i=0;i<onlineDigit;i++)step*=10;
            if(direction==MenuNavigation.Up||direction==MenuNavigation.Down)onlinePrice=Math.Clamp(onlinePrice+(direction==MenuNavigation.Up?step:-step),1,99999);
            if(p.A.WasPressed)OnlineSend(new OnlineCommand{Kind=onlineReprice?OnlineCommandKind.Reprice:OnlineCommandKind.List,Revision=state.Town.Shop.Revision,A=(int)ShopSource.Consignment,B=onlineShopFixture-TownShop.DisplayCount,C=onlinePocket,D=onlinePrice});return;
        }
        if(screen=="online-list")
        {
            if(p.B.WasPressed){screen="world";return;}
            if(onlineRows>0){if(direction==MenuNavigation.Up)onlineSelected=(onlineSelected+onlineRows-1)%onlineRows;if(direction==MenuNavigation.Down)onlineSelected=(onlineSelected+1)%onlineRows;}
            if(!p.A.WasPressed||onlineRows==0)return;
            int choice=onlineSelected;
            if(onlineMode==0){screen="world";return;}
            if(onlineMode==1)
            {
                if(choice==0){onlinePage=0;OnlineSend(new OnlineCommand{Kind=OnlineCommandKind.Jobs});}
                else if(choice==1){visit.MailPaper=PocketMenu.WritingPaper(state,-1);if(visit.MailPaper<0)OnlineNotice("You need writing paper. The shop cabinet sells it.");else OpenOnlineRecipients();}
                else if(choice==2&&visit.Owner){OnlineList(11,"Your gate","The backend and tunnel keep the town running.",4);onlineLabels[0]="Review town";onlineLabels[1]="Remove a guest";onlineLabels[2]="Close gate";onlineLabels[3]="Continue playing";}
                else if(choice==3)visit.ReturnRequested=true;else screen="world";return;
            }
            if(onlineMode==2)
            {
                onlinePocket=onlineValues[choice];int item=state.Pockets[onlinePocket];
                if(Catalog.Has(item,ItemFlags.Stationery)){visit.MailPaper=onlinePocket;OpenOnlineRecipients();}
                else if(Catalog.Has(item,ItemFlags.Place)){onlineRotation=0;OnlineList(8,"Place "+Catalog.Items[item].Name,"Place just ahead of you. Your item stays yours until review.",2);onlineLabels[0]="Place here";onlineLabels[1]="Rotate";}
                else onlineStatus="This item has no supported action during the visit.";return;
            }
            if(onlineMode==3)
            {
                if(choice<5&&onlineJobs[choice]!=null){onlineJob=onlineJobs[choice];ShowOnlineJob();}
                else{onlinePage=choice==5?onlinePage+5:0;OnlineSend(new OnlineCommand{Kind=OnlineCommandKind.Jobs,A=onlinePage});}return;
            }
            if(onlineMode==4)
            {
                var job=onlineJob!;if(choice==2||choice==1&&job.Phase==0||job.Phase>=4&&job.Phase!=5&&(choice==1||job.Phase>=6)){screen="world";return;}
                var kind=choice==1?OnlineCommandKind.AbandonJob:job.Phase==0?OnlineCommandKind.AcceptJob:job.Phase>=4?OnlineCommandKind.CollectReward:OnlineCommandKind.CompleteJob;
                int pocket=job.Template==(int)JobTemplate.FavorFruit?state.Find(job.Item):job.Pocket;
                OnlineSend(new OnlineCommand{Kind=kind,Entity=job.Id,Revision=job.Version,A=onlineNpc,B=pocket});return;
            }
            if(onlineMode==5)
            {
                int row=onlineValues[choice];var id=row<6?state.Town.Jobs.Npc(row):state.OnlineResidents![row-6];
                visit.MailRecipient=new OnlineId(id.Town,id.Serial);visit.MailRecipientName=onlineLabels[choice];OpenOnlineLetter();return;
            }
            if(onlineMode==6){OnlineSend(new OnlineCommand{Kind=OnlineCommandKind.Buy,Revision=state.Town.Shop.Revision,A=(int)ShopSource.Cabinet,B=onlineValues[choice]});return;}
            if(onlineMode==7)
            {
                int slot=onlineShopSource==(int)ShopSource.Display?onlineShopFixture:onlineShopFixture-TownShop.DisplayCount;
                int item=state.Town.Shop.Item(state,(ShopSource)onlineShopSource,slot);
                bool own=visit.Owner&&onlineShopSource==(int)ShopSource.Consignment&&state.Town.Shop.SaleOwners[slot]==visit.Profile;
                if(item==0)
                {
                    if(!visit.Owner){screen="world";return;}
                    OpenOnlinePockets();onlineMode=10;onlineTitle="List an item";onlineDetail="Choose an ordinary item to offer for sale.";return;
                }
                if(own&&choice==0){onlinePrice=state.Town.Shop.SalePrices[slot];onlineReprice=true;screen="online-price";return;}
                OnlineSend(new OnlineCommand{Kind=own?OnlineCommandKind.CancelListing:OnlineCommandKind.Buy,Revision=state.Town.Shop.Revision,A=onlineShopSource,B=slot});return;
            }
            if(onlineMode==8)
            {
                if(choice==1){onlineRotation=(onlineRotation+1)%4;onlineDetail="Rotation: "+(onlineRotation*90)+" degrees. Place just ahead of you.";return;}
                OnlineSend(new OnlineCommand{Kind=OnlineCommandKind.Place,Revision=visit.State.InventoryRevision,A=0,B=onlinePocket,C=(int)state.X+faceX,D=(int)state.Z+faceZ,E=onlineRotation,F=room});return;
            }
            if(onlineMode==9)
            {
                int index=-1;for(int i=0;i<visit.State.PlacementCount;i++)if(visit.State.Placements[i].Same(onlinePlacementId)){index=i;break;}
                if(index<0){OnlineNotice("That placement has been reviewed or retrieved.");return;}
                var id=visit.State.Placements[index];int version=visit.State.PlacementVersions[index],baseCount=state.Town.PlacedCount-visit.State.PlacementCount;
                if(choice==3){screen="world";return;}
                var current=state.Town.Placed[baseCount+index];
                OnlineSend(new OnlineCommand{Kind=choice==2?OnlineCommandKind.PickupPlacement:OnlineCommandKind.MovePlacement,Entity=id,Revision=visit.State.InventoryRevision,A=version,
                    C=choice==0?(int)state.X+faceX:current.X,D=choice==0?(int)state.Z+faceZ:current.Z,E=choice==1?(current.Rotation+1)%4:current.Rotation,F=room});return;
            }
            if(onlineMode==10)
            {
                onlinePocket=onlineValues[choice];int item=state.Pockets[onlinePocket];if(!TownShop.Sellable(item)){onlineStatus="Choose an ordinary sale item.";return;}
                onlinePrice=Math.Max(1,Catalog.Items[item].Buy);onlineReprice=false;screen="online-price";return;
            }
            if(onlineMode==11){if(choice<3)visit.ControlRequested=choice+1;else screen="world";return;}
            return;
        }
        if(screen!="world"){screen="world";return;}
        if(p.Start.WasPressed||p.Z.WasPressed){OpenOnlineMenu();return;}
        if(p.R.WasPressed){OpenChat();chatEntry!.RestoreDraft(visit.ChatDraft);return;}
        if(p.X.WasPressed){OpenOnlinePockets();return;}
        if(p.Y.WasPressed){screen="map";return;}
        if(!p.A.WasPressed)return;
        if(room==TownShop.Room){int fixture=ShopLayout.Near(state,state.X,state.Z);if(fixture>=0&&fixture!=ShopLayout.Counter){OpenOnlineShop(fixture);return;}}
        int door=villagers.Doors.Nearby(state,0,facingYaw);
        if(door>=0){OnlineSend(new OnlineCommand{Kind=OnlineCommandKind.EnterRoom,A=door});return;}
        int first=state.Town.PlacedCount-visit.State.PlacementCount;
        for(int i=0;i<visit.State.PlacementCount;i++)
        {
            var placed=state.Town.Placed[first+i];if(placed.Room!=room||!visit.State.PlacementOwners[i].Same(visit.Self))continue;
            float dx=placed.X+.5f-state.X,dz=placed.Z+.5f-state.Z;
            if(dx*dx+dz*dz>2.25f)continue;
            onlinePlacementId=visit.State.Placements[i];OnlineList(9,Catalog.Items[placed.Item].Name,"Your pending placement.",4);onlineLabels[0]="Move ahead";onlineLabels[1]="Rotate";onlineLabels[2]="Retrieve";onlineLabels[3]="Back";return;
        }
        onlineNpc=-1;float near=6.25f;
        for(int npc=0;npc<Town.SavedVillagerCount;npc++)
        {var resident=state.Town.Residents[npc];if(resident.Space!=room)continue;float dx=resident.PosX-state.X,dz=resident.PosZ-state.Z,distance=dx*dx+dz*dz;if(distance<near){near=distance;onlineNpc=npc;}}
        if(onlineNpc>=0)
        {
            var identity=state.Town.Jobs.Npc(onlineNpc);
            OnlineSend(new OnlineCommand{Kind=OnlineCommandKind.AskJob,A=onlineNpc,Entity=new OnlineId(identity.Town,identity.Serial)});
        }
    }
    void OnlineStep(ControllerState pad)
    {
        if(resourcesReady)RefreshResidencyDemand(Clock.Timestamp);
        onlineMovement=default;moveClip=-1;tick++;
        if(tick%30==0||!weatherClockStarted){ReadWeatherClock();UpdateWeather();}
        if(!resourcesReady||room==0&&!terrain.Ready)return;
        if(screen=="world"&&!session.Online!.Reviewing&&!session.Online.Recovering)
        {
            float x=pad.Stick.X,y=pad.Stick.Y;
            if(x*x+y*y<PlayerMovement.DeadZone*PlayerMovement.DeadZone){int held=(int)pad.HeldButtons;x=((held&2)!=0?1:0)-((held&1)!=0?1:0);y=((held&8)!=0?1:0)-((held&4)!=0?1:0);}
            if(room!=0){indoorCamera.Configure(BuildingWorld.Definition(state,BuildingWorld.Building(room)));indoorCamera.Tick(pad.CStick.X,pad.L.WasPressed);indoorCamera.Movement(x,y,out x,out y);}
            onlineMovement=new OnlineMovementInput((sbyte)Math.Clamp((int)(x*127),-127,127),(sbyte)Math.Clamp((int)(-y*127),-127,127),pad.B.IsHeld);
            float beforeX=state.X,beforeZ=state.Z;
            if(OnlineMovementRules.Move(state,room,onlineMovement,1f/30,ref state.X,ref state.Z,out float yaw))
            {facingYaw=yaw;PlayerMovement.Target(onlineMovement.X,onlineMovement.Z,out faceX,out faceZ);moveClip=pad.B.IsHeld?1:0;}
            RecordFallFootstep(beforeX,beforeZ);
        }
        state.Facing=facingYaw;cachedPlayerHeight=PlayerHeight();if(room==0)RenderView.Prepare(state.X,state.Z,cachedPlayerHeight);
        StepActorAnimation(0,state.Species,moveClip);
        for(int actor=1;actor<BuildingWorld.Actors;actor++)if(state.NpcPresent(actor-1)&&state.Town.NpcAt(actor-1).Space==room)StepActorAnimation(actor,state.NpcSpecies(actor-1),-1);else animationClock.Reset(actor);
        if(state.Town.Traffic.Snow!=null)state.Town.Traffic.Snow.TickSnowfall(weather.Snowing(0));
    }
    bool DrawOnlineUi(Color32 ink)
    {
        var visit=session.Online;if(visit==null)return false;
        if(screen==ChatEntry.Screen){DrawChat(ink);return true;}
        if(screen=="online-letter")
        {var entry=letterEntry!;DrawTextEntry(entry,"Letter to "+visit.MailRecipientName,"Residents of this town · no presents",letterPreview,"What would you like to write?",entry.Status.Length==0?"Your draft stays here if sending fails.":entry.Status,entry.Status.Length>0,ink);return true;}
        if(screen=="map"){DrawMap();return true;}
        if(screen=="online-wait")
        {
            Panel(44,24,552,432);Heading("A moment, please",74,48,ink,1.1f);Text("Owner review in progress",76,99,ink,1);
            Text("The owner is reviewing changes to the town.\nYour visit will resume when review ends.\nYour purchases and earnings are safe.",76,160,ink,1.15f);Text("A: return home",76,410,ink,1);return true;
        }
        if(screen=="online-price")
        {Panel(44,24,552,432);Heading("Choose a price",74,48,ink,1.1f);Text(UiFont.Money(onlinePrice),76,170,ink,1.5f);Text(onlineStatus,76,300,ink,1);Text("Left/Right: digit   Up/Down: amount\nA: confirm   B: back",76,380,ink,1);return true;}
        if(screen=="online-list")
        {
            Panel(44,24,552,432);Heading(onlineTitle,74,48,ink,1.05f);Text(onlineDescription.Get(onlineDetail),76,105,ink,1);
            int first=onlineSelected/4*4;
            for(int row=0;row<4&&first+row<onlineRows;row++)
            {int i=first+row;if(i==onlineSelected)Rounded(64,204+row*46,512,40,16,new Color32(221,231,188));Text(onlineLabels[i]??"",84,212+row*46,ink,1.15f);}
            if(onlineStatus.Length>0)Text(onlineStatusText.Get(onlineStatus),76,395,ink,.88f);Text("A: choose   B: back",76,429,ink,.95f);return true;
        }
        if(screen=="world")
        {
            int first=Math.Max(0,visit.State.ChatCount-3);
            int y=60;
            for(int i=first;i<visit.State.ChatCount;i++)
            {
                var line=visit.State.ChatAt(i);int at=i-first;var wrapped=onlineChatText[at];
                if(onlineChatBodies[at]!=line.Text||onlineChatSpeakers[at]!=line.Name)
                {onlineChatBodies[at]=line.Text;onlineChatSpeakers[at]=line.Name;wrapped.Set(line.Name+": "+line.Text);}
                int lines=Math.Min(3,wrapped.Lines);Text(wrapped.Window(0,lines),25,y,ink,.85f);y+=lines*22+4;
            }
            Text("R: chat   X: pockets   Z: online",25,443,ink,.85f);return true;
        }
        return true;
    }
}
#endif
