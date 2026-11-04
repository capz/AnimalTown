using System;
using System.IO;
using System.Threading.Tasks;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Input;
using Dolphin.Memory;
using Dolphin.Storage;
using Dolphin.Diagnostics;
using Dolphin.Timing;
using Dolphin.Audio;
using Hypercube;

internal static class Program
{
    private static int Main()
    {
        DebugOverlay.PerformanceVisible=false;
        DebugOverlay.WriteLine("AnimalTown "+BuildInfo.Commit);
        if(!Aram.Initialize(2*1024*1024))throw new InvalidOperationException("Audio ARAM reservation failed");
        if(!Storage.MountDisc())throw new IOException("AnimalTown game disc not found");
        var game=new TownGame();long previous=Clock.Timestamp;double accumulator=0;
        while(true)
        {
            FrameProfile.Begin();long simulation=FrameProfile.Start();
            long now=Clock.Timestamp;accumulator+=(now-previous)/1000000.0;previous=now;if(accumulator>.1334)accumulator=.1334;
            Controller.Update();var pad=Controller.GetState(0);game.Input(pad);
            while(accumulator>=1.0/30){game.Step(pad);accumulator-=1.0/30;}
            FrameProfile.End(0,simulation);game.Draw();
            long delay=FrameProfile.Start();Task.Delay(1).GetAwaiter().GetResult();FrameProfile.End(13,delay);game.RecordFrame();
        }
    }
}

internal sealed class TownGame
{
    private GameState state;
    private readonly TownClock townClock=new TownClock();
    private readonly TerrainWalls terrainWalls=new TerrainWalls();
    private readonly VillagerNavigation villagers=new VillagerNavigation();
    private readonly bool[] villagersMoving=new bool[6];
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly Renderer renderer=new Renderer(GraphicsDevice.Default,128);
    private readonly Camera camera=new Camera(CylinderWorld.CameraFieldOfView,4f/3,CylinderWorld.Near,CylinderWorld.Far);
    private readonly Draw2D ui;
    private readonly UiFont font,headingFont,strongFont;
    private readonly CachedTexture wordmark,itemIcons;
    private readonly Transform worldRoot=new Transform(),transform=new Transform(),identity=new Transform();
    private readonly TerrainRenderer terrain;
    private readonly int[] visibleTrees=new int[LowPolyTrees.Capacity];
    private readonly float[] treeDistances=new float[LowPolyTrees.Capacity];
    private readonly LowPolyTrees trees;
    private readonly UiShapes uiShapes=new UiShapes();
    private readonly MapBackground mapBackground=new MapBackground();
    private readonly MenuNavigation menuNavigation=new MenuNavigation();
    private readonly WrappedText dialogueText=new WrappedText(41),itemTitleText=new WrappedText(14),saveStatusText=new WrappedText(48);
    private string wrappedDialogue="";
    private readonly RenderQueue actorQueue=new RenderQueue(7,7),structureQueue=new RenderQueue(9,9);
    private readonly Transform[] modelPoses=new Transform[16];
    private readonly BoxBatch boxes;
    private int modelUsed;
    private readonly Material plain=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    private readonly Model[] models=new Model[7];
    private readonly ModelInstance[] actors=new ModelInstance[7],props=new ModelInstance[1];
    private readonly string[] modelNames={"fox","bunny","giraffe","cat","hamster","spaniel","building"};
    private readonly string[] species={"Fox","Bunny","Giraffe","Maine coon","Hamster","Cocker spaniel"};
    private readonly Mesh cube;
    private readonly byte[] banner,icon;
    private readonly Sound syllable;
    private readonly DialogueVoice dialogueVoice;
    private MemoryCard? card;
    private string screen="title",message="",speaker="AnimalTown",saveStatus="Not saved",action="",entry="";
    private bool returnToSettings;
    private int settingsSelection;
    private int selected,slot,rotation,room,tick,day,month,menuTab,faceX,faceZ=-1,typeAt,voiceTick,keyboard;
    private int jobProgress,moveClip=-1;
    private long lastSave,sessionStart;
    private float exteriorX,exteriorZ;
    private float facingYaw=3.1415927f;
    private bool loaded,relocate,damagedSave;
    private bool indexedModels=true,batchedDrawing=true;
    private int uvDebug;
    private static readonly string[] tabs={"Backpack","Jobs","Catalog","Home","Post office","Settings"};
    private static readonly string[] itemActions={"Drop","Place (R rotates)","Plant","Eat","Wear","Equip","Bury","Open money bag","Read","Quit"};
    internal TownGame()
    {
        ui=new Draw2D(device,new Draw2DOptions{Width=640,Height=480,VertexCapacity=8192,SpriteCapacity=128});
        font=UiFont.LoadSystem();strongFont=font;headingFont=UiFont.Load("gorditas-bold");cube=Cube();
        boxes=new BoxBatch(renderer,cube,worldRoot);for(int i=0;i<modelPoses.Length;i++)modelPoses[i]=new Transform{Parent=worldRoot};
        wordmark=TextureCache.Load("dvd:/wordmark.dndtex",true);
        itemIcons=TextureCache.Load("dvd:/item-icons.dndtex",true);
        transform.Parent=worldRoot;identity.Parent=worldRoot;
        banner=File.ReadAllBytes("dvd:/savebanner.rgb5a3");icon=File.ReadAllBytes("dvd:/saveicon.rgb5a3");
        var pcm=new byte[512];for(int i=0;i<pcm.Length;i++)pcm[i]=(byte)(128+(i%24<12?25:-25)*(pcm.Length-i)/pcm.Length);
        syllable=Sound.FromPcm8(pcm,16000);syllable.Volume=.12f;dialogueVoice=new DialogueVoice(syllable);
        sessionStart=Clock.Timestamp;
        ReadCalendar();
        long seedClock=Clock.Timestamp;state=new GameState(unchecked((int)(seedClock^(seedClock>>32)^day*7919L^month*104729L)));
        LoadSave();
        state.Rollover(day,month);
        if(state.HousePlaced&&!state.CanWalk(state.X,state.Z,0))state.MoveToHouseEntrance();
        // Resource creation occurs outside render passes and instances share models.
        for(int i=0;i<models.Length;i++)
        {
            models[i]=ModelResources.Load(modelNames[i]);
            if(i<6){actors[i+1]=models[i].CreateInstance(512*1024);if(models[i].TryGetAnimation("Walking",out var clip)){actors[i+1].Animator.Play("Walking",true);actors[i+1].Animator.SetSpeed(.45f);}}
            else props[i-6]=models[i].CreateInstance(512*1024);
        }
        trees=new LowPolyTrees(worldRoot);terrain=new TerrainRenderer();SelectSpecies();lastSave=Clock.Timestamp;
        DebugOverlay.WriteLine("ANIMALTOWN READY "+BuildInfo.Commit+" native bytes "+device.NativeBytes);
    }
    private void SelectSpecies()
    { if(actors[0]!=null)actors[0].Dispose();actors[0]=models[state.Species].CreateInstance(512*1024);moveClip=-1; }
    private void LoadSave()
    {
        try
        {
            card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("AMTE","01"));
            int existing=0;var entries=card.GetEntries();for(int i=0;i<entries.Length;i++)if(SplitSaveCodec.IsFile(entries[i].Name))existing++;
            GameState? best=new SaveRepository(new CardSaveStorage(card,banner,icon)).Load();
            if(best!=null){state=best;day=Math.Max(day,state.Town.Day);month=Math.Max(month,state.Town.Month);loaded=true;saveStatus="Save loaded";}
            else if(existing>0){damagedSave=true;saveStatus="No complete save set. Backup card before recovery.";}
            else saveStatus="New town - no valid save";
        }
        catch(Exception){saveStatus="Insert memory card in Slot A to save";}
    }
    private void Save()
    {
        if(damagedSave){saveStatus="Damaged saves preserved. Use another card.";return;}
        string previousScreen=screen;screen="saving";Draw();screen=previousScreen;
        string phase="mount";
        try
        {
            if(card==null)card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("AMTE","01"));
            phase="encode";
            float px=state.X,pz=state.Z;
            if(room!=0){state.X=exteriorX;state.Z=exteriorZ;}
            try
            {
                phase="save set";
                new SaveRepository(new CardSaveStorage(card,banner,icon)).Save(state);
            }
            finally{state.X=px;state.Z=pz;}
            saveStatus="Saved / "+BuildInfo.Commit;lastSave=Clock.Timestamp;
        }
        catch(Exception error){saveStatus="Save failed ("+phase+", code "+NativeCalendar.CardError()+"). Check Slot A and retry.";DebugOverlay.WriteLine("SAVE ERROR: "+phase+" "+error.Message);card=null;}
    }
    private void Say(string name,string text){dialogueVoice.Begin();voiceTick=0;returnToSettings=screen=="menu"&&menuTab==5;if(returnToSettings)settingsSelection=selected;speaker=name;message=text;wrappedDialogue=dialogueText.Get(text);typeAt=0;screen="dialogue";}
    private bool Press(ControllerState p)=>p.A.WasPressed;
    internal void Input(ControllerState p)
    {
        bool chord=p.L.IsHeld&&p.R.IsHeld;
        if(chord&&p.Start.WasPressed){InspectTerrain();return;}
        bool navigating=screen=="menu"||screen=="keyboard"||screen=="create"||screen=="item"||screen=="sell"||screen=="shop"||screen=="store"||screen=="post"||screen=="mail"||screen=="lost";
        int direction=menuNavigation.Read(screen,screen=="menu"?menuTab:-1,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,navigating&&!chord);
        bool up=direction==MenuNavigation.Up,down=direction==MenuNavigation.Down,left=direction==MenuNavigation.Left,right=direction==MenuNavigation.Right;
        if(p.L.IsHeld&&p.R.IsHeld&&p.Z.WasPressed){FrameProfile.Enabled=!FrameProfile.Enabled;return;}
        if(p.L.IsHeld&&p.R.IsHeld&&p.Y.WasPressed){ToggleModelPath();return;}
        if(p.L.IsHeld&&p.R.IsHeld&&p.DPadDown.WasPressed){if(batchedDrawing)UiFont.Batched=!UiFont.Batched;return;}
        if(p.L.IsHeld&&p.R.IsHeld&&p.X.WasPressed){batchedDrawing=!batchedDrawing;UiFont.Batched=batchedDrawing;if(NativeRenderProbe.Batch(batchedDrawing?1:0)!=(batchedDrawing?1:0))throw new InvalidOperationException("Batch switch failed");return;}
        if(p.L.IsHeld&&p.R.IsHeld)return;
        if(screen=="title")
        { if(Press(p)){if(loaded)screen="world";else{screen="create";selected=0;}}return; }
        if(screen=="dialogue")
        { if(Press(p)||p.B.WasPressed){dialogueVoice.Stop();if(typeAt<message.Length)typeAt=message.Length;else{screen=returnToSettings?"menu":"world";if(returnToSettings){menuTab=5;selected=settingsSelection;}}}return; }
        if(screen=="keyboard")
        {
            if(left)keyboard=(keyboard+39)%40;if(right)keyboard=(keyboard+1)%40;
            if(up)keyboard=(keyboard+30)%40;if(down)keyboard=(keyboard+10)%40;
            if(Press(p)&&entry.Length<(action=="name"?16:32))entry+=Letter("ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .!?"[keyboard]);
            if(p.X.WasPressed&&entry.Length>0)entry=entry.Substring(0,entry.Length-1);
            if(p.Start.WasPressed){if(action=="name")state.Name=entry.Length>0?entry:"Newcomer";else state.Catchphrase=entry;screen="create";}
            return;
        }
        if(screen=="create")
        {
            if(up)selected=(selected+5)%6;if(down)selected=(selected+1)%6;
            if(right){if(selected==0){state.Species=(state.Species+1)%6;SelectSpecies();}else state.Palette=(state.Palette+1)%8;}
            if(Press(p))
            {
                if(selected==1||selected==2){action=selected==1?"name":"phrase";entry="";screen="keyboard";}
                else if(selected==5){screen="plot";relocate=false;}
            }
            return;
        }
        if(screen=="plot")
        {
            if(Press(p)){if(state.PlaceHouse((int)state.X-3,(int)state.Z-3,relocate))Say("Hearth","Your home is ready! Visit the store for your first job.");else Say("Hearth","This plot needs 15 x 13 clear land tiles.");}
            if(p.B.WasPressed&&state.HousePlaced)screen="world";return;
        }
        if(p.Start.WasPressed){Save();screen="menu";menuTab=5;selected=0;return;}
        if(p.Y.WasPressed){screen=screen=="map"?"world":"map";return;}
        if(p.B.WasPressed&&screen=="item"){screen="menu";menuTab=0;selected=slot;return;}
        if(p.B.WasPressed&&screen!="world"){screen="world";return;}
        if(p.X.WasPressed){screen=screen=="menu"?"world":"menu";selected=0;menuTab=0;return;}
        if(screen=="map")return;
        if(screen=="world")
        {
            if(p.Z.WasPressed){for(int i=0;i<state.Capacity;i++)if(Catalog.Has(state.Pockets[i],ItemFlags.Tool)){state.Use(i,4,0,0);break;}}
            if(Press(p))Interact();return;
        }
        if(screen=="item")
        {
            if(down)selected=(selected+1)%10;if(up)selected=(selected+9)%10;
            if(p.R.WasPressed)rotation=(rotation+1)%4;
            if(Press(p))
            {
                bool ok=false;int tx=(int)state.X+faceX,tz=(int)state.Z+faceZ;
                if(selected==0)ok=state.Use(slot,0,tx,tz);if(selected==1)ok=state.Place(slot,tx,tz,rotation,room);
                if(selected==2)ok=state.Use(slot,1,tx,tz);if(selected==3)ok=state.Use(slot,2,tx,tz);
                if(selected==4)ok=state.Use(slot,3,tx,tz);if(selected==5)ok=state.Use(slot,4,tx,tz);
                if(selected==6)ok=state.Use(slot,5,tx,tz);if(selected==7)ok=state.Use(slot,6,tx,tz);
                if(selected==8&&state.Pockets[slot]==19){Say("Letter","Thank you for being a part of our town!");return;}
                if(selected==9){screen="menu";selected=slot;return;}
                if(ok&&state.Tutorial==0&&state.Shirt==4)state.CreditTutorial(0);
                if(ok&&selected==2&&state.Tutorial==1)state.CreditTutorial(1);
                Say("Backpack",ok?"Done!":"That action is not possible here.");
            }
            return;
        }
        bool backpack=screen=="sell"||(screen=="menu"&&menuTab==0);
        if(backpack)
        {
            if(down)selected=(selected+5)%state.Capacity;if(up)selected=(selected+state.Capacity-5)%state.Capacity;
            if(right)selected=(selected+1)%state.Capacity;if(left)selected=(selected+state.Capacity-1)%state.Capacity;
            if(screen=="menu"&&p.R.WasPressed){menuTab=1;selected=0;return;}if(screen=="menu"&&p.L.WasPressed){menuTab=5;selected=0;return;}
            if(Press(p)&&state.Pockets[selected]!=0){if(screen=="sell")Say("Hearth",state.Sell(selected)?"Thank you! Acorns added to your wallet.":"I cannot buy that, or your wallet is full.");else{slot=selected;screen="item";selected=0;}}return;
        }
        int count=screen=="shop"?state.Town.StockCount:screen=="post"?7:screen=="store"?8:screen=="mail"?Math.Max(1,state.MailCount):screen=="lost"?Math.Max(1,LostCount()):TabCount();
        if(down)selected=(selected+1)%count;if(up)selected=(selected+count-1)%count;
        if(screen=="menu"&&(p.R.WasPressed||right)){menuTab=(menuTab+1)%6;selected=0;}
        if(screen=="menu"&&(p.L.WasPressed||left)){menuTab=(menuTab+5)%6;selected=0;}
        if(!Press(p))return;
        if(screen=="sell"){Say("Hearth",state.Sell(selected)?"Thank you! Acorns added to your wallet.":"I cannot buy that, or your wallet is full.");return;}
        if(screen=="shop"){Say("Hearth",state.Buy(selected)?"Thank you for shopping!":"You need enough acorns and a free pocket.");return;}
        if(screen=="store"){StoreAction();return;}
        if(screen=="post"){PostAction();return;}
        if(screen=="mail")
        { if(selected<state.MailCount&&state.Mail[selected]>0&&state.Receive(state.Mail[selected])){for(int i=selected;i<state.MailCount-1;i++)state.Mail[i]=state.Mail[i+1];state.MailCount--;Say("Mailbox","Delivery collected.");}return; }
        if(screen=="lost")
        { int index=LostIndex(selected);Say("Town hall",state.ClaimLost(index,0)?"Recovered!":"Your pockets are full, or the item is reserved.");return; }
        if(screen=="menu")MenuAction();
    }
    private int TabCount()=>menuTab==0?state.Capacity:menuTab==1?7:menuTab==2?Catalog.Items.Length-1:menuTab==3?7:menuTab==4?7:9;
    private void ToggleModelPath()
    {int next=indexedModels?0:1;if(NativeRenderProbe.Indexed(next)!=next)throw new InvalidOperationException("Model renderer switch failed");indexedModels=!indexedModels;}
    private int LostCount(){int c=0;for(int i=0;i<state.Town.GroundCount;i++)if(state.Town.Ground[i].Kind==4)c++;return c;}
    private int LostIndex(int n){for(int i=0;i<state.Town.GroundCount;i++)if(state.Town.Ground[i].Kind==4&&n--==0)return i;return -1;}
    private void StoreAction()
    {
        if(selected==7){Say("Hearth",state.UpgradeBackpack()?"A bigger backpack! You can carry "+state.Capacity+" items now.":state.BackpackTier==2?"Your backpack already holds 30 items.":"The next backpack costs "+state.BackpackUpgradePrice+" acorns.");return;}
        if(selected==0){screen="shop";selected=0;return;}
        if(selected==1){int i=state.Find(18);if(state.Tutorial==3&&i>=0){state.Pockets[i]=0;state.CreditTutorial(3);Say("Hearth","Good work! Write a letter at town hall.");return;}screen="sell";selected=0;return;}
        if(selected==2){Say("Hearth",TutorialText());return;}
        if(selected==3){Say("Hearth",state.Expand(false,day)?"Your house expansion arrives after 06:00.":"Pay off your debt before expanding.");return;}
        if(selected==4){Say("Hearth",state.Expand(true,day)?"Your garden expands after 06:00.":"Pay off your debt and meet the house tier first.");return;}
        if(selected==5){int amount=(int)Math.Min(state.Debt,state.Wallet);Say("Hearth",state.Repay(amount)?"Thank you for your repayment!":"No repayment is possible.");return;}
        if(state.Tutorial<6){Say("Hearth",TutorialText());return;}
        if(state.Jobs>=2){Say("Hearth","All jobs are done for today.");return;}
        int fruit=state.Find(1);if(fruit>=0&&state.Wallet<=99199){state.Pockets[fruit]=0;state.Wallet+=800;state.Jobs++;Say("Hearth","A peach for our customer! Here are 800 acorns.");}else Say("Hearth","Today's job: bring a peach for a customer. Reward: 800. Make space in your wallet.");
    }
    private string TutorialText()
    { string[] text={"Welcome! Wear the work shirt in your pockets.","Plant your flower seed near the store.","Deliver the parcel to a villager.","Collect "+state.Town.Residents[4].Name+"'s parcel and return it to this counter.","Write a letter at town hall.","Introduce yourself to all six neighbours.","Your part-time job is complete. Your debt is 18,000 acorns."};return text[state.Tutorial]; }
    private void PostAction()
    {
        if(selected==0){Say("Fern",state.Deposit(Math.Min(1000,state.Wallet))?"Deposit complete.":"No acorns to deposit.");return;}
        if(selected==1){Say("Fern",state.Withdraw((int)Math.Min(1000,state.Savings))?"Withdrawal complete.":"No room in wallet, or insufficient savings.");return;}
        if(selected==2){Say("Fern",state.Repay((int)Math.Min(state.Debt,state.Wallet))?"Loan repayment complete.":"No payment is possible.");return;}
        if(selected==3){screen="lost";selected=0;return;}
        if(selected==4){screen="mail";selected=0;return;}
        if(selected==5){if(state.Tutorial==4){state.CreditTutorial(4);if(state.MailCount<128)state.Mail[state.MailCount++]=-19;}Say("Fern","Your letter has been sent. A reply will arrive tomorrow.");return;}
        Say("Gate","Online visits require a Broadband Adapter and paired companion. Online transport is not yet available in this build.");
    }
    private void MenuAction()
    {
        if(menuTab==0){slot=selected;screen="item";selected=0;return;}
        if(menuTab==1){Say("Jobs",selected==0?TutorialText():state.Town.Residents[selected-1].Name+": "+(state.Town.Residents[selected-1].Request==0?"No request":"Bring "+Catalog.Items[state.Town.Residents[selected-1].Request].Name));return;}
        if(menuTab==2){Say("Catalog",state.Order(selected+1)?"Ordered! Check your mailbox after the next rollover.":"Only previously owned store items can be ordered.");return;}
        if(menuTab==3)
        {
            if(selected==0){state.Roof=(state.Roof+1)%12;Say("Home","Roof palette selected.");}
            if(selected==1){state.Exterior=(state.Exterior+1)%5;Say("Home","Exterior palette selected.");}
            if(selected==2){state.Fence=(state.Fence+1)%6;Say("Home","Fence style selected.");}
            if(selected==3){state.Mailbox=(state.Mailbox+1)%6;Say("Home","Mailbox selected.");}
            if(selected==4){relocate=true;screen="plot";}
            if(selected==5){Say("Home",state.Expand(false,day)?"Construction ordered.":"Pay off your loan first.");}
            if(selected==6){Say("Home",state.Expand(true,day)?"Garden construction ordered.":"Loan or house tier prevents this expansion.");}return;
        }
        if(menuTab==4){PostAction();return;}
        if(selected==0){Save();Say("Save",saveStatus);}else if(selected==1){Say("Controls","Stick / D-pad: walk / menus. L/R: tabs. B: run / back. A: interact / select. X: backpack. Y: map. Z: tool. START: save / settings.");}
        else if(selected==2)Say("About","AnimalTown / "+BuildInfo.Commit+" / hypercube-engine. Original homebrew for Nintendo GameCube.");
        else if(selected==3){FrameProfile.Enabled=!FrameProfile.Enabled;}
        else if(selected==4){Say("Calendar","Console RTC / day "+day+". New days begin at 06:00.");}
        else if(selected==5)Say("Chat","Quick phrases and online chat are pending the companion transport.");
        else if(selected==6)ToggleModelPath();
        else {int next=uvDebug^(selected==7?1:2);if(NativeRenderProbe.Uv(next)==next)uvDebug=next;else Say("UV display","Not enough memory to prepare UV regions. The current view is unchanged.");}
    }
    private void Interact()
    {
        int x=(int)state.X,z=(int)state.Z,tx=x+faceX,tz=z+faceZ;
        if(room!=0)
        { if(z>=3&&x<=1){room=0;state.X=exteriorX;state.Z=exteriorZ;return;}Say("Home","Use pockets to place furniture in this room.");return; }
        if(x>=28&&x<=37&&z>=27&&z<=31){screen="store";selected=0;return;}
        if(x>=46&&x<=55&&z>=27&&z<=31){screen="post";selected=0;return;}
        if(x>=32&&x<=40&&z<=12){Say("Gate","The gate awaits a Broadband Adapter and companion connection.");return;}
        if(state.HousePlaced&&x>=state.Town.HouseX+1&&x<=state.Town.HouseX+5&&z>=state.Town.HouseZ+3&&z<=state.Town.HouseZ+5)
        { exteriorX=state.X;exteriorZ=state.Z;room=1;state.X=1;state.Z=3;return; }
        for(int i=0;i<6;i++){var v=state.Town.Residents[i];if(Math.Abs(v.X-state.X)<2.8f&&Math.Abs(v.Z-state.Z)<2.8f){Say(v.Name,state.Talk(i));return;}}
        if(state.Pickup(tx,tz)||state.Pickup(x,z)){Say("Backpack","Picked up!");return;}
        int ground=state.Town.FindGround(tx,tz);
        if(ground>=0)
        {
            var g=state.Town.Ground[ground];
            if(g.Kind==3){if(state.Tool==9){g.Kind=0;g.Item=3;Say("Tree","A sapling for replanting.");}else if(state.Receive(1))Say("Tree","A ripe peach drops into your pockets!");return;}
            if(g.Kind==1&&state.Tool==7&&state.Receive(g.Item)){state.Town.RemoveGround(ground);Say("Digging","You found something!");return;}
            if(g.Kind==2&&state.Tool==8){jobProgress++;Say("Flowers","Watered with care.");return;}
        }
        if(state.Tool==5&&(state.Town.At(tx,tz)==1||state.Town.At(tx+1,tz)==1))
        { Say("Fishing",state.Receive(14)?"A river fish!":"Your pockets are full.");return; }
        if(state.Tool==6){Say("Bug net",tick%3==0&&state.Receive(15)?"Caught a butterfly!":"No butterfly in reach. Try again nearby.");return;}
        if(state.Tool==7&&state.Town.AddGround(16,tx,tz,1,day)){Say("Digging","A spot for something buried.");return;}
        Say("Meadowroll","A quiet moment. Try the store, town hall, or your neighbours.");
    }
    internal void Step(ControllerState pad)
    {
        tick++;
        if(tick%30==0)townClock.Update(NativeCalendar.ReadClock());
        if(tick%900==0){int previousDay=state.Town.Day;ReadCalendar();state.Rollover(day,month);if(state.Town.Day!=previousDay)Save();}
        if(screen=="dialogue"&&typeAt<message.Length){
            typeAt++;
            if(typeAt==message.Length)dialogueVoice.Stop();
            else if(++voiceTick%3==0&&dialogueVoice.Available&&!dialogueVoice.Pulse())DebugOverlay.WriteLine("Dialogue audio unavailable; continuing text");
        }
        if(screen=="world"||screen=="plot")
        {
            if(PlayerMovement.Move(state,room,pad.Stick.X,pad.Stick.Y,(int)pad.HeldButtons,pad.B.IsHeld,out float dx,out float dz))
            {
                facingYaw=PlayerMovement.Yaw(dx,dz);PlayerMovement.Target(dx,dz,out faceX,out faceZ);
                int clip=pad.B.IsHeld?1:0;if(clip!=moveClip){string name=clip==1?"Running":"Walking";if(models[state.Species].TryGetAnimation(name,out var _)){actors[0].Animator.Play(name,true);actors[0].Animator.SetSpeed(1.05f);}moveClip=clip;}
            }
            else if(moveClip>=0){actors[0].Animator.Stop();moveClip=-1;}
        }
        if(room==0&&screen=="world")villagers.Tick(state);
        long skinAt=FrameProfile.Start();
        if(models[state.Species].Animations.Count>0)actors[0].Update(1f/30);
        for(int i=1;i<7;i++){
            var v=state.Town.Residents[i-1];bool moving=room==0&&screen=="world"&&villagers.Routes[i-1].Moving;
            if(moving!=villagersMoving[i-1]){villagersMoving[i-1]=moving;if(moving&&models[i-1].TryGetAnimation("Walking",out var _)){actors[i].Animator.Play("Walking",true);actors[i].Animator.SetSpeed(1);}else actors[i].Animator.Stop();}
            if(moving&&Visible(v.PosX,v.PosZ,1.8f))actors[i].Update(1f/30);
        }
        FrameProfile.End(1,skinAt);
        if(Clock.Timestamp-lastSave>300000000){Save();}
    }
    private void ReadCalendar(){int date=NativeCalendar.Read();day=date&0x1ffff;month=(int)((uint)date>>17);townClock.Update(NativeCalendar.ReadClock());}
    private Vector3 SurfacePoint(float x,float z,float height=0)
    { if(room!=0)return new Vector3(x,height,z);var p=CylinderWorld.Surface(z,state.Z,height+TerrainElevation.At(state.Town,x,z));return new Vector3(x,p.Y,p.Z); }
    private float PlayerHeight()=>room==0?TerrainElevation.At(state.Town,state.X,state.Z):0;
    private Quaternion SurfaceRotation(float z,float yaw=0)
    {
        float halfPitch=room==0?(z-state.Z)/CylinderWorld.Radius*.5f:0;
        float sx=(float)Math.Sin(halfPitch),cx=(float)Math.Cos(halfPitch),sy=(float)Math.Sin(yaw*.5f),cy=(float)Math.Cos(yaw*.5f);
        return new Quaternion(sx*cy,cx*sy,sx*sy,cx*cy);
    }
    private static Mesh Cube()
    {
        float[] p={0,0,0,1,0,0,1,1,0,0,1,0,0,0,1,1,0,1,1,1,1,0,1,1};
        int[] ids={0,2,1,0,3,2,4,5,6,4,6,7,0,4,7,0,7,3,1,2,6,1,6,5,3,7,6,3,6,2,0,1,5,0,5,4};
        // GX front faces use the same winding as the terrain triangles.
        var v=new VertexPositionColor[36];for(int i=0;i<36;i++){int a=ids[i%3==0?i:i%3==1?i+1:i-1]*3;v[i]=new VertexPositionColor(new Vector3(p[a],p[a+1],p[a+2]),Color32.White);}return Mesh.Create(v);
    }
    private void Box(float x,float y,float z,float w,float h,float d,Color32 color)
    {
        var origin=SurfacePoint(x,z,y);var pose=room==0?CylinderWorld.Surface(z,state.Z):new CylinderPose(0,z,1,0,0);
        // Bound the actual rigidly rotated box, rather than a different point
        // on the curved ground half a box-depth farther along the cylinder.
        var center=new Vector3(x+w*.5f,origin.Y+h*.5f*pose.UpY-d*.5f*pose.UpZ,origin.Z+h*.5f*pose.UpZ+d*.5f*pose.UpY);
        float radius=(float)Math.Sqrt(w*w+h*h+d*d)*.5f+.05f;
        if(!RenderView.Sphere(center.X,center.Y,center.Z,radius,state.X,state.Z,PlayerHeight())||(room==0&&!RenderView.AboveHorizon(center.Y,center.Z,radius,state.Z,PlayerHeight()))){FrameProfile.CulledObjects++;return;}
        if(batchedDrawing)boxes.Add(origin,new Vector3(w,h,d),SurfaceRotation(z),color);
        else{transform.LocalPosition=origin;transform.LocalScale=new Vector3(w,h,d);transform.LocalRotation=SurfaceRotation(z);plain.Color=color;renderer.DrawMesh(cube,plain,transform);}
    }
    private bool Visible(float x,float z,float scale)
    {var center=SurfacePoint(x,z,scale*.5f);float radius=scale*1.5f;return RenderView.Sphere(center.X,center.Y,center.Z,radius,state.X,state.Z,PlayerHeight())&&(room!=0||RenderView.AboveHorizon(center.Y,center.Z,radius,state.Z,PlayerHeight()));}
    private void ModelAt(ModelInstance m,float x,float z,float scale,float facing=0)
    {
        if(!Visible(x,z,scale)){FrameProfile.CulledObjects++;return;}
        var pose=batchedDrawing?modelPoses[modelUsed++]:transform;pose.LocalPosition=SurfacePoint(x,z);pose.LocalScale=new Vector3(scale,scale,scale);pose.LocalRotation=SurfaceRotation(z,facing);
        if(batchedDrawing){if(m==props[0])structureQueue.Submit(m,pose);else actorQueue.Submit(m,pose);}else renderer.DrawModel(m,pose);
    }
    internal void Draw()
    {
        long phase=FrameProfile.Start();terrain.Prepare(state,room,month);terrainWalls.Prepare(state,room);FrameProfile.End(2,phase);
        phase=FrameProfile.Start();uiShapes.Prepare(screen,screen=="menu"?menuTab:-1,batchedDrawing);if(batchedDrawing&&screen=="map")mapBackground.Prepare(state.Town);modelUsed=0;FrameProfile.End(8,phase);
        phase=FrameProfile.Start();device.BeginFrame(new Color32(149,198,186));
        float eyeHeight=PlayerHeight();camera.LookAt(new Vector3(state.X,CylinderWorld.CameraHeight+eyeHeight,state.Z+CylinderWorld.CameraBack),new Vector3(state.X,eyeHeight,state.Z-CylinderWorld.CameraAimAhead),new Vector3(0,1,0));renderer.Begin3D(camera);FrameProfile.End(4,phase);
        phase=FrameProfile.Start();terrain.Draw(renderer,identity,room);terrainWalls.Draw(renderer,identity);FrameProfile.End(4,phase);phase=FrameProfile.Start();
        if(room==0)
        {
            for(int i=0;i<6;i++){var v=state.Town.Residents[i];ModelAt(actors[i+1],v.PosX,v.PosZ,1.8f,v.Yaw);}
            if(batchedDrawing)actorQueue.Execute(renderer);
            FrameProfile.End(5,phase);phase=FrameProfile.Start();DrawTrees();FrameProfile.End(6,phase);phase=FrameProfile.Start();
            long groundAt=FrameProfile.Start();
            for(int i=0;i<state.Town.GroundCount;i++)
            {
                var g=state.Town.Ground[i];if(g.Kind==4)continue;
                float y=0;
                if(!IsTree(g))Box(g.X+.25f,y+.02f,g.Z+.25f,.5f,g.Kind==1?.03f:.25f,.5f,g.Kind==1?new Color32(105,81,54):g.Kind==2?new Color32(242,174,170):new Color32(229,190,82));
            }
            if(batchedDrawing)boxes.Flush();FrameProfile.End(3,groundAt);
            DrawBuilding(32,26,4);DrawBuilding(50,26,4);for(int i=0;i<6;i++)DrawBuilding(state.Town.Residents[i].HomeX+1,state.Town.Residents[i].HomeZ+1,2.3f);
            if(state.HousePlaced)DrawBuilding(state.Town.HouseX+3,state.Town.HouseZ+2,2.5f);
            if(batchedDrawing)structureQueue.Execute(renderer);
            if(state.HousePlaced)
            {
                int w=GameState.GardenWidths[state.GardenTier],h=GameState.GardenHeights[state.GardenTier];
                for(int i=0;i<w;i++){FenceAt(state.Town.HouseX+i,state.Town.HouseZ);if(i!=3)FenceAt(state.Town.HouseX+i,state.Town.HouseZ+h-1);}
                for(int j=1;j<h;j++){FenceAt(state.Town.HouseX,state.Town.HouseZ+j);FenceAt(state.Town.HouseX+w-1,state.Town.HouseZ+j);}
            }
        }
        for(int i=0;i<state.Town.PlacedCount;i++){var q=state.Town.Placed[i];if(q.Room==room)Box(q.X,0,q.Z,.7f,.7f,.7f,new Color32(176,119,81));}
        if(batchedDrawing)boxes.Flush();
        FrameProfile.End(7,phase);phase=FrameProfile.Start();
        ModelAt(actors[0],state.X,state.Z,1.8f,facingYaw);
        if(batchedDrawing)actorQueue.Execute(renderer);
        FrameProfile.End(5,phase);phase=FrameProfile.Start();
        if(screen=="plot")
        { int x=(int)state.X-3,z=(int)state.Z-3;bool valid=state.Town.ValidPlot(x,z);for(int i=0;i<15;i++){Box(x+i,.02f,z,.9f,.03f,.9f,valid?new Color32(74,230,113):new Color32(229,82,72));Box(x+i,.02f,z+12,.9f,.03f,.9f,valid?new Color32(74,230,113):new Color32(229,82,72));} }
        if(batchedDrawing)boxes.Flush();renderer.End3D();ui.Begin();DrawHud();FrameProfile.End(8,phase);FrameProfile.Draw(ui);
        phase=FrameProfile.Start();ui.End();FrameProfile.End(8,phase);
        phase=FrameProfile.Start();device.EndFrame();FrameProfile.End(9,phase);
    }
    internal void RecordFrame(){if(FrameProfile.Enabled){int scene=screen=="map"?10:screen=="menu"&&menuTab==0?(indexedModels?1:7):room!=0?2:screen=="world"?(indexedModels?0:6):3;FrameProfile.Complete(GpuProfiler.Snapshot().Triangles,terrain.VisibleTriangles,terrain.RejectedQuads,batchedDrawing?(UiFont.Batched?scene:scene+32):scene+16);}}
    private bool IsTree(GroundItem g)=>g.Kind==3||(g.Kind==2&&g.Item!=2);
    private void InspectTerrain()
    {
        if(room!=0)return;
        for(int z=2;z<Town.Height-2;z+=2)for(int x=2;x<Town.Width-2;x+=2)if(TerrainElevation.Mode(state.Town,x,z)==TerrainElevation.RampNorth){
            state.X=x+.5f;state.Z=z+2.5f;screen="world";facingYaw=(float)Math.PI;moveClip=-1;actors[0].Animator.Stop();return;
        }
    }
    private void DrawTrees()
    {
        // Generated low-poly meshes share native geometry and indexed GX draws.
        long selectionAt=FrameProfile.Start();
        for(int n=0;n<visibleTrees.Length;n++){visibleTrees[n]=-1;treeDistances[n]=float.MaxValue;}
        for(int i=0;i<state.Town.GroundCount;i++)
        {
            var g=state.Town.Ground[i];if(!IsTree(g)||!Visible(g.X,g.Z,g.Kind==2?.75f:3))continue;
            float dx=g.X-state.X,dz=g.Z-state.Z,d=dx*dx+dz*dz;
            for(int n=0;n<visibleTrees.Length;n++)if(d<treeDistances[n])
            {for(int j=visibleTrees.Length-1;j>n;j--){visibleTrees[j]=visibleTrees[j-1];treeDistances[j]=treeDistances[j-1];}visibleTrees[n]=i;treeDistances[n]=d;break;}
        }
        FrameProfile.End(15,selectionAt);
        trees.Draw(renderer,state.Town,visibleTrees,state.Z,month);
    }
    private void DrawBuilding(float x,float z,float scale)
    { ModelAt(props[0],x,z,scale); }
    private void FenceAt(int x,int z)
    { Box(x,0,z,.12f,.55f,.12f,new Color32(211,191,147)); }
    private void Text(string text,float x,float y,Color32 c,float scale=1.5f,int visibleCharacters=-1)=>font.Draw(ui,text,x,y,c,scale/1.5f,0,visibleCharacters);
    private void Strong(string text,float x,float y,Color32 c,float scale=1.5f)=>strongFont.Draw(ui,text,x,y,c,scale/1.5f);
    private void Heading(string text,float x,float y,Color32 c,float scale=1)=>headingFont.Draw(ui,text,x,y,c,scale,(float)Math.Sin(tick*.018f+x*.01f)*1.8f);
    private void Panel(float x,float y,float w,float h)
    { float radius=Math.Min(30,h*.5f);Bulged(x+4,y+5,w,h,radius,new Color32(49,72,62,130));Bulged(x,y,w,h,radius,new Color32(255,245,221)); }
    private void Bulged(float x,float y,float w,float h,float radius,Color32 color)
    {
        if(batchedDrawing&&uiShapes.Draw(ui,x,y,w,h,radius,true,color))return;
        Rounded(x,y,w,h,radius,color,false);if(h<=radius*2+1)return;
        float start=y+radius,span=h-2*radius;
        for(int side=0;side<2;side++)
        {
            float edge=side==0?x:x+w,direction=side==0?-1:1;
            for(int i=0;i<20;i++)
            {float a=i/20f,b=(i+1)/20f;ui.FillTriangle(new Vector2(edge-direction*2,y+h*.5f),new Vector2(edge+direction*9*(float)Math.Sin(a*3.1415927f),start+a*span),new Vector2(edge+direction*9*(float)Math.Sin(b*3.1415927f),start+b*span),color);}
        }
    }
    private void Rounded(float x,float y,float w,float h,float radius,Color32 color,bool cache=true)
    {
        if(cache&&batchedDrawing&&uiShapes.Draw(ui,x,y,w,h,radius,false,color))return;
        ui.FillRectangle(new RectF(x+radius,y,w-2*radius,h),color);
        ui.FillRectangle(new RectF(x,y+radius,radius,h-2*radius),color);
        ui.FillRectangle(new RectF(x+w-radius,y+radius,radius,h-2*radius),color);
        // Ten segments per quarter circle, batched with the rest of the bubble.
        for(int corner=0;corner<4;corner++)
        {
            float cx=x+(corner==0||corner==3?radius:w-radius),cy=y+(corner<2?radius:h-radius);
            float start=corner==0?3.1415927f:corner==1?4.712389f:corner==2?0:1.5707964f;
            for(int i=0;i<10;i++){float a=start+i*.15707964f,b=a+.15707964f;ui.FillTriangle(new Vector2(cx,cy),new Vector2(cx+radius*(float)Math.Cos(a),cy+radius*(float)Math.Sin(a)),new Vector2(cx+radius*(float)Math.Cos(b),cy+radius*(float)Math.Sin(b)),color);}
        }
    }
    private void SpeechBubble()
    {
        Rounded(36,329,576,108,30,new Color32(48,65,58,130));
        Rounded(32,324,576,108,30,new Color32(255,245,221));
        ui.FillTriangle(new Vector2(112,428),new Vector2(142,428),new Vector2(118,446),new Color32(255,245,221));
        Rounded(48,299,180,38,19,new Color32(53,105,84));
        Strong(speaker,62,306,new Color32(255,245,221),1.35f);Text(wrappedDialogue,57,344,new Color32(49,72,62),1.4f,typeAt);
    }
    private void BrandMark(float x,float y)
    {
        Rounded(x-17,y-8,34,34,15,new Color32(53,105,84));
        Rounded(x-6,y+5,12,23,6,new Color32(255,245,221));
        Rounded(x-22,y-21,17,10,5,new Color32(232,183,86));Rounded(x+5,y-21,17,10,5,new Color32(232,183,86));
        Rounded(x-30,y+29,60,4,2,new Color32(195,109,84));
    }
    private static string Letter(char c)=>new string(new[]{c});
    private void DrawHud()
    {
        var ink=new Color32(49,72,62);Panel(20,18,282,36);Text("AnimalTown / "+BuildInfo.Commit,32,24,ink,.9f);
        if(FrameProfile.Enabled)Text((indexedModels?"GX indexed":"Direct")+(batchedDrawing?" / batched":" / baseline"),420,418,ink,.8f);
        Panel(410,18,210,56);Strong(state.Wallet+" acorns",427,22,ink,1.35f);Text("Loan "+state.Debt,427,47,ink,.95f);
        Rounded(20,440,420,29,14.5f,new Color32(48,76,57,210));Text("A interact   X backpack   Y map",32,451,Color32.White,1.3f);
        if(screen=="world"||screen=="plot"){Panel(20,371,170,61);Text(townClock.Date,32,380,ink,1.1f);Strong(townClock.Time,32,400,ink,1.6f);}
        if(screen=="title")
        {
            Panel(70,96,500,300);BrandMark(320,137);
            wordmark.Draw(ui,new RectF(0,0,wordmark.SourceWidth,wordmark.SourceHeight),new RectF(90,161,460,115),ink);
            const string tagline="A little life, well rooted.";Text(tagline,(640-font.Measure(tagline))/2,255,ink);
            Rounded(167,302,306,49,24.5f,new Color32(53,105,84));string prompt=loaded?"A  Continue your town":"A  Begin a new life";
            Strong(prompt,(640-strongFont.Measure(prompt))/2,312,new Color32(255,245,221));
            Text("GameCube / "+BuildInfo.Commit,187,367,ink,.9f);return;
        }
        if(screen=="dialogue")
        { SpeechBubble();return; }
        if(screen=="map"){DrawMap();return;}
        if(screen=="plot"){Panel(55,370,530,55);Text("Choose a 15 x 13 plot. A: confirm",70,385,ink);return;}
        if(screen=="keyboard")
        { Panel(60,100,520,280);Rounded(78,118,480,35,17,new Color32(231,222,190));Text(entry+"_",88,128,ink);string keys="ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789 .!?";for(int i=0;i<40;i++){Rounded(82+i%10*45,177+i/10*38,34,32,14,i==keyboard?new Color32(225,177,119):new Color32(236,226,197));Text(keys.Substring(i,1),88+i%10*45,185+i/10*38,ink,2);}Text("A type  X erase  START done",85,355,ink);return; }
        if(screen=="saving")
        { Panel(115,181,410,132);Heading("Saving your town",142,199,ink);Text("Please keep the memory\ncard inserted.",142,245,ink,1.25f);return; }
        if(screen=="create")
        { Panel(55,90,530,295);Heading("Make yourself at home",78,102,ink);string[] options={"Species: "+species[state.Species],"Name: "+state.Name,"Catchphrase: "+state.Catchphrase,"Palette: "+state.Palette,"Right: change / A: edit","Choose your house plot"};for(int i=0;i<options.Length;i++){if(selected==i)Rounded(69,148+i*31,495,28,14,new Color32(221,231,188));Text((selected==i?"> ":"  ")+options[i],75,149+i*31,ink);}return; }
        if(screen=="world")return;
        if((screen=="menu"&&menuTab==0)||screen=="sell"||screen=="item"){DrawBackpack();return;}
        Panel(45,75,550,350);string title=screen=="menu"?tabs[menuTab]:screen=="shop"?"Store stock":screen=="store"?"Hearth's store":screen=="post"?"Town hall":screen=="mail"?"Mailbox":"Lost and Found";
        Heading(title,68,91,ink);
        int total=screen=="menu"?TabCount():screen=="shop"?state.Town.StockCount:screen=="mail"?Math.Max(1,state.MailCount):screen=="lost"?Math.Max(1,LostCount()):screen=="store"?8:7;
        bool settings=screen=="menu"&&menuTab==5;int page=settings?9:8,step=settings?24:29;float rowScale=settings?1.2f:1.4f;int first=selected/page*page;
        for(int i=first;i<Math.Min(total,first+page);i++){float y=131+(i-first)*step;if(i==selected){Rounded(57,y,520,step-2,(step-2)*.5f,new Color32(221,231,188));Strong("> "+RowLabel(i),68,y,ink,rowScale);}else Text("  "+RowLabel(i),68,y,ink,rowScale);}
        Text("Stick / D-pad choose   L/R tabs   A select   B close",65,392,ink,1);
        if(screen=="menu"&&menuTab==5)Text(saveStatusText.Get(saveStatus),65,357,ink,1);
    }
    private string RowLabel(int i)
    {
        if(screen=="shop"){int id=state.Town.Stock[i];return id==0?"Sold out":Catalog.Items[id].Name+" / "+Catalog.Items[id].Buy;}
        if(screen=="store"){string[] a={"Buy stock","Sell / return parcel","Part-time job","House expansion","Garden expansion","Repay loan","Daily job","Expand backpack"};return a[i];}
        if(screen=="post"||(screen=="menu"&&menuTab==4)){string[] a={"Deposit up to 1000","Withdraw up to 1000","Repay from wallet","Lost and Found","Mailbox","Write a letter","Visit / gate"};return a[i];}
        if(screen=="mail")return i>=state.MailCount?"No mail":state.Mail[i]<0?"Arrives tomorrow":Catalog.Items[state.Mail[i]].Name;
        if(screen=="lost"){int n=LostIndex(i);return n<0?"No lost items":Catalog.Items[state.Town.Ground[n].Item].Name;}
        if(menuTab==0)return (i+1)+". "+Catalog.Items[state.Pockets[i]].Name;
        if(menuTab==1)return i==0?"Tutorial / daily jobs":state.Town.Residents[i-1].Name+" / friendship "+state.Town.Residents[i-1].Friendship;
        if(menuTab==2)return Catalog.Items[i+1].Name+(state.Owned[i+1]==0?" (not owned)":" / "+Catalog.Items[i+1].Buy);
        if(menuTab==3){string[] a={"Roof palette","Exterior palette","Fence style","Mailbox","Relocate (50000)","Expand house","Expand garden"};return a[i];}
        string[] settings={"Save now","Controls","About / build identity","Performance display: "+(FrameProfile.Enabled?"On":"Off"),"Calendar","Chat settings","Model drawing: "+(indexedModels?"GX indexed":"Direct comparison"),"UV island colors: "+((uvDebug&1)!=0?"On":"Off"),"UV island outlines: "+((uvDebug&2)!=0?"On":"Off")};return settings[i];
    }
    private void DrawBackpack()
    {
        var ink=new Color32(49,72,62);int rows=state.Capacity/5;float height=94+rows*44;
        bool actions=screen=="item";int focus=actions?slot:selected;
        Rounded(198,57,82,50,24,new Color32(195,109,84));Rounded(210,66,58,28,14,new Color32(255,245,221));
        Bulged(70,73,350,height+7,32,new Color32(53,105,84));Panel(76,77,338,height);
        Heading(screen=="sell"?"Sell from backpack":"Backpack",96,92,ink);
        int filled=0;for(int i=0;i<state.Capacity;i++)if(state.Pockets[i]!=0)filled++;
        if(screen!="sell"){Rounded(316,94,75,30,15,new Color32(232,183,86));Strong(filled+" / "+state.Capacity,328,98,ink,1.15f);}
        for(int i=0;i<state.Capacity;i++)
        {
            float x=99+i%5*58,y=144+i/5*44;
            if(i==focus)Rounded(x-3,y-3,54,46,16,new Color32(232,183,86));
            Rounded(x,y,48,40,13,i==focus?new Color32(255,251,238):new Color32(231,228,202));
            int id=state.Pockets[i];
            if(id!=0)itemIcons.Draw(ui,new RectF(id%8*64,id/8*64,64,64),new RectF(x+5,y+1,38,38),Color32.White);
            else Rounded(x+21,y+17,6,6,3,new Color32(188,195,166));
        }
        Text("Stick / D-pad choose   A actions   L/R tabs",90,77+height-26,ink,.85f);
        if(state.BackpackTier<2){Text("Next pack: "+(state.Capacity+10)+" slots",436,325,ink,1);Text("Hearth's store",436,349,ink,1);}
        // Follow the actual slot for both the title and action menu. Clamp the
        // panel (including bulges/shadow) above the HUD for all backpack sizes.
        float slotX=99+focus%5*58,slotY=144+focus/5*44;
        float popWidth=actions?186:168,popHeight=actions?272:141;
        float popX=Math.Min(slotX+66,607-popWidth),popY=Math.Max(80,Math.Min(slotY-12,428-popHeight));
        float pointerY=Math.Max(popY+36,Math.Min(slotY+20,popY+popHeight-36));
        Panel(popX,popY,popWidth,popHeight);
        ui.FillTriangle(new Vector2(popX+2,pointerY-12),new Vector2(popX+2,pointerY+12),new Vector2(slotX+52,slotY+20),new Color32(255,245,221));
        int item=state.Pockets[focus];float textX=popX+16;
        Strong(itemTitleText.Get(item==0?"Empty pocket":Catalog.Items[item].Name),textX,popY+12,ink,1.3f);
        if(actions)
        {
            for(int i=0;i<itemActions.Length;i++){
                float y=popY+56+i*18;
                if(i==selected)Rounded(popX+10,y-1,popWidth-20,18,9,new Color32(221,231,188));
                Text((i==selected?"> ":"  ")+itemActions[i],textX,y,ink,1.05f);
            }
            Text("A select   B back",textX,popY+246,ink,1);
        }
        else if(item==0)Text("Room for\nsomething new.",textX,popY+67,ink,1.1f);
        else
        {
            Text(screen=="sell"?"Sell: "+Catalog.Items[item].Sell+" acorns":"One item",textX,popY+69,ink,1.05f);
            Strong(screen=="sell"?"A  Sell item":"A  Item actions",textX,popY+101,ink,1.05f);
        }
    }
    private Color32 MapTileColor(int x,int z)
    { return MapBackground.TileColor(state.Town.At(x,z)); }
    private void DrawMap()
    {
        Panel(65,65,510,365);Heading("Town map",85,79,new Color32(49,72,62));Text(Town.ScreenTiles+" x "+Town.ScreenTiles+" screen tiles",310,83,new Color32(49,72,62),1.2f);
        const float left=160,top=115,scale=MapBackground.Scale;
        if(batchedDrawing)mapBackground.Draw(ui);
        else for(int z=0;z<Town.Height;z+=5)for(int x=0;x<Town.Width;x+=5)
            ui.FillRectangle(new RectF(left+x*scale,top+z*scale,5*scale,5*scale),MapTileColor(x+2,z+2));
        for(int i=0;i<=Town.ScreenTiles;i++)
        {
            float offset=i*Town.ScreenTileSize*scale;
            ui.FillRectangle(new RectF(left+offset,top,1,280),new Color32(235,235,206,140));
            ui.FillRectangle(new RectF(left,top+offset,280,1),new Color32(235,235,206,140));
            if(i<Town.ScreenTiles){Text(Letter((char)('A'+i)),left+offset+3,402,Color32.Black,.75f);Text((i+1).ToString(),445,top+offset+3,Color32.Black,.8f);}
        }
        for(int i=0;i<state.Town.GroundCount;i++){var g=state.Town.Ground[i];if(g.Kind==3)ui.FillRectangle(new RectF(left+g.X*scale,top+g.Z*scale,2,2),new Color32(45,106,60));}
        ui.FillRectangle(new RectF(left+32*scale,top+26*scale,4,4),new Color32(187,107,71));
        ui.FillRectangle(new RectF(left+50*scale,top+26*scale,4,4),new Color32(132,96,155));
        ui.FillRectangle(new RectF(left+36*scale,top+9*scale,4,4),new Color32(81,98,130));
        if(state.HousePlaced)ui.FillRectangle(new RectF(left+state.Town.HouseX*scale,top+state.Town.HouseZ*scale,GameState.GardenWidths[state.GardenTier]*scale,GameState.GardenHeights[state.GardenTier]*scale),new Color32(220,182,124,180));
        for(int i=0;i<6;i++){var v=state.Town.Residents[i];ui.FillRectangle(new RectF(left+v.X*scale,top+v.Z*scale,3,3),new Color32(153,76,103));}
        ui.FillRectangle(new RectF(left+state.X*scale,top+state.Z*scale,4,4),Color32.White);
    }
}
