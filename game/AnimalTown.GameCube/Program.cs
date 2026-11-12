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
        DebugOverlay.WriteLine("AnimalTown "+BuildInfo.Version);
        if(!Storage.MountDisc())throw new IOException("AnimalTown game disc not found");
        RuntimeOptions.Load();
        var config=DevConfigStorage.Load();
        DevConfigStorage.ApplyGlobal(config);
        StartupSplash.Run();
        while(true){
            if(!TitleScreen.Run(config))return 0;
            var session=TownSession.Load();LoadingScreen.Finish();
            if(!ProfilePicker.Run(session)){AnimalTown.GameCube.NativeTexturePages.Collect();continue;}
            if(session.Draft!=null&&!OnboardingFlow.Run(config,session)){
                session.DiscardDraft();AnimalTown.GameCube.NativeTexturePages.Collect();continue;
            }
            RunSession(config,session);
            // RunSession and all its rendering locals have unwound before the
            // draft is restored and the native collector reclaims managed roots.
            session.DiscardDraft();LoadingScreen.Finish();TextureCache.TrimUnused();
            AnimalTown.GameCube.NativeTexturePages.Collect();
        }
    }
    internal static int RunSession(DevConfig config,TownSession session)
    {
        LoadingScreen.Draw();TextureCache.TrimUnused();
        AnimalTown.GameCube.NativeTexturePages.Collect();
        DebugOverlay.WriteLine("ANIMALTOWN TITLE RELEASED native bytes "+GraphicsDevice.Default.NativeBytes);
        DebugOverlay.WriteLine("ANIMALTOWN GAME START");
        DebugOverlay.WriteLine("ANIMALTOWN ARAM INIT BEGIN");
        if(!Aram.Initialize(2*1024*1024))throw new InvalidOperationException("Audio ARAM reservation failed");
        DebugOverlay.WriteLine("ANIMALTOWN ARAM INIT READY");
        LoadingScreen.Pulse();
        DebugOverlay.WriteLine("ANIMALTOWN GAME CONSTRUCT BEGIN");
        using var game=new TownGame(config,session);
        DebugOverlay.WriteLine("ANIMALTOWN GAME CONSTRUCT READY");
        AnimalTown.GameCube.NativeTexturePages.Collect();
        long previous=Clock.Timestamp;double accumulator=0;
        while(true)
        {
            game.AdvanceLoading();
            FrameProfile.Begin();game.AdvanceResidency();long simulation=FrameProfile.Start();
            long now=Clock.Timestamp;accumulator+=(now-previous)/1000000.0;previous=now;if(accumulator>.1334)accumulator=.1334;
            Controller.Update();var pad=Controller.GetState(0);game.Input(pad);
            if(game.ExitToTitle)return OnboardingFlow.ToTitle;
            if(game.NextSetup!=0)return game.NextSetup;
            // Setup and preview scopes have returned before gameplay starts.
            // Their disposed views/snapshots cannot remain stack roots here.
            game.ReleaseIntro();
            while(accumulator>=1.0/30){game.Step(pad);accumulator-=1.0/30;}
            FrameProfile.End(0,simulation);game.Draw();game.AdvanceResidency(afterDraw:true);
            long delay=FrameProfile.Start();RuntimeOptions.Wait();FrameProfile.End(13,delay);game.RecordFrame();
        }
    }
}

internal sealed class TownGame : IDisposable
{
    private GameState state;
    private readonly TownSession session;
    private bool releaseIntro,disposed;
    internal int NextSetup {get;private set;}
    internal bool ExitToTitle {get;private set;}
    private readonly ModelResidency modelResidency=new ModelResidency();
    private bool modelSceneReady=true,terrainExitReady=true,outdoorModelLookahead;
    private int requestedUv=-1,residencyBudgetUs;
    private float residencyX=float.NaN,residencyZ;
    private int residencySpace=-1,residencyFailures;
    private long playerStillSince,spaceChangedAt;
    private readonly TownClock townClock=new TownClock();
    private readonly DevConfig debugConfig;
    private readonly RenderDebugOptions renderDebug=new RenderDebugOptions();
    private readonly VillagerAttentionRenderer attention=new VillagerAttentionRenderer();
    private readonly DebugWeather weather=new DebugWeather();
    private readonly RainRenderer rain=new RainRenderer();
    private readonly ActorFrameVisibility actorVisibility=new ActorFrameVisibility();
    private readonly AnimationFrameClock animationClock=new AnimationFrameClock(7);
    private readonly bool[] restartClips=new bool[7];
    private readonly int[] appliedClips={-2,-2,-2,-2,-2,-2,-2};
    private float desiredPlayerSpeed=1.05f,appliedPlayerSpeed=1.05f;
    private readonly VillagerJourney villagers=new VillagerJourney();
    private readonly InteriorCamera indoorCamera=new InteriorCamera();
    private readonly Camera indoorView=new Camera(CylinderWorld.CameraFieldOfView,4f/3,.1f,64);
    private readonly BuildingRenderer buildings;
    private int room {get=>state.Space;set=>state.Space=value;}
    private readonly bool[] villagersMoving=new bool[6];
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly Renderer renderer=new Renderer(GraphicsDevice.Default,128);
    private readonly Camera camera=new Camera(CylinderWorld.CameraFieldOfView,4f/3,CylinderWorld.Near,CylinderWorld.Far);
    private readonly Draw2D ui;
    private readonly UiFont font,headingFont,strongFont;
    private readonly CachedTexture itemIcons;
    private readonly Transform worldRoot=new Transform(),transform=new Transform(),identity=new Transform();
    private readonly TerrainRenderer terrain;
    private readonly VillagerShadows villagerShadows;
    private readonly int[] visibleTrees=new int[LowPolyTrees.Capacity];
    private readonly float[] treeDistances=new float[LowPolyTrees.Capacity];
    private readonly LowPolyTrees trees;
    private readonly DroppedItemRenderer droppedItems=new DroppedItemRenderer();
    private readonly UiShapes uiShapes=new UiShapes();
    private readonly MapBackground mapBackground=new MapBackground();
    private readonly RunningMinimap runningMinimap=new RunningMinimap();
    private readonly MenuNavigation menuNavigation=new MenuNavigation();
    private readonly BuildingPrompt buildingPrompt=new BuildingPrompt();
    private readonly WrappedText dialogueText=new WrappedText(41),itemTitleText=new WrappedText(14),saveStatusText=new WrappedText(48);
    private string wrappedDialogue="";
    private readonly RenderQueue actorQueue=new RenderQueue(7,7);
    private readonly Transform[] modelPoses=new Transform[16];
    private readonly BoxBatch boxes;
    private int modelUsed,loadingIndex;
    private Task<Model>? loadingModel;
    private bool resourcesReady,loadingFailed,loadingBuildingsStarted;
    private string loadingLabel="Preparing town assets";
    private float cachedPlayerHeight;
    private float profiledX=float.NaN,profiledZ;
    private string profileNativeLabel="",profileShadowLabel="";
    private int profileLabelFrames=30;

    private readonly Material plain=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    private readonly Model[] models=new Model[6];
    private readonly ModelInstance[] actors=new ModelInstance[7];
    private readonly string[] modelNames={"fox","bunny","giraffe","cat","hamster","spaniel"};
    private readonly Mesh cube;
    private readonly byte[] banner,icon;
    private readonly Sound syllable;
    private readonly DialogueVoice dialogueVoice;
    private MemoryCard? card;
    private string screen="world",message="",speaker="AnimalTown",saveStatus="Not saved";
    private bool returnToMenu;
    private int returnMenuTab,returnMenuSelection;
    private int selected,slot,rotation,tick,day,month,menuTab,faceX,faceZ=-1,typeAt,voiceTick;
    private int jobProgress,moveClip=-1;
    private long sessionStart;
    private float facingYaw=3.1415927f;
    private bool loaded,relocate,damagedSave;
    private bool indexedModels=true,batchedDrawing=true;
    private int lighting=3;
    private int uvDebug;
    private bool cliffPreview;
    private int waterInspection;
    private int buildingInspection;
    private const int SettingsTab=5,TestingTab=6,TestingRows=31;
    private static readonly string[] tabs={"Backpack","Jobs","Catalog","Home","Post office","Settings","Debug / testing"};
    private static readonly string[] itemActions={"Drop","Place (R rotates)","Plant","Eat","Wear","Equip","Bury","Open money bag","Read","Quit"};
    internal TownGame(DevConfig config,TownSession session)
    {
        this.session=session;releaseIntro=session.Draft==null;
        ui=new Draw2D(device,new Draw2DOptions{Width=640,Height=480,VertexCapacity=8192,SpriteCapacity=128});
        font=UiFont.LoadSystem();strongFont=font;headingFont=UiFont.Load("gorditas-bold");cube=Cube();
        boxes=new BoxBatch(renderer,cube);for(int i=0;i<modelPoses.Length;i++)modelPoses[i]=new Transform{Parent=worldRoot};
        LoadingScreen.Pulse();
        itemIcons=TextureCache.Load("dvd:/item-icons.dndtex",true);
        LoadingScreen.Pulse();
        transform.Parent=worldRoot;identity.Parent=worldRoot;
        banner=File.ReadAllBytes("dvd:/savebanner.rgb5a3");icon=File.ReadAllBytes("dvd:/saveicon.rgb5a3");
        DebugOverlay.WriteLine("ANIMALTOWN GAME UI READY");
        var pcm=new byte[512];for(int i=0;i<pcm.Length;i++)pcm[i]=(byte)(128+(i%24<12?25:-25)*(pcm.Length-i)/pcm.Length);
        DebugOverlay.WriteLine("ANIMALTOWN GAME AUDIO BEGIN");
        syllable=Sound.FromPcm8(pcm,16000);syllable.Volume=.12f;dialogueVoice=new DialogueVoice(syllable);
        DebugOverlay.WriteLine("ANIMALTOWN GAME AUDIO READY");
        sessionStart=Clock.Timestamp;
        ReadCalendar();
        debugConfig=config;
        state=session.State;loaded=session.Loaded;damagedSave=session.Damaged;saveStatus=session.Status;day=Math.Max(day,state.Town.Day);month=Math.Max(month,state.Town.Month);
        BuildingCatalog.Configure(File.ReadAllBytes("dvd:/buildings.bin"));
        LoadingScreen.Pulse();
        screen=session.Draft!=null?"plot":"world";
        if(session.Draft!=null&&!session.PreviewStarted){
            DebugOverlay.WriteLine("ANIMALTOWN INITIAL PLOT BEGIN");
            FindStartingPlot();session.PreviewStarted=true;
            DebugOverlay.WriteLine("ANIMALTOWN INITIAL PLOT READY");
        }
        facingYaw=state.Facing;
        if(session.Draft==null)state.Rollover(day,month);
        if(room==0&&state.HousePlaced&&!state.CanWalk(state.X,state.Z,0))state.MoveToHouseEntrance();
        DebugOverlay.WriteLine("ANIMALTOWN WORLD RENDERERS BEGIN");
        buildings=new BuildingRenderer(worldRoot);
        trees=new LowPolyTrees(worldRoot);terrain=new TerrainRenderer();villagerShadows=new VillagerShadows();
        DebugOverlay.WriteLine("ANIMALTOWN WORLD RENDERERS READY");
        if(NativeShadows.Init(GroundShadow.Atlas(),VillagerShadowPatch.Texture())!=1)throw new InvalidOperationException("Shadow union native budget");
        if(config.Values[1]==0)ToggleModelPath();
        if(config.Values[4]==0)ToggleBatching();
        UiFont.Batched=config.Values[4]!=0&&config.Values[5]!=0;
        uvDebug=config.Values[2]|config.Values[3]<<1;
        lighting=config.Values[6]|config.Values[7]<<1;terrain.ObjectShadowsEnabled=(lighting&2)!=0;
        float cover=config.Values[8]==0?0:config.Values[8]==1?1:.5f;while(terrain.CloudCover!=cover)terrain.CycleCloudCover();
        cliffPreview=config.Values[9]!=0;waterInspection=config.Values[10];
        weather.RainEnabled=config.Values[16]!=0;weather.RoughOceanWhenRaining=config.Values[17]!=0;
        for(int i=0;i<config.Values[18];i++)renderDebug.CycleSeason();
        renderDebug.ForceAttention=config.Values[19]!=0;
        while(renderDebug.RimPercent!=config.Values[20])renderDebug.AdjustRim(config.Values[20]>renderDebug.RimPercent?1:-1);
        renderDebug.GroundMipmaps=config.Values[22]!=0;for(int i=0;i<config.Values[23];i++)renderDebug.AdjustGroundMipDistance(1);
        terrain.FreezeMotion=config.Values[24]!=0;terrain.RecreateMeshes=config.Values[25]!=0;if(config.Values[26]!=0)terrain.ToggleGroundRing();
        renderDebug.DiscardTerrainCacheWhenIndoors=config.Values[DevConfig.DiscardTerrainCacheWhenIndoorsIndex]!=0;
        NativeRim.Enable(0);
    }
    private void FindStartingPlot()
    {
        var draft=session.Draft!;int best=int.MaxValue,bx=15,bz=35;
        for(int z=1;z<=Town.Height-13;z++)for(int x=1;x<=Town.Width-15;x++){
            int distance=(x-15)*(x-15)+(z-35)*(z-35);
            if(distance>=best||!draft.CanPlaceHome(x,z,out _)||!state.CanWalk(x+3.5f,z+6.5f,0))continue;
            // Favor an unobstructed first view without removing garden trees.
            for(int i=0;i<state.Town.GroundCount;i++){
                var g=state.Town.Ground[i];
                if(IsTree(g)&&Math.Abs(g.X+.5f-(x+3.5f))<3&&g.Z+.5f>z+6&&g.Z+.5f<z+12)distance+=10000;
            }
            if(distance<best){best=distance;bx=x;bz=z;}
        }
        if(best<int.MaxValue){state.X=bx+3.5f;state.Z=bz+6.5f;}
    }
    private void OnboardingInput(ControllerState p)
    {
        // Do not load setup imagery beside the resident town. RunSession
        // disposes this renderer and unwinds before the next view is created.
        if(p.B.WasPressed){NextSetup=OnboardingFlow.ToLook;return;}
        if(p.A.WasPressed&&session.Draft!.PlaceHome((int)state.X-3,(int)state.Z-6)){
            state.Facing=facingYaw;NextSetup=OnboardingFlow.ToReview;
        }
    }
    internal void ReleaseIntro()
    {
        if(!releaseIntro||!resourcesReady||room==0&&!terrain.Ready)return;
        LoadingScreen.Finish();
        TextureCache.TrimUnused();AnimalTown.GameCube.NativeTexturePages.Collect();
        if(session.Draft!=null||OnboardingUi.LiveViews!=0||
           TextureCache.SourceBytesContaining("dvd:/picker-")!=0||
           TextureCache.SourceBytesContaining("dvd:/wordmark.dndtex")!=0)
            throw new InvalidOperationException("Onboarding resources survived gameplay transition");
        releaseIntro=false;
        DebugOverlay.WriteLine("ANIMALTOWN ONBOARDING RELEASED native bytes "+device.NativeBytes);
    }
    public void Dispose()
    {
        if(disposed)return;
        if(device.IsFrameActive)throw new InvalidOperationException("Dispose the town after EndFrame");
        disposed=true;LoadingScreen.Finish();
        dialogueVoice.Stop();syllable.Dispose();
        Model? unadopted=null;
        if(loadingModel!=null){try{unadopted=loadingModel.GetAwaiter().GetResult();}catch(Exception){if(!loadingModel.IsCompleted)throw;}loadingModel=null;}
        modelResidency.Stop();
        for(int i=0;i<actors.Length;i++){actors[i]?.Dispose();actors[i]=null!;}
        if(unadopted!=null&&Array.IndexOf(models,unadopted)<0)unadopted.Dispose();
        for(int i=0;i<models.Length;i++){models[i]?.Dispose();models[i]=null!;}
        attention.Dispose();rain.Dispose();trees.Dispose();droppedItems.Dispose();buildings.Dispose();terrain.Dispose();fences.Dispose();
        uiShapes.Dispose();mapBackground.Dispose();cube.Dispose();ui.Dispose();
        font.Release();headingFont.Release();TextureCache.Release(itemIcons);card?.Dispose();card=null;
        NativeRim.Enable(0);NativeRenderProbe.Uv(0);
        if(NativeShadows.Shutdown()!=1)throw new InvalidOperationException("Shadow shutdown failed");
        TextureCache.TrimUnused();
    }
    internal void AdvanceLoading()
    {
        if(resourcesReady||loadingFailed)return;
        try{
            terrain.WarmCliffs(state.Town,Clock.Timestamp+1000);
            if(loadingIndex<models.Length){
                if(loadingModel==null){
                    loadingLabel="Loading "+modelNames[loadingIndex];
                    DebugOverlay.WriteLine("ANIMALTOWN MODEL LOAD BEGIN "+loadingIndex+" "+modelNames[loadingIndex]);
                    loadingModel=ModelResources.LoadAsync(modelNames[loadingIndex]);return;
                }
                if(!loadingModel.IsCompleted)return;
                var model=loadingModel.GetAwaiter().GetResult();models[loadingIndex]=model;
                modelResidency.Register(loadingIndex,model,Clock.Timestamp);
                actors[loadingIndex+1]=model.CreateInstance(512*1024);attention.Register(loadingIndex,actors[loadingIndex+1]);if(model.TryGetAnimation("Walking",out var clip))animationClock.Step(loadingIndex+1,0,false);
                DebugOverlay.WriteLine("ANIMALTOWN MODEL LOAD READY "+loadingIndex+" "+modelNames[loadingIndex]);
                loadingModel=null;loadingIndex++;return;
            }
            if(!loadingBuildingsStarted){loadingBuildingsStarted=true;DebugOverlay.WriteLine("ANIMALTOWN BUILDINGS LOAD BEGIN");}
            bool complete=buildings.AdvanceLoading();loadingLabel=buildings.LoadingLabel;if(!complete)return;
            DebugOverlay.WriteLine("ANIMALTOWN BUILDINGS LOAD READY");
            buildings.Begin(villagers.Doors);
            for(int actor=0;actor<BuildingWorld.Actors;actor++){
                var location=BuildingWorld.Location(state,actor);if(location.Space.IsTown)continue;int building=BuildingWorld.Building(location.Space.Value);
                if(!buildings.Ready(state.BuildingTypes[building])){BuildingWorld.Entrance(state,building,out float rx,out float rz);BuildingWorld.SetLocation(state,actor,new ActorLocation(0,rx,rz+1,0));saveStatus="A home is unavailable; its occupants are outside.";}
            }
            droppedItems.Load();
            SelectSpecies();
            // UV regions require the fully loaded model set. Enabling them in
            // the constructor would leave an empty cache and hide the actors.
            if(uvDebug!=0&&NativeRenderProbe.Uv(uvDebug)!=uvDebug){uvDebug=0;saveStatus="UV preview unavailable: not enough memory.";}
            resourcesReady=true;
            DebugOverlay.WriteLine("BUILDINGS READY native bytes "+buildings.ResidentBytes);
            DebugOverlay.WriteLine("ANIMALTOWN READY "+BuildInfo.Version+" native bytes "+device.NativeBytes);
        }catch(Exception){loadingFailed=true;loadingLabel="Could not load town assets. Restart with a valid disc.";DebugOverlay.WriteLine("ANIMALTOWN ASSET LOAD FAILED");}
    }
    private void DrawLoading(bool landscape=false)
    {
        if(loadingFailed)LoadingScreen.Draw("Could not load town assets.\n"+(session.Draft!=null?"B: return to the title.":"Restart with a valid disc."),font);
        else LoadingScreen.Draw();
    }
    // Two opportunities per presented frame overlap DMA with drawing. The total
    // CPU credit remains one millisecond and is charged to preparation telemetry.
    internal void AdvanceResidency(bool afterDraw=false)
    {
        if(!resourcesReady)return;
        long at=Clock.Timestamp;
        if(!afterDraw){residencyBudgetUs=1000;RefreshResidencyDemand(at);}
        var crossing=villagers.Doors.Actors[0];
        bool exiting=villagers.Doors.Busy(0)&&crossing.Destination.Space.IsTown;
        terrain.DiscardTerrainCacheWhenIndoors=renderDebug.DiscardTerrainCacheWhenIndoors;
        if(room==0||exiting||outdoorModelLookahead||!renderDebug.DiscardTerrainCacheWhenIndoors){
            float x=state.X,z=state.Z;
            if(room!=0){if(exiting){x=crossing.Destination.X;z=crossing.Destination.Z;}else BuildingWorld.Entrance(state,BuildingWorld.Building(room),out x,out z);}
            int allowance=Math.Min(500,Math.Max(0,residencyBudgetUs-(int)(Clock.Timestamp-at)));
            terrainExitReady=terrain.PrepareOutdoorResidency(x,z,Clock.Timestamp+allowance,room!=0);
        }else if(!villagers.Doors.Busy(0)&&at-spaceChangedAt>=2000000){
            terrainExitReady=terrain.SuspendOutdoorCache()&&!terrain.CacheSuspended;
        }
        residencyBudgetUs=Math.Max(0,residencyBudgetUs-(int)(Clock.Timestamp-at));
        long modelAt=Clock.Timestamp;
        bool allowDemotion=session.Draft==null&&terrain.ArchiveReady&&room!=0&&at-playerStillSince>=ModelResidency.IdleDelayUs&&
            !villagers.Doors.Busy(0)&&requestedUv<0&&uvDebug==0&&waterInspection==0;
        modelResidency.Pump(at,allowDemotion,modelAt+Math.Max(0,residencyBudgetUs));
        residencyBudgetUs=Math.Max(0,residencyBudgetUs-(int)(Clock.Timestamp-modelAt));
        if(modelResidency.Failures!=residencyFailures){residencyFailures=modelResidency.Failures;TextureCache.TrimUnused();}
        if(requestedUv>=0&&modelResidency.AllReady){int next=requestedUv;requestedUv=-1;ApplyUv(next);}
        modelSceneReady=modelResidency.ReadyForSpace(state,villagers.Doors,room);
        UpdatePortalReadiness();
        FrameProfile.Add(2,Clock.Timestamp-at);
    }
    private void RefreshResidencyDemand(long now)
    {
        if(state.X!=residencyX||state.Z!=residencyZ||room!=residencySpace){
            playerStillSince=now;residencyX=state.X;residencyZ=state.Z;
            if(room!=residencySpace){spaceChangedAt=now;residencySpace=room;}
        }
        // Moving inside starts outside prefetch before the player reaches a door.
        bool nearExit=false;
        if(room!=0){BuildingWorld.InsideDoor(state,room,out float x,out float z);float dx=state.X-x,dz=state.Z-z;nearExit=dx*dx+dz*dz<1.1025f;}
        outdoorModelLookahead=room!=0&&(nearExit||now-playerStillSince<2000000);
        modelResidency.Require(state,villagers.Doors,now,uvDebug!=0||requestedUv>=0||waterInspection!=0||session.Draft!=null,outdoorModelLookahead?0:-1);
        modelSceneReady=modelResidency.ReadyForSpace(state,villagers.Doors,room);
        UpdatePortalReadiness();
    }
    private void UpdatePortalReadiness()
    {
        var doors=villagers.Doors;
        for(int actor=0;actor<BuildingWorld.Actors;actor++){
            if(!doors.Busy(actor)){doors.ResourcesReady[actor]=true;continue;}
            var crossing=doors.Actors[actor];
            if(actor==0)doors.ResourcesReady[actor]=modelResidency.ReadyForSpace(state,doors,crossing.Destination.Space.Value)&&
                modelResidency.ReadyForSpace(state,doors,room)&&(!crossing.Destination.Space.IsTown||terrainExitReady);
            else doors.ResourcesReady[actor]=!modelResidency.Required(actor-1)||modelResidency.Ready(actor-1);
        }
    }
    private void ApplyUv(int next)
    {
        if(NativeRenderProbe.Uv(next)==next)uvDebug=next;
        else Say("UV display","Not enough memory to prepare UV regions. The current view is unchanged.");
    }
    private void SelectSpecies()
    { if(actors[0]!=null)actors[0].Dispose();actors[0]=models[state.Species].CreateInstance(512*1024);moveClip=-1;animationClock.Reset(0);appliedClips[0]=-2; }
    // Gameplay saves require this explicit menu action. The profile picker's
    // separately confirmed Delete action also commits, via TownSession.
    private void SaveFromMenu()
    {
        if(screen!="menu"||menuTab!=SettingsTab||selected!=0)return;
        if(damagedSave){saveStatus="Damaged saves preserved. Use another card.";return;}
        string previousScreen=screen;screen="saving";Draw();screen=previousScreen;
        uiShapes.Trim();
        bool terrainSuspended=false;string phase="mount";
        try
        {
            phase="terrain cache";terrainSuspended=terrain.SuspendForSave();phase="mount";
            if(card==null)card=MemoryCard.Mount(StorageDevice.MemoryCardSlotA,new MemoryCardIdentity("AMTE","01"));
            phase="encode";
            state.Facing=facingYaw;
            phase="save set";
            var repository=new SaveRepository(new CardSaveStorage(card,banner,icon));repository.Save(state);
            saveStatus=repository.LastWrittenRecords==0?"No changes to save.":"Saved / "+BuildInfo.Version;
            DebugOverlay.WriteLine("SAVE: "+repository.LastWrittenRecords+" records / "+repository.LastWrittenBytes+" bytes");
        }
        catch(Exception error){
            // Built-in native exceptions may have no managed message. The
            // recovery screen must remain usable even after an allocation fails.
            string detail=error.Message??"Native save error (no message)";if(detail.Length>64)detail=detail.Substring(0,61)+"...";
            saveStatus="Save failed: "+detail;
            DebugOverlay.WriteLine("SAVE ERROR: "+phase+" (code "+NativeCalendar.CardError()+"): "+detail);
        }
        finally{
            try{card?.Dispose();}catch(Exception error){DebugOverlay.WriteLine("CARD CLOSE: "+(error.Message??"Native close error"));}
            card=null;if(terrainSuspended)terrain.ResumeAfterSave();
        }
    }
    private void Say(string name,string text){dialogueVoice.Begin();voiceTick=0;returnToMenu=screen=="menu"&&(menuTab==SettingsTab||menuTab==TestingTab);if(returnToMenu){returnMenuTab=menuTab;returnMenuSelection=selected;}speaker=name;message=text;wrappedDialogue=dialogueText.Get(text);typeAt=0;screen="dialogue";}
    private bool Press(ControllerState p)=>p.A.WasPressed;
    internal void Input(ControllerState p)
    {
        if(!resourcesReady||!modelSceneReady||room==0&&!terrain.Ready){if(loadingFailed&&session.Draft!=null&&p.B.WasPressed)ExitToTitle=true;return;}
        if(session.Draft!=null){OnboardingInput(p);return;}
        bool chord=p.L.IsHeld&&p.R.IsHeld;
        if(chord&&p.Z.IsHeld&&p.DPadDown.WasPressed){terrain.FreezeMotion=!terrain.FreezeMotion;return;}
        if(chord&&p.Z.IsHeld&&p.X.WasPressed){terrain.RecreateMeshes=!terrain.RecreateMeshes;return;}
        if(chord&&p.Z.IsHeld&&p.Y.WasPressed){lighting=(lighting+1)&3;terrain.ObjectShadowsEnabled=(lighting&2)!=0;return;}
        if(chord&&p.A.WasPressed&&screen=="world"){InspectBuilding();return;}
        if(chord&&p.CStick.Y>.8f&&p.Z.WasPressed&&room==0){terrain.ToggleGroundRing();return;}
        if(chord&&p.B.WasPressed){RuntimeOptions.Cycle();return;}
        if(chord&&p.DPadRight.WasPressed&&room==0){terrain.CycleCloudCover();return;}
        if(chord&&p.DPadUp.WasPressed&&room==0){cliffPreview=!cliffPreview;return;}
        if(chord&&p.DPadLeft.WasPressed&&room==0&&screen=="world"){waterInspection=(waterInspection+1)%6;return;}
        if(waterInspection!=0&&screen=="world"&&!p.Start.WasPressed){if(p.B.WasPressed)waterInspection=0;return;}
        if(chord&&p.Start.WasPressed){InspectTerrain();return;}
        bool navigating=screen=="menu"||screen=="item"||screen=="sell"||screen=="shop"||screen=="store"||screen=="post"||screen=="mail"||screen=="lost";
        int direction=menuNavigation.Read(screen,screen=="menu"?menuTab:-1,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,navigating&&!chord);
        bool up=direction==MenuNavigation.Up,down=direction==MenuNavigation.Down,left=direction==MenuNavigation.Left,right=direction==MenuNavigation.Right;
        if(screen=="menu"&&menuTab==TestingTab&&selected==20&&!chord){
            if(left||right){renderDebug.AdjustRim(right?1:-1);return;}
            if(p.Z.WasPressed){renderDebug.ResetRim();return;}
        }
        if(screen=="menu"&&menuTab==TestingTab&&selected==23&&!chord){
            if(left||right){renderDebug.AdjustGroundMipDistance(right?1:-1);return;}
            if(p.Z.WasPressed){renderDebug.ResetGroundMipDistance();return;}
        }
        if(screen=="menu"&&menuTab==TestingTab&&selected==24&&!chord){
            if(left||right){renderDebug.CycleWaterStyle();return;}
            if(p.Z.WasPressed){renderDebug.ResetWaterStyle();return;}
        }
        if(screen=="menu"&&menuTab==TestingTab&&selected==29&&!chord){
            if(left||right){UiNoiseSettings.Adjust(right?1:-1);return;}
            if(p.Z.WasPressed){UiNoiseSettings.Reset();return;}
        }
        if(screen=="menu"&&menuTab==TestingTab&&(selected==25||selected==26)&&!chord){
            int option=selected==25?DevConfig.TreeNoiseStrength:DevConfig.TreeNoiseScale;
            if(left||right){debugConfig.Adjust(option,right?1:-1);return;}
            if(p.Z.WasPressed){debugConfig.Reset(option);return;}
        }
        if(p.L.IsHeld&&p.R.IsHeld&&p.Z.WasPressed){FrameProfile.Enabled=!FrameProfile.Enabled;return;}
        if(p.L.IsHeld&&p.R.IsHeld&&p.Y.WasPressed){ToggleModelPath();return;}
        if(p.L.IsHeld&&p.R.IsHeld&&p.DPadDown.WasPressed){if(batchedDrawing)UiFont.Batched=!UiFont.Batched;return;}
        if(p.L.IsHeld&&p.R.IsHeld&&p.X.WasPressed){ToggleBatching();return;}
        if(p.L.IsHeld&&p.R.IsHeld)return;
        if(screen=="dialogue")
        { if(Press(p)||p.B.WasPressed){dialogueVoice.Stop();if(typeAt<message.Length)typeAt=message.Length;else{screen=returnToMenu?"menu":"world";if(returnToMenu){menuTab=returnMenuTab;selected=returnMenuSelection;}}}return; }
        if(screen=="plot")
        {
            if(Press(p)){if(state.PlaceHouse((int)state.X-3,(int)state.Z-3,relocate))Say("Hearth","Your home is ready! Visit the store for your first job.");else Say("Hearth","This plot needs 15 x 13 clear land tiles.");}
            if(p.B.WasPressed&&state.HousePlaced)screen="world";return;
        }
        if(screen=="world"&&villagers.Doors.Busy(0))return;
        if(p.Start.WasPressed){screen="menu";menuTab=SettingsTab;selected=0;return;}
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
                if(room!=0&&(selected==0||selected==2||selected==6)){Say("Home","Drop, plant and bury outdoors. Choose Place for indoor furniture.");return;}
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
            if(screen=="menu"&&p.R.WasPressed){menuTab=1;selected=0;return;}if(screen=="menu"&&p.L.WasPressed){menuTab=tabs.Length-1;selected=0;return;}
            if(Press(p)&&state.Pockets[selected]!=0){if(screen=="sell")Say("Hearth",state.Sell(selected)?"Thank you! Acorns added to your wallet.":"I cannot buy that, or your wallet is full.");else{slot=selected;screen="item";selected=0;}}return;
        }
        int count=screen=="shop"?state.Town.StockCount:screen=="post"?7:screen=="store"?8:screen=="mail"?Math.Max(1,state.MailCount):screen=="lost"?Math.Max(1,LostCount()):TabCount();
        if(down)selected=(selected+1)%count;if(up)selected=(selected+count-1)%count;
        if(screen=="menu"&&(p.R.WasPressed||right)){menuTab=(menuTab+1)%tabs.Length;selected=0;}
        if(screen=="menu"&&(p.L.WasPressed||left)){menuTab=(menuTab+tabs.Length-1)%tabs.Length;selected=0;}
        if(!Press(p))return;
        if(screen=="sell"){Say("Hearth",state.Sell(selected)?"Thank you! Acorns added to your wallet.":"I cannot buy that, or your wallet is full.");return;}
        if(screen=="shop"){Say("Hearth",state.Buy(selected)?"Thank you for shopping!":"You need enough acorns and a free pocket.");return;}
        if(screen=="store"){StoreAction();return;}
        if(screen=="post"){PostAction();return;}
        if(screen=="mail")
        { if(selected<state.MailCount&&state.Mail[selected]>0&&state.Receive(state.Mail[selected])){for(int i=selected;i<state.MailCount-1;i++)state.Mail[i]=state.Mail[i+1];state.MailCount--;Say("Mailbox","Delivery collected.");}return; }
        if(screen=="lost")
        { int index=LostIndex(selected);Say("Town hall",state.ClaimLost(index,state.ActiveProfile)?"Recovered!":"Your pockets are full, or the item is reserved.");return; }
        if(screen=="menu")MenuAction();
    }
    private int TabCount()=>menuTab==0?state.Capacity:menuTab==1?7:menuTab==2?Catalog.Items.Length-1:menuTab==3?7:menuTab==4?7:menuTab==TestingTab?TestingRows:6;
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
            if(selected==4){if(room!=0){Say("Home","Step outside before relocating your home.");return;}relocate=true;screen="plot";}
            if(selected==5){Say("Home",state.Expand(false,day)?"Construction ordered.":"Pay off your loan first.");}
            if(selected==6){Say("Home",state.Expand(true,day)?"Garden construction ordered.":"Loan or house tier prevents this expansion.");}return;
        }
        if(menuTab==4){PostAction();return;}
        if(menuTab==TestingTab){TestingAction();return;}
        if(menuTab!=SettingsTab)return;
        if(selected==5){menuTab=TestingTab;selected=0;return;}
        if(selected==0){SaveFromMenu();}else if(selected==1){Say("Controls","Stick / D-pad: walk / menus. L/R: tabs. B: run / back. A: interact / select. X: backpack. Y: map. Z: tool. START: settings. Choose Save now to save.");}
        else if(selected==2)Say("About","AnimalTown / "+BuildInfo.Version+" / hypercube-engine. Original homebrew for Nintendo GameCube.");
        else if(selected==3){Say("Calendar","Console RTC / day "+day+". New days begin at 06:00.");}
        else Say("Chat","Quick phrases and online chat are pending the companion transport.");
    }
    private void TestingAction()
    {
        switch(selected){
            case 0:FrameProfile.Enabled=!FrameProfile.Enabled;break;
            case 1:ToggleModelPath();break;
            case 2:case 3:
                int next=uvDebug^(selected==2?1:2);
                if(modelResidency.AllReady)ApplyUv(next);else requestedUv=next;
                break;
            case 4:ToggleBatching();break;
            case 5:if(batchedDrawing)UiFont.Batched=!UiFont.Batched;break;
            case 6:lighting^=1;break;
            case 7:lighting^=2;terrain.ObjectShadowsEnabled=(lighting&2)!=0;break;
            case 8:terrain.CycleCloudCover();break;
            case 9:if(room==0)cliffPreview=!cliffPreview;break;
            case 10:if(room==0)waterInspection=(waterInspection+1)%6;break;
            case 11:RuntimeOptions.Toggle(1);break;
            case 12:RuntimeOptions.Toggle(2);break;
            case 13:RuntimeOptions.Toggle(4);break;
            case 14:if(room==0){waterInspection=0;InspectTerrain();}break;
            case 15:waterInspection=0;InspectBuilding();screen="world";break;
            case 16:weather.RainEnabled=!weather.RainEnabled;break;
            case 17:weather.RoughOceanWhenRaining=!weather.RoughOceanWhenRaining;break;
            case 18:renderDebug.CycleSeason();break;
            case 19:renderDebug.ForceAttention=!renderDebug.ForceAttention;break;
            case 20:renderDebug.AdjustRim(1);break;
            case 21:DebugOverlay.PerformanceVisible=!DebugOverlay.PerformanceVisible;break;
            case 22:renderDebug.GroundMipmaps=!renderDebug.GroundMipmaps;break;
            case 23:renderDebug.AdjustGroundMipDistance(1);break;
            case 24:renderDebug.CycleWaterStyle();break;
            case 29:UiNoiseSettings.Adjust(1);break;
            case 25:debugConfig.Adjust(DevConfig.TreeNoiseStrength,1);break;
            case 26:debugConfig.Adjust(DevConfig.TreeNoiseScale,1);break;
            case 27:uiShapes.Trim();card?.Dispose();card=null;DevConfigStorage.Save(debugConfig);Say("Tree noise",DevConfigStorage.Status);break;
            case 28:
                uiShapes.Trim();card?.Dispose();card=null;
                var saved=DevConfigStorage.Load();
                debugConfig.Values[DevConfig.TreeNoiseStrength]=saved.Values[DevConfig.TreeNoiseStrength];
                debugConfig.Values[DevConfig.TreeNoiseScale]=saved.Values[DevConfig.TreeNoiseScale];
                Say("Tree noise",DevConfigStorage.Status);break;
            case 30:
                renderDebug.DiscardTerrainCacheWhenIndoors=!renderDebug.DiscardTerrainCacheWhenIndoors;
                debugConfig.Values[DevConfig.DiscardTerrainCacheWhenIndoorsIndex]=renderDebug.DiscardTerrainCacheWhenIndoors?1:0;
                uiShapes.Trim();card?.Dispose();card=null;
                if(!DevConfigStorage.Save(debugConfig)){saveStatus=DevConfigStorage.Status;Say("Dev config",saveStatus);}
                break;
        }
    }
    private void ToggleBatching()
    {
        int next=batchedDrawing?0:1;
        if(NativeRenderProbe.Batch(next)!=next)throw new InvalidOperationException("Batch switch failed");
        batchedDrawing=next!=0;UiFont.Batched=batchedDrawing;
    }
    private static string OnOff(bool value)=>value?"On":"Off";
    private string TestingLabel(int row)
    {
        switch(row){
            case 0:return "Performance display: "+OnOff(FrameProfile.Enabled);
            case 1:return "Model drawing: "+(indexedModels?"GX indexed":"Direct comparison");
            case 2:return "UV island colors: "+OnOff((uvDebug&1)!=0);
            case 3:return "UV island outlines: "+OnOff((uvDebug&2)!=0);
            case 4:return "Batched drawing: "+OnOff(batchedDrawing);
            case 5:return "Batched text: "+(batchedDrawing?OnOff(UiFont.Batched):"Requires batched drawing");
            case 6:return "Actor rim lighting: "+OnOff((lighting&1)!=0);
            case 7:return "Object / actor shadows: "+OnOff((lighting&2)!=0);
            case 8:return "Cloud cover: "+(terrain.CloudCover==0?"Clear":terrain.CloudCover==1?"Overcast":"Partial");
            case 9:return "Cliff preview: "+(room==0?OnOff(cliffPreview):"Outdoors only");
            case 10:return "Water inspection: "+(room!=0?"Outdoors only":waterInspection==0?"Off":waterInspection+" / 5");
            case 11:return "Frame pacing: "+((RuntimeOptions.Mode&1)!=0?"Uncapped":"Yield");
            case 12:return "Productive waits: "+OnOff((RuntimeOptions.Mode&2)!=0);
            case 13:return "Reuse frame clear: "+OnOff((RuntimeOptions.Mode&4)!=0);
            case 14:return room==0?"Go to test ramp":"Test ramp: outdoors only";
            case 15:return room==0?"Go to next test building":"Go to indoor doorway";
            case 16:return "Rain preview: "+OnOff(weather.RainEnabled);
            case 17:return "Rough ocean in rain: "+OnOff(weather.RoughOceanWhenRaining);
            case 18:return "Season preview: "+(renderDebug.SeasonOverride<0?"Auto":TerrainTiles.SeasonName(renderDebug.SeasonOverride));
            case 19:return "Villager attention bubbles: "+OnOff(renderDebug.ForceAttention);
            case 20:return "Rim intensity: "+renderDebug.RimPercent+"%";
            case 21:return "SDK performance overlay: "+OnOff(DebugOverlay.PerformanceVisible);
            case 22:return "Ground mipmaps: "+OnOff(renderDebug.GroundMipmaps);
            case 23:return "Ground mip distance: "+(renderDebug.GroundMipDistance==0?"Auto":renderDebug.GroundMipDistance+(renderDebug.GroundMipDistance==1?" square":" squares"));
            case 24:return "Water rendering: "+(renderDebug.WaterStyle==WaterRenderStyle.WindWaker?"Wind Waker":"Sunshine");
            case 25:return "Tree noise strength: "+debugConfig.TreeNoiseStrengthPercent+"%";
            case 26:return "Tree noise scale: "+debugConfig.TreeNoiseScalePercent+"%";
            case 27:return "Save tree noise settings";
            case 28:return "Restore saved tree noise";
            case 29:return "UI bubble noise: "+UiNoiseSettings.Percent+"% (session)";
            default:return "discard terrain cache when indoors: "+OnOff(renderDebug.DiscardTerrainCacheWhenIndoors);
        }
    }
    private void Interact()
    {
        int x=(int)state.X,z=(int)state.Z,tx=x+faceX,tz=z+faceZ;
        if(villagers.Doors.Busy(0))return;
        int door=villagers.Doors.Nearby(state,0,facingYaw);
        if(door>=0){if(!buildings.Ready(state.BuildingTypes[door])){Say("Home","This home is unavailable right now.");return;}if(!villagers.Doors.Request(state,0,door))Say("Door","Please wait for the doorway to clear.");return;}
        if(room!=0){
            for(int i=0;i<6;i++){var v=state.Town.Residents[i];if(v.Space==room&&Math.Abs(v.PosX-state.X)<2&&Math.Abs(v.PosZ-state.Z)<2){Say(v.Name,state.Talk(i));return;}}
            if(room==24){screen="store";selected=0;return;}if(room==25){screen="post";selected=0;return;}
            // No nearby villager or usable doorway: A does nothing.
            return;
        }
        if(x>=32&&x<=40&&z<=12){Say("Gate","The gate awaits a Broadband Adapter and companion connection.");return;}
        for(int i=0;i<6;i++){var v=state.Town.Residents[i];if(v.Space==0&&Math.Abs(v.PosX-state.X)<2.8f&&Math.Abs(v.PosZ-state.Z)<2.8f){Say(v.Name,state.Talk(i));return;}}
        if(state.Pickup(tx,tz)||state.Pickup(x,z)){Say("Backpack","Picked up!");return;}
        int ground=state.Town.FindGround(tx,tz);
        if(ground>=0)
        {
            var g=state.Town.Ground[ground];
            if(g.Kind==3){if(state.Tool==9){g.Kind=0;g.Item=3;Say("Tree","A sapling for replanting.");}else if(state.Receive(1))Say("Tree","A ripe peach drops into your pockets!");return;}
            if(g.Kind==1&&state.Tool==7&&state.Receive(g.Item)){state.Town.RemoveGround(ground);Say("Digging","You found something!");return;}
            if(g.Kind==2&&state.Tool==8){jobProgress++;Say("Flowers","Watered with care.");return;}
        }
        if(state.Tool==5&&(WaterSurface.IsWater(state.Town,tx,tz)||WaterSurface.IsWater(state.Town,tx+1,tz)))
        { Say("Fishing",state.Receive(14)?"A river fish!":"Your pockets are full.");return; }
        if(state.Tool==6){
            // Keep successful bug catches; misses are silent like all other
            // empty-space interactions.
            if(tick%3==0&&state.Receive(15))Say("Bug net","Caught a butterfly!");
            return;
        }
        if(state.Tool==7&&state.Town.AddGround(16,tx,tz,1,day)){Say("Digging","A spot for something buried.");return;}
        // Nothing in reach. Do not interrupt movement with an idle dialogue.
    }
    internal void Step(ControllerState pad)
    {
        if(resourcesReady)RefreshResidencyDemand(Clock.Timestamp);
        if(!resourcesReady||!modelSceneReady||room==0&&!terrain.Ready){runningMinimap.Update(Clock.Timestamp,false,false);return;}
        bool running=false;float beforeX=state.X,beforeZ=state.Z;
        tick++;
        if(tick%30==0)townClock.Update(NativeCalendar.ReadClock());
        if(tick%900==0&&session.Draft==null){ReadCalendar();state.Rollover(day,month);}
        if(screen=="dialogue"&&typeAt<message.Length){
            typeAt++;
            if(typeAt==message.Length)dialogueVoice.Stop();
            else if(++voiceTick%3==0&&dialogueVoice.Available&&!dialogueVoice.Pulse())DebugOverlay.WriteLine("Dialogue audio unavailable; continuing text");
        }
        if(screen=="world"||screen=="plot")
        {
            if(villagers.Doors.Busy(0))facingYaw=state.Facing;
            float inputX=pad.Stick.X,inputY=pad.Stick.Y;
            if(room!=0&&inputX*inputX+inputY*inputY<PlayerMovement.DeadZone*PlayerMovement.DeadZone){int held=(int)pad.HeldButtons;inputX=((held&2)!=0?1:0)-((held&1)!=0?1:0);inputY=((held&8)!=0?1:0)-((held&4)!=0?1:0);}
            if(room!=0){indoorCamera.Configure(BuildingWorld.Definition(state,BuildingWorld.Building(room)));indoorCamera.Tick(pad.L.IsHeld&&pad.R.IsHeld?0:pad.CStick.X,pad.L.WasPressed&&!pad.R.IsHeld);indoorCamera.Movement(inputX,inputY,out inputX,out inputY);}
            if(!villagers.Doors.Busy(0)&&!(pad.L.IsHeld&&pad.R.IsHeld)&&PlayerMovement.Move(state,room,inputX,inputY,room==0?(int)pad.HeldButtons:0,pad.B.IsHeld,out float dx,out float dz))
            {
                facingYaw=PlayerMovement.Yaw(dx,dz);PlayerMovement.Target(dx,dz,out faceX,out faceZ);
                int clip=pad.B.IsHeld?1:0;moveClip=clip;running=clip==1&&(state.X!=beforeX||state.Z!=beforeZ);desiredPlayerSpeed=1.05f;
            }
            else if(villagers.Doors.Moving(0)){
                moveClip=0;desiredPlayerSpeed=1;
            }
            else if(moveClip>=0)moveClip=-1;
        }
        if(screen=="world"){
            RefreshResidencyDemand(Clock.Timestamp);
            if(villagers.Doors.Busy(0)&&!villagers.Doors.ResourcesReady[0])modelResidency.WaitForPortal();
            int oldRoom=room;villagers.Tick(state);
            if(room!=oldRoom){facingYaw=state.Facing;faceX=0;faceZ=room==0?1:-1;indoorCamera.Reset();}
        }
        runningMinimap.Update(Clock.Timestamp,running,screen=="world"&&room==0&&waterInspection==0&&!cliffPreview);
        state.Facing=facingYaw;cachedPlayerHeight=PlayerHeight();if(room==0)RenderView.Prepare(state.X,state.Z,cachedPlayerHeight);
        animationClock.Step(0,moveClip,models[state.Species].Animations.Count>0);
        for(int i=1;i<7;i++){
            var v=state.Town.Residents[i-1];bool moving=v.Space==room&&screen=="world"&&villagers.Moving[i-1];
            if(moving!=villagersMoving[i-1]){
                villagersMoving[i-1]=moving;animationClock.Reset(i);animationClock.Step(i,moving?0:-1,false);restartClips[i]=moving;
            }
            animationClock.Step(i,animationClock.Clip(i),moving);
        }
    }
    private void UpdateAnimationFrame()
    {
        long start=FrameProfile.Start();
        for(int i=0;i<7;i++){
            bool visible=actorVisibility.Visible[i];float seconds=animationClock.Consume(i,visible);
            // Clip controls can reskin too. Defer every native animation call,
            // including Stop/Play/speed changes, until this actor can be drawn.
            if(!visible)continue;
            int clip=animationClock.Clip(i);
            if(restartClips[i]||appliedClips[i]!=clip){
                restartClips[i]=false;
                string name=clip==1?"Running":"Walking";int model=i==0?state.Species:i-1;
                if(clip<0)actors[i].Animator.Stop();
                else if(models[model].TryGetAnimation(name,out var _)){actors[i].Animator.Play(name,true);actors[i].Animator.SetSpeed(i==0?desiredPlayerSpeed:1);if(i==0)appliedPlayerSpeed=desiredPlayerSpeed;}
                appliedClips[i]=clip;
            }
            if(i==0&&clip>=0&&appliedPlayerSpeed!=desiredPlayerSpeed){actors[0].Animator.SetSpeed(desiredPlayerSpeed);appliedPlayerSpeed=desiredPlayerSpeed;}
            if(seconds>0){actors[i].Update(seconds);FrameProfile.AnimationUpdates++;}
        }
        FrameProfile.End(1,start);
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
        if(!(room==0?RenderView.Sphere(center.X,center.Y,center.Z,radius,state.X,state.Z,cachedPlayerHeight):indoorCamera.Sphere(center.X,center.Y,center.Z,radius,BuildingWorld.Size(state,room)))||(room==0&&!RenderView.AboveHorizon(center.Y,center.Z,radius,state.Z,cachedPlayerHeight))){FrameProfile.CulledObjects++;return;}
        if(batchedDrawing)boxes.Add(origin,new Vector3(w,h,d),SurfaceRotation(z),color);
        else{transform.LocalPosition=origin;transform.LocalScale=new Vector3(w,h,d);transform.LocalRotation=SurfaceRotation(z);plain.Color=color;renderer.DrawMesh(cube,plain,transform);}
    }
    private bool Visible(float x,float z,float scale)
    {var center=SurfacePoint(x,z,scale*.5f);float radius=scale*1.5f;return room!=0?indoorCamera.Sphere(x,scale*.5f,z,radius,BuildingWorld.Size(state,room)):RenderView.Sphere(center.X,center.Y,center.Z,radius,state.X,state.Z,cachedPlayerHeight)&&RenderView.AboveHorizon(center.Y,center.Z,radius,state.Z,cachedPlayerHeight);}
    private void ModelAt(int actor)
    {
        if(!actorVisibility.Visible[actor]){FrameProfile.CulledObjects++;return;}
        var location=actorVisibility.Locations[actor];float x=location.X,z=location.Z,scale=1.8f,facing=actor==0?facingYaw:location.Yaw;
        var pose=batchedDrawing?modelPoses[modelUsed++]:transform;pose.LocalPosition=SurfacePoint(x,z);pose.LocalScale=new Vector3(scale,scale,scale);pose.LocalRotation=SurfaceRotation(z,facing);
        if(batchedDrawing){actorQueue.Submit(actors[actor],pose);}else{NativeRim.Enable((lighting&1)!=0?renderDebug.RimPercent:0);try{renderer.DrawModel(actors[actor],pose);}finally{NativeRim.Enable(0);}}
    }
    private void DrawActors()
    {NativeRim.Enable((lighting&1)!=0?renderDebug.RimPercent:0);try{actorQueue.Execute(renderer);}finally{NativeRim.Enable(0);}}
    private void PrepareVillagerShadows()
    {
        villagerShadows.Enabled=(lighting&2)!=0;villagerShadows.BeginFrame(state.Town);
        NativeShadows.Begin(room,villagerShadows.Enabled?1:0,room==0?0:BuildingWorld.Size(state,room));if(!villagerShadows.Enabled)return;
        int size=room==0?0:BuildingWorld.Size(state,room);
        bool mat=room!=0&&BuildingWorld.Definition(state,BuildingWorld.Building(room)).Floor==BuildingCatalog.Cottage.Floor;
        for(int actor=0;actor<BuildingWorld.Actors;actor++){
            if(BuildingWorld.Location(state,actor).Space.Value!=room||actor==0&&waterInspection!=0)continue;
            var p=villagers.Doors.VisualLocation(state,actor);if(Visible(p.X,p.Z,1.8f))villagerShadows.Set(actor,room,p.X,p.Z,size,mat);
        }
    }
    internal void Draw()
    {
        if(!resourcesReady){DrawLoading();return;}
        float x=state.X,z=state.Z;
        try{if(waterInspection!=0)WaterInspection.Focus(state,waterInspection);DrawScene();}
        finally{state.X=x;state.Z=z;}
    }
    private readonly FenceRenderer fences=new FenceRenderer();
    private void DrawScene()
    {
        // Input/debug camera changes may occur on a frame with no simulation
        // tick. Resolve final drawable residency after the effective view changes.
        long residencyAt=FrameProfile.Start();
        modelResidency.Require(state,villagers.Doors,Clock.Timestamp,uvDebug!=0||requestedUv>=0||waterInspection!=0||session.Draft!=null,outdoorModelLookahead?0:-1);
        modelSceneReady=modelResidency.ReadyForSpace(state,villagers.Doors,room);
        FrameProfile.End(2,residencyAt);
        if(!modelSceneReady){modelResidency.WaitForScene();DrawLoading();return;}
        terrain.RoughOcean=weather.RoughOcean(room);terrain.SeasonOverride=renderDebug.SeasonOverride;
        terrain.WaterStyle=renderDebug.WaterStyle;
        terrain.GroundMipmaps=renderDebug.GroundMipmaps;terrain.GroundMipDistance=renderDebug.GroundMipDistance;
        cachedPlayerHeight=PlayerHeight();if(room==0)RenderView.Prepare(state.X,state.Z,cachedPlayerHeight);
        if(room!=0)indoorCamera.Configure(BuildingWorld.Definition(state,BuildingWorld.Building(room)));
        long phase=FrameProfile.Start(),preparationDeadline=Clock.Timestamp+2000;PrepareVillagerShadows();
        long masksAt=FrameProfile.Start();villagerShadows.PrepareMasks(preparationDeadline);FrameProfile.DetailEnd(4,masksAt);
        if(room==0){terrain.Prepare(state,room,month,villagerShadows,preparationDeadline,cliffPreview);terrain.Animate((Clock.Timestamp-sessionStart)/1000000.0);}
        else{int steps=0;while(steps<8&&Clock.Timestamp<preparationDeadline){if(!villagerShadows.Advance())break;steps++;}}
        FrameProfile.End(2,phase);
        if(room==0&&!terrain.Ready){DrawLoading(landscape:true);return;}
        LoadingScreen.Finish();
        // Only a drawable scene may evaluate native poses. Shadow receivers
        // use logical positions and can prepare while the landscape loads.
        if(room==0){trees.Prepare(terrain.RenderSeason,debugConfig.TreeNoiseStrengthPercent,debugConfig.TreeNoiseScalePercent);fences.Prepare(state);droppedItems.Prepare(state,cachedPlayerHeight);}
        if(weather.Raining(room)&&villagers.Doors.PlayerFade<1)rain.Prepare(state,(Clock.Timestamp-sessionStart)/1000000.0,cachedPlayerHeight);
        actorVisibility.Prepare(state,villagers.Doors,indoorCamera,waterInspection,cachedPlayerHeight);
        UpdateAnimationFrame();
        if(renderDebug.ForceAttention&&villagers.Doors.PlayerFade<1)attention.Prepare();
        phase=FrameProfile.Start();uiShapes.Prepare(screen,screen=="menu"?menuTab:-1,batchedDrawing);if((batchedDrawing&&screen=="map")||(runningMinimap.Visible&&screen=="world"&&room==0))mapBackground.Prepare(state.Town);modelUsed=0;FrameProfile.End(8,phase);
        phase=FrameProfile.Start();UiNoise.Prepare();device.BeginFrame(room==0?(weather.Raining(room)?new Color32(112,143,151):new Color32(149,198,186)):new Color32(32,40,39));
        float eyeHeight=PlayerHeight();
        if(room==0)camera.LookAt(new Vector3(state.X,CylinderWorld.CameraHeight+eyeHeight,state.Z+CylinderWorld.CameraBack),new Vector3(state.X,eyeHeight,state.Z-CylinderWorld.CameraAimAhead),new Vector3(0,1,0));
        else{float size=BuildingWorld.Size(state,room);indoorView.LookAt(new Vector3(indoorCamera.EyeX(size),indoorCamera.EyeY(size),indoorCamera.EyeZ(size)),new Vector3(size*.5f,.7f,size*.5f),new Vector3(0,1,0));}
        renderer.Begin3D(room==0?camera:indoorView);buildings.Begin(villagers.Doors);FrameProfile.End(4,phase);
        phase=FrameProfile.Start();int submissionAt=FrameProfile.SubmissionStart();if(room==0){terrain.Draw(renderer,terrain.ScrollTransform,room);}else buildings.Interior(renderer,state,worldRoot,indoorCamera,villagers.Doors);
        FrameProfile.SubmissionEnd(0,submissionAt);FrameProfile.End(4,phase);phase=FrameProfile.Start();
        if(room==0)
        {
            for(int i=1;i<BuildingWorld.Actors;i++)ModelAt(i);
            if(batchedDrawing)DrawActors();
            FrameProfile.End(5,phase);phase=FrameProfile.Start();submissionAt=FrameProfile.SubmissionStart();DrawTrees();FrameProfile.SubmissionEnd(1,submissionAt);FrameProfile.End(6,phase);phase=FrameProfile.Start();
            long groundAt=FrameProfile.Start();
            for(int i=0;i<state.Town.GroundCount;i++)
            {
                var g=state.Town.Ground[i];if(g.Kind==0||g.Kind==4)continue;
                float y=0;
                if(!IsTree(g))Box(g.X+.25f,y+.02f,g.Z+.25f,.5f,g.Kind==1?.03f:.25f,.5f,g.Kind==1?new Color32(105,81,54):g.Kind==2?new Color32(242,174,170):new Color32(229,190,82));
            }
            if(batchedDrawing)boxes.Flush();FrameProfile.End(3,groundAt);
            submissionAt=FrameProfile.SubmissionStart();
            for(int building=0;building<BuildingWorld.Count;building++)buildings.Exterior(renderer,state,building,villagers.Doors);
            FrameProfile.SubmissionEnd(2,submissionAt);submissionAt=FrameProfile.SubmissionStart();
            fences.Draw(renderer,state,terrain.ScrollTransform,cachedPlayerHeight);FrameProfile.SubmissionEnd(3,submissionAt);
            groundAt=FrameProfile.Start();droppedItems.Draw(renderer);FrameProfile.End(3,groundAt);
        }
        if(room!=0){
            if(!indoorCamera.WallVisible(2)){
                float size=BuildingWorld.Size(state,room),scale=size/4;
                Box(size*.5f-.63f*scale,0,size,.08f,2.23f,.08f,new Color32(133,80,48));
                Box(size*.5f+.55f*scale,0,size,.08f,2.23f,.08f,new Color32(133,80,48));
                Box(size*.5f-.63f*scale,2.15f,size,1.26f*scale,.08f,.08f,new Color32(133,80,48));
            }
            for(int i=1;i<BuildingWorld.Actors;i++)ModelAt(i);
            for(int i=0;i<16;i++)if(BuildingWorld.Fixture(state,room,i,out float fx,out float fz,out float fw,out float fd,out float fh)){
                int kind=BuildingWorld.FixtureKind(state,room,i);if(kind==0)continue;
                Box(fx,0,fz,fw,fh,fd,kind==1?new Color32(176,119,81):new Color32(127,85,58));
                if(kind==1){Box(fx+.06f,fh,fz+.06f,fw-.12f,.12f,fd-.12f,new Color32(106,155,144));Box(fx+.1f,fh+.12f,fz+.1f,fw-.2f,.1f,.35f,new Color32(245,231,199));}
            }
        }
        for(int i=0;i<state.Town.PlacedCount;i++){var q=state.Town.Placed[i];if(q.Room==room)Box(q.X,0,q.Z,.7f,.7f,.7f,new Color32(176,119,81));}
        if(batchedDrawing)boxes.Flush();
        FrameProfile.End(7,phase);phase=FrameProfile.Start();
        ModelAt(0);
        if(batchedDrawing)DrawActors();
        FrameProfile.End(5,phase);phase=FrameProfile.Start();
        if(screen=="plot")
        {
            int x=(int)state.X-3,z=(int)state.Z-(session.Draft!=null?6:3);
            bool valid=session.Draft!=null?session.Draft.CanPlaceHome(x,z,out _):state.Town.ValidPlot(x,z);
            var color=valid?new Color32(115,202,114):new Color32(229,116,89);
            for(int i=0;i<15;i++){Box(x+i,.035f,z,.9f,.04f,.9f,color);Box(x+i,.035f,z+12,.9f,.04f,.9f,color);}
            for(int i=1;i<12;i++){Box(x,.035f,z+i,.9f,.04f,.9f,color);Box(x+14,.035f,z+i,.9f,.04f,.9f,color);}
            if(session.Draft!=null)buildings.PreviewHome(renderer,state,x,z);
        }
        if(batchedDrawing)boxes.Flush();
        if(renderDebug.ForceAttention&&villagers.Doors.PlayerFade<1)attention.Draw(renderer,state,actorVisibility,indoorCamera,cachedPlayerHeight);
        submissionAt=FrameProfile.SubmissionStart();if(weather.Raining(room)&&villagers.Doors.PlayerFade<1)rain.Draw(renderer);FrameProfile.SubmissionEnd(4,submissionAt);
        renderer.End3D();ui.Begin();submissionAt=FrameProfile.SubmissionStart();DrawHud();if(FrameProfile.Enabled)ui.Flush();FrameProfile.SubmissionEnd(5,submissionAt);FrameProfile.End(8,phase);FrameProfile.Draw(ui);
        float fade=villagers.Doors.PlayerFade;if(fade>0)ui.FillRectangle(new RectF(0,0,640,480),new Color32(12,19,25,(byte)(fade*255)));
        phase=FrameProfile.Start();submissionAt=FrameProfile.SubmissionStart();ui.End();FrameProfile.SubmissionEnd(6,submissionAt);FrameProfile.End(8,phase);
        phase=FrameProfile.Start();device.EndFrame();FrameProfile.End(9,phase);
    }
    internal void RecordFrame()
    {
        if(!resourcesReady||!FrameProfile.Enabled)return;
        int scene=screen=="map"?10:screen=="menu"&&menuTab==0?(indexedModels?1:7):room!=0?2:screen=="world"?(indexedModels?0:6):3;
        int drawingScene=batchedDrawing?(UiFont.Batched?scene:scene+32):scene+16;
        if(room==0){if(!terrain.Ready)drawingScene|=32768;if(terrain.MissingVisible>0)drawingScene|=16384;if(terrain.UrgentStages>0)drawingScene|=8192;}
        bool moved=!float.IsNaN(profiledX)&&(state.X!=profiledX||state.Z!=profiledZ);profiledX=state.X;profiledZ=state.Z;
        FrameProfile.Complete(GpuProfiler.Snapshot().Triangles,room==0?terrain.VisibleTriangles:0,room==0?terrain.RejectedQuads:0,drawingScene+RuntimeOptions.Mode*64,room==0?terrain.BuiltThisFrame:0,moved?(moveClip==1?2:1):0,room==0&&terrain.GroundRingActive,room==0&&terrain.GroundRingUploaded,NativeShadows.Stats(8)>0);
    }
    private bool IsTree(GroundItem g)=>g.Kind==3||(g.Kind==2&&g.Item!=2);
    private void InspectTerrain()
    {
        if(room!=0)return;
        for(int z=2;z<Town.Height-2;z+=2)for(int x=2;x<Town.Width-2;x+=2)if(TerrainElevation.Mode(state.Town,x,z)==TerrainElevation.RampNorth){
            state.X=x+.5f;state.Z=z+2.5f;screen="world";facingYaw=(float)Math.PI;moveClip=-1;animationClock.Reset(0);return;
        }
    }
    private void InspectBuilding()
    {
        if(villagers.Doors.Busy(0))return;
        if(room!=0){BuildingWorld.InsideDoor(state,room,out state.X,out state.Z);facingYaw=0;return;}
        int building=1+buildingInspection++%6;
        BuildingWorld.Entrance(state,building,out float x,out float z);state.X=x+.85f;state.Z=z+.2f;facingYaw=(float)Math.PI;state.Facing=facingYaw;screen="world";waterInspection=0;cliffPreview=false;
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
        trees.Draw(renderer,state.Town,visibleTrees,state.Z);
    }
    private void Text(string text,float x,float y,Color32 c,float scale=1.5f,int visibleCharacters=-1)=>font.Draw(ui,text,x,y,c,scale/1.5f,0,visibleCharacters);
    private void Strong(string text,float x,float y,Color32 c,float scale=1.5f)=>strongFont.Draw(ui,text,x,y,c,scale/1.5f);
    private void Heading(string text,float x,float y,Color32 c,float scale=1)=>headingFont.Draw(ui,text,x,y,c,scale,(float)Math.Sin(tick*.018f+x*.01f)*1.8f);
    private void DoorPopover(string label,bool action)
    {
        const float normalScale=1.05f;
        float measured=strongFont.Measure(label,normalScale/1.5f),padding=action?62:32;
        float width=Math.Max(224,Math.Min(584,measured+padding)),left=(640-width)*.5f;
        float scale=normalScale*Math.Min(1,(width-padding)/Math.Max(1,measured));
        Panel(left,320,width,40);
        if(action){
            // Match the controller's round green A key using cached UI shapes.
            Rounded(left+12,328,24,24,12,new Color32(53,105,84));
            Strong("A",left+24-strongFont.Measure("A",normalScale/1.5f)*.5f,328,Color32.White,normalScale);
        }
        Strong(label,left+(action?48:16),328,new Color32(49,72,62),scale);
    }
    private void Panel(float x,float y,float w,float h)
    {UiNoise.Begin(); float radius=Math.Min(30,h*.5f);if(batchedDrawing&&uiShapes.DrawPanel(ui,x,y,w,h,radius)){UiNoise.End();return;}Bulged(x+4,y+5,w,h,radius,new Color32(49,72,62,130),false);Bulged(x,y,w,h,radius,new Color32(255,245,221),false); UiNoise.End();}
    private void Bulged(float x,float y,float w,float h,float radius,Color32 color,bool cache=true)
    {UiNoise.Begin();
        if(cache&&batchedDrawing&&uiShapes.Draw(ui,x,y,w,h,radius,true,color)){UiNoise.End();return;}
        Rounded(x,y,w,h,radius,color,false);if(h<=radius*2+1){UiNoise.End();return;}
        float start=y+radius,span=h-2*radius;
        for(int side=0;side<2;side++)
        {
            float edge=side==0?x:x+w,direction=side==0?-1:1;
            for(int i=0;i<20;i++)
            {float a=i/20f,b=(i+1)/20f;ui.FillTriangle(new Vector2(edge-direction*2,y+h*.5f),new Vector2(edge+direction*9*(float)Math.Sin(a*3.1415927f),start+a*span),new Vector2(edge+direction*9*(float)Math.Sin(b*3.1415927f),start+b*span),color);}
        }
    UiNoise.End();}
    private void Rounded(float x,float y,float w,float h,float radius,Color32 color,bool cache=true)
    {UiNoise.Begin();
        if(cache&&batchedDrawing&&uiShapes.Draw(ui,x,y,w,h,radius,false,color)){UiNoise.End();return;}
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
    UiNoise.End();}
    private void SpeechBubble()
    {
        Rounded(36,329,576,108,30,new Color32(48,65,58,130));
        Rounded(32,324,576,108,30,new Color32(255,245,221));
        UiNoise.Begin();ui.FillTriangle(new Vector2(112,428),new Vector2(142,428),new Vector2(118,446),new Color32(255,245,221));UiNoise.End();
        Rounded(48,299,180,38,19,new Color32(53,105,84));
        Strong(speaker,62,306,new Color32(255,245,221),1.35f);Text(wrappedDialogue,57,344,new Color32(49,72,62),1.4f,typeAt);
    }
    private static string Letter(char c)=>new string(new[]{c});
    private void DrawHud()
    {
        var ink=new Color32(49,72,62);
        if(screen=="plot"&&session.Draft!=null){
            session.Draft.CanPlaceHome((int)state.X-3,(int)state.Z-6,out string reason);
            Panel(150,24,340,42);Heading("Find your little corner",168,31,ink,1.05f);
            Panel(28,355,584,101);Rounded(44,342,104,28,14,new Color32(53,105,84));
            Strong("Hearth",58,346,new Color32(255,245,221),1.1f);
            Text(reason,48,372,ink,1.13f);
            Text("Your home and garden need 15 x 13 clear tiles.",48,402,ink,1);
            Text("Stick move   A choose spot   B back",48,430,ink,1.05f);return;
        }
        if(FrameProfile.Enabled){
            if(++profileLabelFrames>=30){profileLabelFrames=0;profileNativeLabel="Native: "+NativeShadows.Stats(9)+" slots / "+NativeShadows.Stats(10)/1024+" KiB";profileShadowLabel=(room==0&&terrain.GroundRingActive?"Ring / ":"")+"Shadows: "+NativeShadows.Stats(0)+" pages / "+NativeShadows.Stats(2)+" updated";}
            Text(indexedModels?(batchedDrawing?"GX indexed / batched":"GX indexed / baseline"):(batchedDrawing?"Direct / batched":"Direct / baseline"),420,397,ink,.8f);
            Text(profileNativeLabel,420,408,ink,.55f);
            Text(profileShadowLabel,420,419,ink,.55f);
        }
        Rounded(20,440,420,29,14.5f,new Color32(48,76,57,210));Text("A interact   X backpack   Y map",32,451,Color32.White,1.3f);
        if(screen=="world"&&room!=0){Panel(205,371,410,61);Text("C-stick turn view / L recenter",220,380,ink,.95f);Text(buildingPrompt.Name(state,BuildingWorld.Building(room)),220,402,ink,1.1f);}
        if(screen=="world"){
            int door=villagers.Doors.Nearby(state,0,facingYaw);
            if(door>=0||villagers.Doors.Busy(0)){
                bool action=!villagers.Doors.Busy(0);
                string label=action?room==0?buildingPrompt.Enter(state,door):"Exit":villagers.Doors.Actors[0].Phase==DoorPhase.Waiting?"Waiting at door":"Through the doorway";
                DoorPopover(label,action);
            }
        }
        if(screen=="world"||screen=="plot"){Panel(20,371,170,61);Text(townClock.Date,32,380,ink,1.1f);Strong(townClock.Time,32,400,ink,1.6f);}
        if(screen=="dialogue")
        { SpeechBubble();return; }
        if(screen=="map"){DrawMap();return;}
        if(screen=="plot"){Panel(55,370,530,55);Text("Choose a 15 x 13 plot. A: confirm",70,385,ink);return;}
        if(screen=="saving")
        { Panel(115,181,410,132);Heading("Saving your town",142,199,ink);Text("Please keep the memory\ncard inserted.",142,245,ink,1.25f);return; }
        if(screen=="world"){if(room==0&&runningMinimap.Visible)DrawMinimap();if(waterInspection!=0)Text("Water inspection "+waterInspection+" / L+R+Left cycles",42,320,ink);if(cliffPreview)Text("Cliff kit preview / L+R+Up closes",42,343,ink);return;}
        if((screen=="menu"&&menuTab==0)||screen=="sell"||screen=="item"){DrawBackpack();return;}
        Panel(45,75,550,350);string title=screen=="menu"?tabs[menuTab]:screen=="shop"?"Store stock":screen=="store"?"Hearth's store":screen=="post"?"Town hall":screen=="mail"?"Mailbox":"Lost and Found";
        Heading(title,68,91,ink);
        int total=screen=="menu"?TabCount():screen=="shop"?state.Town.StockCount:screen=="mail"?Math.Max(1,state.MailCount):screen=="lost"?Math.Max(1,LostCount()):screen=="store"?8:7;
        bool compact=screen=="menu"&&(menuTab==SettingsTab||menuTab==TestingTab);int page=compact?9:8,step=compact?24:29;float rowScale=compact?1.2f:1.4f;int first=selected/page*page;
        for(int i=first;i<Math.Min(total,first+page);i++){float y=131+(i-first)*step;if(i==selected){Rounded(57,y,520,step-2,(step-2)*.5f,new Color32(221,231,188));Strong("> "+RowLabel(i),68,y,ink,rowScale);}else Text("  "+RowLabel(i),68,y,ink,rowScale);}
        Text(screen=="menu"&&menuTab==TestingTab&&(selected==25||selected==26||selected==29)?"Left/Right: adjust   A: increase   Z: reset":screen=="menu"&&menuTab==TestingTab&&selected==20?"Left/Right: 5%   A: +5%   Z: reset":screen=="menu"&&menuTab==TestingTab&&selected==23?"Left/Right: 1 square   A: +1   Z: Auto":screen=="menu"&&menuTab==TestingTab&&selected==24?"A / Left/Right: switch   Z: Wind Waker":"Stick / D-pad choose   L/R tabs   A select   B close",65,392,ink,1);
        if(screen=="menu"&&menuTab==SettingsTab){
            Text("Manual saves only. Choose Save now to write the card.",65,331,ink,1);
            Text(saveStatusText.Get(saveStatus),65,357,ink,1);
        }
        if(screen=="menu"&&menuTab==TestingTab)Text("Session options "+(first+1)+"-"+Math.Min(total,first+page)+" / "+total,65,357,ink,1);
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
        if(menuTab==TestingTab)return TestingLabel(i);
        string[] settings={"Save now","Controls","About / build identity","Calendar","Chat settings","Debug / testing menu"};return settings[i];
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
    private void DrawMinimap()
    {
        Bulged(MapBackground.MinimapLeft+4,MapBackground.MinimapTop+5,MapBackground.MinimapSize,MapBackground.MinimapSize,MapBackground.MinimapRadius,new Color32(49,72,62,130));
        mapBackground.DrawMinimap(ui);
        MinimapMarker(32,26,new Color32(187,107,71),3);
        MinimapMarker(50,26,new Color32(132,96,155),3);
        for(int p=0;p<GameState.ProfileCount;p++)if(state.HomePlaced(p))MinimapMarker(state.HomeX(p)+3,state.HomeZ(p)+3,new Color32(220,182,124),4);
        MinimapMarker(state.X,state.Z,new Color32(35,49,44),6);
        MinimapMarker(state.X,state.Z,Color32.White,4);
    }
    private void MinimapMarker(float x,float z,Color32 color,float size)
    {
        var p=MapBackground.MinimapPosition(x,z);
        ui.FillRectangle(new RectF(p.X-size*.5f,p.Y-size*.5f,size,size),color);
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
        for(int p=0;p<GameState.ProfileCount;p++)if(state.HomePlaced(p))ui.FillRectangle(new RectF(left+state.HomeX(p)*scale,top+state.HomeZ(p)*scale,GameState.GardenWidths[state.HomeGardenTier(p)]*scale,GameState.GardenHeights[state.HomeGardenTier(p)]*scale),new Color32(220,182,124,180));
        for(int i=0;i<6;i++){var v=state.Town.Residents[i];ui.FillRectangle(new RectF(left+v.X*scale,top+v.Z*scale,3,3),new Color32(153,76,103));}
        float mapX=state.X,mapZ=state.Z;if(room!=0)BuildingWorld.Entrance(state,BuildingWorld.Building(room),out mapX,out mapZ);
        ui.FillRectangle(new RectF(left+mapX*scale,top+mapZ*scale,4,4),Color32.White);
    }
}
