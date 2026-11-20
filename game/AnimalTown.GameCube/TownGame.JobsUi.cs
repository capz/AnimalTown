using System;
using AnimalTown;
using AnimalTown.Jobs;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Timing;

internal sealed partial class TownGame
{
    private bool jobWritingDialogue;
    private int jobMode,jobChoice,jobNpc=-1,jobRowsCount,jobSelected,jobScroll;
    private JobIdentity jobSubject;
    private int jobExpectedRevision,jobUiRevision;
    private readonly MenuNavigation jobNavigation=new MenuNavigation();
    private readonly int[] jobRows=new int[216],jobPocketRows=new int[30];
    private readonly string[] jobRowLabels=new string[216];
    private readonly WrappedText jobDetailText=new WrappedText(43);
    private string jobDetail="";
    private bool JobsLive=>state.Town.Jobs.Initialized&&state.ResidentJobs.Enabled;
    private void InitializeJobs()
    {
        if(session.Draft!=null)return;
        ulong nonce=unchecked((ulong)Clock.Timestamp^((ulong)(uint)state.Town.Seed<<32)^0x9e3779b97f4a7c15UL);
        nonce=unchecked((nonce^(nonce>>30))*0xbf58476d1ce4e5b9UL);nonce=unchecked((nonce^(nonce>>27))*0x94d049bb133111ebUL);nonce^=nonce>>31;
        state.ResidentJobs.Enable(nonce==0?1:nonce,JobNow);state.ResidentJobs.Travel=new RememberedTownRoutes(state.Town.Jobs);
    }
    private string JobNpcName(JobIdentity id)
    {int npc=state.Town.Jobs.FindNpc(id);return npc>=0?state.Town.Residents[npc].Name:state.Town.Jobs.KnownName(id);}
    private string JobPlayerName(JobIdentity id)
    {int player=state.Town.Jobs.FindPlayer(id);return player>=0?state.ProfileAt(player).Name:"another villager";}
    private static string JobTitle(JobTemplate template)
    {
        switch(template){
            case JobTemplate.DeliveryClothing:return "Shirt delivery";case JobTemplate.DeliveryForeign:return "Delivery to another town";
            case JobTemplate.DeliveryMoved:return "Delivery to a former neighbour";case JobTemplate.DeliveryProperty:return "Property delivery";
            case JobTemplate.RetrievalBorrowed:return "Borrowed possession";case JobTemplate.FavorFruit:return "Fruit for a neighbour";
            case JobTemplate.FavorBall:return "Bring a ball";case JobTemplate.FavorSnowman:return "Build a snowman";
            case JobTemplate.FavorFlowers:return "Plant flowers";case JobTemplate.FavorFish:return "Bring a fish";
            case JobTemplate.FavorInsect:return "Bring an insect";default:return "Write a letter";
        }
    }
    private string JobDescription(VillagerJobRecord job)
    {
        string who=JobNpcName(job.Requester),target=JobNpcName(job.Target),what=Catalog.Valid(job.Item)?Catalog.Items[job.Item].Name:"possession";
        if(job.Status==JobStatus.CompletedRetained)return JobPlayerName(job.Claimant)+" has handled this favour. "+who+" will have new work after "+JobCalendar.Display(job.RetainedUntil)+".";
        if(job.Status==JobStatus.ReplyOwed)return "Your reply from "+who+" is ready. Speak to them when you have mailbox space.";
        if(job.Status==JobStatus.RewardOwed)return "Your reward is waiting. Speak to "+target+" when you have room.";
        string text=job.Template==JobTemplate.RetrievalBorrowed?(job.Status==JobStatus.Returning?"Return the borrowed "+what+" to "+who+".":"Ask "+target+" about "+who+"'s "+what+"."):
            job.Template<=JobTemplate.DeliveryProperty?"Take the parcel to "+target+".":
            job.Template==JobTemplate.FavorFruit?"Bring "+who+" a "+what+".":
            job.Template==JobTemplate.FavorFish?"Bring "+who+" an ordinary fish.":job.Template==JobTemplate.FavorInsect?"Bring "+who+" an ordinary insect.":
            job.Template==JobTemplate.FavorFlowers?"Add four net flowers in "+who+"'s home acre, then report.":
            job.Template==JobTemplate.FavorBall?job.Status==JobStatus.ReadyToReport?"The ball reached "+who+". Report back.":"Roll a ball into "+who+", then report back.":
            job.Template==JobTemplate.FavorSnowman?job.Status==JobStatus.ReadyToReport?"Your snowman is ready. Report to "+who+".":"Build a new snowman in "+who+"'s home acre, then report.":"Send a real letter to "+who+". The first resident sender receives the reply.";
        return text+" Due "+JobCalendar.Display(job.DueAt)+".";
    }
    private void JobSay(string text,int mode=3)
    {
        jobWritingDialogue=true;try{Say(jobNpc>=0?state.Town.Residents[jobNpc].Name:"Errands",text);}finally{jobWritingDialogue=false;}
        jobMode=mode;jobChoice=0;
    }
    private void ResetJobConversation()
    {if(JobsLive)state.ResidentJobs.EndConversation();jobMode=0;jobNpc=-1;}
    private void CloseJobDialogue()
    {
        dialogueVoice.Stop();ResetJobConversation();screen=returnToMenu?"menu":"world";
        if(returnToMenu){menuTab=returnMenuTab;selected=returnMenuSelection;}
    }
    private bool TryJobConversation(int npc)
    {
        if(!JobsLive)return false;
        if(state.Tutorial<7){jobNpc=npc;if(state.ResidentJobs.BeginOpeningConversation(npc))JobSay(state.Talk(npc));else Say(state.Town.Residents[npc].Name,"Let’s speak once the doorway is clear.");return true;}
        if(villagers.Doors.Busy(npc+1)||!state.ResidentJobs.BeginConversation(npc,out _)){Say(state.Town.Residents[npc].Name,"I'm busy just now. Let's chat a little later.");return true;}
        jobNpc=npc;var patience=state.Town.JobState.Resident(npc);JobSay(patience.ConversationCount>=JobRulesV1.Personality(patience.Personality).MildAnnoyanceCount?"Back again? Let’s keep this a little shorter. What is it?":"Good to see you! What brings you over?",1);return true;
    }
    private void ShowJobOffer(JobIdentity id)
    {
        var offer=state.Town.Jobs.Offer(state.ActiveProfile,jobNpc);if(offer.Id!=id){JobSay("That errand is no longer available.");return;}
        jobSubject=id;jobExpectedRevision=offer.Revision;
        string target=JobNpcName(offer.Target),item=Catalog.Valid(offer.Item)?Catalog.Items[offer.Item].Name:"possession";
        string request=offer.Template==JobTemplate.DeliveryClothing?"Could you take this shirt to "+target+"?":
            offer.Template<=JobTemplate.DeliveryProperty?"Could you deliver this "+item+" to "+target+"?":
            offer.Template==JobTemplate.RetrievalBorrowed?"Could you find my "+item+"? "+target+" borrowed it.":
            offer.Template==JobTemplate.FavorFruit?"I'd love a "+item+". Could you bring me one?":
            offer.Template==JobTemplate.FavorFish?"Could you bring me a fish? Any ordinary fish will do.":
            offer.Template==JobTemplate.FavorInsect?"I'd love to see an insect. Could you bring me one?":
            offer.Template==JobTemplate.FavorFlowers?"Would you add four flowers to my home acre?":
            offer.Template==JobTemplate.FavorBall?"Would you roll a ball over to me?":
            offer.Template==JobTemplate.FavorSnowman?"Could you build a snowman in my home acre?":"Would you write me a letter? You can send it at town hall.";
        int days=JobRulesV1.Definition(offer.Template).DeadlineDays;JobSay(request+" You'll have "+days+" day"+(days==1?"":"s")+" after accepting.",2);
    }
    private string JobResultText(JobOutcome outcome)
    {
        if(outcome.Result==JobResult.Ok&&outcome.Item==JobContent.Letter)return "Your recovered present is waiting in your mailbox.";
        switch(outcome.Result){
            case JobResult.FullPockets:return outcome.Item!=0?"Your pockets are full. I'll keep your "+Catalog.Items[outcome.Item].Name+" ready.":"Make a little room in your pockets. This errand will wait for you.";
            case JobResult.FullMailbox:return "Your mailbox is full. I'll keep the same reply ready for you.";
            case JobResult.FullOffers:return "I am keeping several visitors’ offers safe. Please finish an existing offer first.";
            case JobResult.FullJobs:case JobResult.FullRewards:return "Finish an errand or collect a waiting reward first. I'll keep this offer for you.";
            case JobResult.NoWork:return "Nothing comes to mind just now. Try exploring a little and ask me again.";
            case JobResult.AlreadyClaimed:return "Another villager is handling that. Their reward will stay safe.";
            case JobResult.OfferInvalidated:return "Things have changed, so that errand won't work now. Please ask me again later.";
            case JobResult.WrongItem:return "That isn't the item for this errand.";
            case JobResult.WrongNpc:return "That parcel is addressed to someone else.";
            case JobResult.NotReady:return "Not quite yet. Have another look at your errand details.";
            case JobResult.InvalidContext:return "Let's speak in person again before continuing.";
            case JobResult.Ok:return outcome.Amount>0?"Thank you! Here are "+UiFont.Money(outcome.Amount)+" for you.":outcome.Item!=0?"Thank you! Your "+Catalog.Items[outcome.Item].Name+" is ready.":"All taken care of. Thank you!";
            default:return "That errand is no longer available. Your belongings are safe.";
        }
    }
    private void HandleJobOutcome(JobOutcome outcome)
    {
        if(outcome.Result==JobResult.ExistingOffer){ShowJobOffer(outcome.Job);return;}
        if(outcome.Result==JobResult.ExistingJob&&state.Town.Jobs.FindReward(outcome.Job)>=0){JobSay("I have something ready for you. Let's take care of your errands first.",1);return;}
        if(outcome.Result==JobResult.ExistingJob||outcome.Result==JobResult.Retained){int at=state.Town.Jobs.FindJob(outcome.Job);if(at>=0){JobSay(JobDescription(state.Town.Jobs.Job(at)));return;}}
        JobSay(JobResultText(outcome));
    }
    private void OpenJobLog(bool npcOnly=false)
    {
        dialogueVoice.Stop();jobRowsCount=0;jobSelected=0;jobScroll=0;
        var book=state.Town.Jobs;var owner=book.Player(state.ActiveProfile);var npc=jobNpc>=0?book.Npc(jobNpc):default;
        for(int i=0;i<OfflineJobLedger.RewardCapacity;i++){var r=book.Reward(i);if(r.State!=JobRewardState.Owed||r.Owner!=owner||npcOnly&&r.Giver!=npc)continue;jobRows[jobRowsCount]=1000+i;jobRowLabels[jobRowsCount++]=(r.IsReply?"Reply from ":"Reward from ")+JobNpcName(r.Giver);}
        for(int i=0;i<OfflineJobLedger.JobCapacity;i++){var j=book.Job(i);if(!j.Occupying||!j.Shared&&j.Owner!=owner||npcOnly&&j.Requester!=npc&&j.Target!=npc)continue;
            if(j.Status==JobStatus.RewardOwed||j.Status==JobStatus.ReplyOwed)continue;
            jobRows[jobRowsCount]=i;jobRowLabels[jobRowsCount++]=JobTitle(j.Template)+(j.Status==JobStatus.ReadyToReport?" · Ready":" · "+JobNpcName(j.Shared?j.Requester:j.Target));}
        if(!npcOnly)for(int i=0;i<OfflineJobLedger.TerminalCapacity;i++){var end=book.Terminal(i);if(end.Job.IsEmpty||end.Owner!=owner||end.Reason==JobEndReason.Success)continue;jobRows[jobRowsCount]=2000+i;jobRowLabels[jobRowsCount++]=JobTitle(end.Template)+" · "+EndReason(end.Reason);}
        jobUiRevision=book.Revision;screen=npcOnly?"job-actions":"job-log";RefreshJobDetail();
    }
    private static string EndReason(JobEndReason reason)=>reason==JobEndReason.Expired?"Expired":reason==JobEndReason.Abandoned?"Abandoned":reason==JobEndReason.SeasonClosed?"Season ended":reason==JobEndReason.BrokenBinding?"Parcel recovered":"World changed";
    private void RefreshJobDetail()
    {
        if(jobRowsCount==0){jobDetail="No errands right now. Speak to a neighbour and ask for work.";return;}
        int row=jobRows[jobSelected];if(row>=2000){var end=state.Town.Jobs.Terminal(row-2000);jobDetail=EndReason(end.Reason)+" · "+JobCalendar.Display(end.At)+". "+(end.Reason==JobEndReason.WorldRemoved||end.Reason==JobEndReason.BrokenBinding||end.Reason==JobEndReason.SeasonClosed?"The errand ended without blame.":"Speak to a neighbour when you are ready for more work.");return;}
        if(row>=1000&&state.Town.Jobs.PermanentlyRemoved(state.Town.Jobs.Reward(row-1000).Giver)){jobDetail="Your earned reward is safe. Visit town hall, then press A here to collect it.";return;}
        jobDetail=row>=1000?"Speak to "+JobNpcName(state.Town.Jobs.Reward(row-1000).Giver)+" to collect your waiting "+(state.Town.Jobs.Reward(row-1000).IsReply?"reply.":"reward."):JobDescription(state.Town.Jobs.Job(row));
    }
    private void ActivateNpcJobRow()
    {
        if(jobRowsCount==0)return;int row=jobRows[jobSelected];
        if(row>=1000){var r=state.Town.Jobs.Reward(row-1000);HandleJobOutcome(state.ResidentJobs.Execute(state.ResidentJobs.Command(r.IsReply?JobCommandKind.LetterFollowup:JobCommandKind.Collect,r.Id)));return;}
        var j=state.Town.Jobs.Job(row);jobSubject=j.Id;jobExpectedRevision=j.Revision;
        if(j.Shared&&(j.Template==JobTemplate.FavorFruit||j.Template==JobTemplate.FavorFish||j.Template==JobTemplate.FavorInsect)&&j.Status==JobStatus.Active){OpenJobSelector(j);return;}
        HandleJobOutcome(state.ResidentJobs.Execute(state.ResidentJobs.Command(JobCommandKind.Handoff,j.Id,j.Revision)));
    }
    private void OpenJobSelector(VillagerJobRecord job)
    {
        if(!state.ResidentJobs.BeginSupplySelection(job.Id)){JobSay("That handoff isn't available right now.");return;}
        jobRowsCount=0;jobSelected=0;
        for(int i=0;i<state.Capacity;i++){int item=state.Pockets[i];if(state.IsJobItem(i))continue;bool eligible=job.Template==JobTemplate.FavorFruit?item==job.Item:job.Template==JobTemplate.FavorFish?Catalog.Has(item,ItemFlags.Fish):Catalog.Has(item,ItemFlags.Insect);if(eligible)jobPocketRows[jobRowsCount++]=i;}
        jobDetail=job.Template==JobTemplate.FavorFruit?"Choose the requested fruit.":"Choose an ordinary "+(job.Template==JobTemplate.FavorFish?"fish":"insect")+". Cancelling this handoff costs one friendship point.";
        screen="job-select";
    }
    private bool JobInput(ControllerState p)
    {
        if(!JobsLive)return false;
        if((screen=="job-log"||screen=="job-actions")&&jobUiRevision!=state.Town.Jobs.Revision){int previous=jobSelected;OpenJobLog(screen=="job-actions");jobSelected=Math.Min(previous,Math.Max(0,jobRowsCount-1));RefreshJobDetail();}
        bool dialogue=screen=="dialogue"&&jobMode!=0;
        if(!dialogue&&!screen.StartsWith("job-")){
            if(screen=="item"&&state.IsJobItem(slot)){
                if(p.B.WasPressed){screen="menu";menuTab=0;selected=slot;return true;}
                int dir=jobNavigation.Read(screen,0,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
                if(dir==MenuNavigation.Up||dir==MenuNavigation.Down)selected=1-selected;
                if(Press(p)){if(selected==1){screen="menu";menuTab=0;selected=slot;}else{int at=FindBoundJob(slot);jobNpc=-1;JobSay(at>=0?JobDescription(state.Town.Jobs.Job(at)):"This parcel belongs to an errand.");returnToMenu=true;returnMenuTab=0;returnMenuSelection=slot;}}
                return true;
            }
            return false;
        }
        int direction=jobNavigation.Read(screen,jobMode,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,true);
        bool up=direction==MenuNavigation.Up,down=direction==MenuNavigation.Down;
        if(dialogue){
            if(typeAt<message.Length){if(Press(p)||p.B.WasPressed){dialogueVoice.Stop();typeAt=message.Length;}return true;}
            if(p.B.WasPressed){CloseJobDialogue();return true;}
            if(jobMode==3){if(Press(p))CloseJobDialogue();return true;}
            int count=jobMode==2?2:state.ResidentJobs.BestInteraction(jobNpc).Result==JobResult.NoWork?3:4;
            if(up)jobChoice=(jobChoice+count-1)%count;if(down)jobChoice=(jobChoice+1)%count;
            if(!Press(p))return true;
            if(jobMode==2){var result=state.ResidentJobs.Execute(state.ResidentJobs.Command(jobChoice==0?JobCommandKind.Accept:JobCommandKind.Decline,jobSubject,jobExpectedRevision));if(result.Result==JobResult.Ok&&jobChoice==0){int at=state.Town.Jobs.FindJob(result.Job);JobSay(at>=0?JobDescription(state.Town.Jobs.Job(at)):JobResultText(result));}else HandleJobOutcome(result);return true;}
            bool existing=count==4;int choice=jobChoice;
            if(existing&&choice==0){OpenJobLog(true);return true;}if(existing)choice--;
            if(choice==0)HandleJobOutcome(state.ResidentJobs.Execute(state.ResidentJobs.Command(JobCommandKind.Ask)));
            else if(choice==1)JobSay(state.Town.JobState.Resident(jobNpc).CanStartConversation?"A little time outside always does me good, "+state.Town.Residents[jobNpc].Phrase+".":"I’ll handle an errand, but I need a breather before we chat again.");
            else CloseJobDialogue();return true;
        }
        if(screen=="job-abandon"){
            if(p.B.WasPressed){OpenJobLog();return true;}if(up||down)jobChoice=1-jobChoice;
            if(Press(p)){if(jobChoice==1){var result=state.ResidentJobs.Execute(state.ResidentJobs.Command(JobCommandKind.Abandon,jobSubject));jobNpc=-1;JobSay(JobResultText(result));}else OpenJobLog();}return true;
        }
        if(screen=="job-select"){
            if(p.B.WasPressed){int at=state.Town.Jobs.FindJob(jobSubject);if(at>=0&&state.Town.Jobs.Job(at).Template!=JobTemplate.FavorFruit)state.ResidentJobs.Execute(state.ResidentJobs.Command(JobCommandKind.SupplyCancel,jobSubject));JobSay("We can try that handoff another time.");return true;}
            int count=Math.Max(1,jobRowsCount);if(up)jobSelected=(jobSelected+count*5-5)%count;if(down)jobSelected=(jobSelected+5)%count;if(direction==MenuNavigation.Left)jobSelected=(jobSelected+count-1)%count;if(direction==MenuNavigation.Right)jobSelected=(jobSelected+1)%count;
            if(Press(p)&&jobRowsCount>0)HandleJobOutcome(state.ResidentJobs.Execute(state.ResidentJobs.Command(JobCommandKind.Handoff,jobSubject,jobExpectedRevision,jobPocketRows[jobSelected])));return true;
        }
        bool npcScreen=screen=="job-actions";
        if(p.B.WasPressed||p.Start.WasPressed){if(npcScreen)JobSay("Anything else on your mind?",1);else{screen="menu";menuTab=1;selected=0;}return true;}
        int rows=Math.Max(1,jobRowsCount);if(up)jobSelected=(jobSelected+rows-1)%rows;if(down)jobSelected=(jobSelected+1)%rows;
        if(jobSelected<jobScroll)jobScroll=jobSelected;if(jobSelected>=jobScroll+6)jobScroll=jobSelected-5;if(up||down)RefreshJobDetail();
        if(Press(p)&&npcScreen)ActivateNpcJobRow();
        else if(Press(p)&&jobRowsCount>0&&jobRows[jobSelected]>=1000&&jobRows[jobSelected]<2000){var r=state.Town.Jobs.Reward(jobRows[jobSelected]-1000);if(state.Town.Jobs.PermanentlyRemoved(r.Giver)){jobNpc=-1;if(state.Space!=25)JobSay("Visit town hall to collect this waiting reward.");else HandleJobOutcome(state.ResidentJobs.Execute(state.ResidentJobs.Command(JobCommandKind.RecoverReward,r.Id)));}}
        else if(p.X.WasPressed&&jobRowsCount>0&&jobRows[jobSelected]<1000){var job=state.Town.Jobs.Job(jobRows[jobSelected]);if(!job.Shared&&job.Status!=JobStatus.RewardOwed){jobSubject=job.Id;jobChoice=0;screen="job-abandon";}}
        return true;
    }
    private int FindBoundJob(int pocket)
    {var binding=state.Town.Jobs.Binding(state.ActiveProfile,pocket);for(int i=0;i<OfflineJobLedger.JobCapacity;i++){var j=state.Town.Jobs.Job(i);if(j.Occupying&&j.Binding==binding)return i;}return -1;}
    private string BackpackJobTitle(int pocket,int item)
    {int at=JobsLive&&state.IsJobItem(pocket)?FindBoundJob(pocket):-1;return at>=0?"Parcel for "+JobNpcName(state.Town.Jobs.Job(at).Target):Catalog.Items[item].Name;}
    private bool DrawJobUi(Color32 ink)
    {
        if(!JobsLive)return false;
        if(screen=="dialogue"&&jobMode!=0){
            SpeechBubble();if(jobMode==3||typeAt<message.Length)return true;
            bool offer=jobMode==2,existing=!offer&&state.ResidentJobs.BestInteraction(jobNpc).Result!=JobResult.NoWork;int count=offer?2:existing?4:3;
            float height=24+count*27,y=300-height;Panel(360,y,238,height);
            for(int i=0;i<count;i++){string label=offer?(i==0?"I'll do it":"No thanks"):existing&&i==0?"My errands...":(i-(existing?1:0))==0?"Got any work?":(i-(existing?1:0))==1?"Let's chat":"See you";if(i==jobChoice)Rounded(370,y+10+i*27,218,25,12,new Color32(221,231,188));if(i==jobChoice)Text(">",378,y+12+i*27,ink,1.1f);Text(label,398,y+12+i*27,ink,1.1f);}return true;
        }
        if(!screen.StartsWith("job-"))return false;
        if(screen=="job-abandon"){
            Panel(110,145,420,190);Heading("Abandon this errand?",132,165,ink,1.1f);Text("The parcel is returned. Friendship falls by 3.",132,211,ink,1.05f);
            for(int i=0;i<2;i++){if(jobChoice==i)Rounded(125,252+i*28,390,25,12,new Color32(221,231,188));Text((jobChoice==i?"> ":"  ")+(i==0?"Keep the errand":"Abandon errand"),142,254+i*28,ink,1.1f);}return true;
        }
        Panel(45,65,550,360);Heading(screen=="job-select"?"Choose an item":"Errands",68,85,ink);
        if(screen=="job-select"){
            for(int i=0;i<jobRowsCount;i++){float x=85+i%5*90,y=140+i/5*44;if(i==jobSelected)Rounded(x-3,y-3,70,44,14,new Color32(232,183,86));Rounded(x,y,64,38,12,new Color32(231,228,202));int icon=Catalog.IconId(state.Pockets[jobPocketRows[i]]);itemIcons.Draw(ui,new RectF(icon%8*64,icon/8*64,64,64),new RectF(x+13,y,38,38),Color32.White);}
            if(jobRowsCount==0)Text("You have no matching items in your pockets.",72,145,ink,1.1f);
            Text(jobDetailText.Get(jobDetail),72,332,ink,1.08f);return true;
        }
        if(jobRowsCount==0)Text("No errands right now.",72,140,ink,1.2f);
        for(int i=jobScroll;i<Math.Min(jobRowsCount,jobScroll+6);i++){float y=137+(i-jobScroll)*27;if(i==jobSelected)Rounded(60,y-2,520,25,12,new Color32(221,231,188));if(i==jobSelected)Text(">",73,y,ink,1.02f);Text(jobRowLabels[i],91,y,ink,1.02f);}
        Text(jobDetailText.Get(jobDetail),72,315,ink,1.08f);
        if(screen=="job-log"&&jobRowsCount>0&&jobRows[jobSelected]<1000){var j=state.Town.Jobs.Job(jobRows[jobSelected]);if(!j.Shared){ButtonGlyph("X",427,392);Text("Abandon...",450,390,ink,1);}}
        return true;
    }
}
