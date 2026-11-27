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
        AnimalTown.Jobs.JobMemory.ReclaimTemporaryText=ReclaimJobText;
        DebugOverlay.PerformanceVisible=false;
        DebugOverlay.WriteLine("AnimalTown "+BuildInfo.Version);
        ContentBootstrap.Initialize();
        if(NativeRenderProbe.Indexed(1)!=1||NativeRenderProbe.Batch(1)!=1)
            throw new InvalidOperationException("GX indexed and batched rendering required");
        RuntimeOptions.Load();
        SaveCards.Initialize();
        var config=DevConfigStorage.Load();
        DevConfigStorage.ApplyGlobal(config);
        var options=UserOptionsStorage.Load();VideoOptions.Load(options);
        StartupSplash.Run();
        while(true){
            bool start=TitleScreen.Run(config,options);
            if(TitleScreen.OnlineEraseRequested)TownDataStorage.DeleteOnline(config);
            else if(start)RunTown(config);
            else return 0;
            // The entire prior town frame must unwind, including its session,
            // before title resources or a second full mailbox are loaded.
            LoadingScreen.Finish();TextureCache.TrimUnused();
            AnimalTown.GameCube.NativeTexturePages.Collect();
        }
    }
    private static void RunTown(DevConfig config)
    {
        DebugOverlay.WriteLine("ANIMALTOWN TOWN LOAD BEGIN");
        FrameProfile.Checkpoint(1);
        var session=TownSession.Load();LoadingScreen.Finish();
        DebugOverlay.WriteLine("ANIMALTOWN TOWN LOAD READY");
        FrameProfile.Checkpoint(2);
        try{
#if ANIMALTOWN_ONLINE_PLAY
            DebugOverlay.WriteLine("ANIMALTOWN ONLINE RECOVERY BEGIN");
            if(!OnlinePlay.Recover(config,session))return;
            DebugOverlay.WriteLine("ANIMALTOWN ONLINE RECOVERY READY");
#else
            if(session.OnlineRecoveryRequired){
                var recoveryFont=UiFont.LoadSystem();
                try{while(true){LoadingScreen.Draw("Keep this home card.\nFinish the online return using an online-enabled build.\nA / B: return to title",recoveryFont);RuntimeOptions.Wait();Controller.Update();var recoveryPad=Controller.GetState(0);if(recoveryPad.A.WasPressed||recoveryPad.B.WasPressed)return;}}
                finally{LoadingScreen.Finish();recoveryFont.Release();}
            }
#endif
            DebugOverlay.WriteLine("ANIMALTOWN PROFILE PICKER BEGIN");
            if(!ProfilePicker.Run(session,config))return;
            session.State.Town.Traffic.ResetSnowForTownEntry();
            if(session.Draft!=null&&!OnboardingFlow.Run(config,session))return;
            while(true){
                int action=RunSession(config,session);
#if ANIMALTOWN_ONLINE_PAIRING
                if(action==TownGame.OnlinePairingRestart){
#if ANIMALTOWN_ONLINE_PLAY
                    if(!OnlinePlay.Run(config,session))break;
#else
                    OnlinePairing.Run(session);
#endif
                    continue;
                }
#endif
                if(action!=TownGame.TravelRestart)break;
                if(session.Flight.Phase==FlightPhase.Cruise)LoadingScreen.BeginFlight(session.Flight.Direction);
                if(!session.CompleteTravel()&&!OfflineTravelRecovery.Run(session,session.TravelError))break;
            }
        }finally{session.DiscardDraft();}
    }
    private static void ReclaimJobText()=>AnimalTown.GameCube.NativeTexturePages.Collect();
    internal static int RunSession(DevConfig config,TownSession session)
    {
        LoadingScreen.Draw();TextureCache.TrimUnused();
        AnimalTown.GameCube.NativeTexturePages.Collect();
        session.RefreshVisitors();
        DebugOverlay.WriteLine("ANIMALTOWN TITLE RELEASED native bytes "+GraphicsDevice.Default.NativeBytes);
        DebugOverlay.WriteLine("ANIMALTOWN GAME START");
        DebugOverlay.WriteLine("ANIMALTOWN ARAM INIT BEGIN");
        if(!Aram.Initialize(2*1024*1024))throw new InvalidOperationException("Audio ARAM reservation failed");
        DebugOverlay.WriteLine("ANIMALTOWN ARAM INIT READY");
        LoadingScreen.Pulse();
        DebugOverlay.WriteLine("ANIMALTOWN GAME CONSTRUCT BEGIN");
        FrameProfile.Checkpoint(3);using var game=new TownGame(config,session);FrameProfile.Checkpoint(4);
        DebugOverlay.WriteLine("ANIMALTOWN GAME CONSTRUCT READY");
        AnimalTown.GameCube.NativeTexturePages.Collect();
        long previous=Clock.Timestamp;double accumulator=0;
        try{
            while(true)
            {
#if ANIMALTOWN_ONLINE_PLAY
                if(session.Online!=null)NativeFrame.OnlineProbe(1);
#endif
                FrameProfile.Begin();long loading=FrameProfile.Start();game.AdvanceLoading();FrameProfile.End(2,loading);
                game.AdvanceResidency();long simulation=FrameProfile.Start();
                long now=Clock.Timestamp;accumulator+=(now-previous)/1000000.0;previous=now;if(accumulator>.1334)accumulator=.1334;
#if ANIMALTOWN_ONLINE_PLAY
                if(session.Online!=null)NativeFrame.OnlineProbe(2);
#endif
                Controller.Update();var pad=Controller.GetState(0);game.Input(pad);
#if ANIMALTOWN_ONLINE_PLAY
                if(session.Online!=null)NativeFrame.OnlineProbe(3);game.OnlineFrame(pad);if(session.Online!=null)NativeFrame.OnlineProbe(4);
                if(session.Online!=null&&(!session.Online.Connected||session.Online.ReturnRequested||session.Online.ControlRequested!=0))return TownGame.OnlinePairingRestart;
#endif
#if ANIMALTOWN_ONLINE_PAIRING
                if(game.OnlinePairingRequested)return TownGame.OnlinePairingRestart;
#endif
                if(game.TravelRestartRequested)return TownGame.TravelRestart;
                if(game.ExitToTitle)return OnboardingFlow.ToTitle;
                if(game.NextSetup!=0)return game.NextSetup;
                // Setup and preview scopes have returned before gameplay starts.
                // Their disposed views/snapshots cannot remain stack roots here.
                game.ReleaseIntro();
                while(accumulator>=1.0/30){
                    if(!game.StepHelipadFlight())game.Step(pad);
                    if(game.TravelRestartRequested)return TownGame.TravelRestart;
                    accumulator-=1.0/30;
                }
#if ANIMALTOWN_ONLINE_PLAY
                if(session.Online!=null)NativeFrame.OnlineProbe(5);
#endif
                FrameProfile.End(0,simulation);game.Draw();
#if ANIMALTOWN_ONLINE_PLAY
                if(session.Online!=null)NativeFrame.OnlineProbe(6);
#endif
                game.AdvanceResidency(afterDraw:true);
#if ANIMALTOWN_ONLINE_PLAY
                if(session.Online!=null)NativeFrame.OnlineProbe(7);
#endif
                long delay=FrameProfile.Start();RuntimeOptions.Wait();FrameProfile.End(13,delay);game.RecordFrame();
#if ANIMALTOWN_ONLINE_PLAY
                if(session.Online!=null)NativeFrame.OnlineProbe(8);
#endif
            }
        }catch(Exception error){DebugOverlay.WriteLine("ANIMALTOWN SESSION FAILED "+error.Message);throw;}
    }
}

internal sealed partial class TownGame : IDisposable,Hypercube.GameCube.IFrameScene
{
    private GameState state;
    private readonly TownSession session;
    private bool releaseIntro,disposed;
    internal int NextSetup {get;private set;}
    internal bool ExitToTitle {get;private set;}
    private readonly ModelResidency modelResidency=new ModelResidency();
    private readonly VisualMemoryCoordinator visualMemory=new VisualMemoryCoordinator();
    private readonly WorldObjectCandidates objectCandidates=new WorldObjectCandidates();
    private readonly VillagerModelLod modelLod;
    private readonly StartupModelAdmission startupAdmission;
    private bool trimmedForPressure;
    private bool modelSceneReady=true,terrainExitReady=true,snowExitReady,outdoorModelLookahead;
    private int requestedUv=-1;
    private readonly VisualResidencyBudget residencyBudget=new VisualResidencyBudget();
    private float residencyX=float.NaN,residencyZ;
    private int residencySpace=-1,residencyFailures;
    private long playerStillSince,spaceChangedAt;
    private readonly TownClock townClock=new TownClock();
    private readonly FloatingClockHud clockHud;
    private HudFade clockFade;
    private bool playerRunning;
    private readonly TownLights townLights=new TownLights();
    private readonly DevConfig debugConfig;
    private readonly RenderDebugOptions renderDebug=new RenderDebugOptions();
    private readonly VillagerAttentionRenderer attention=new VillagerAttentionRenderer();
    private readonly FallLeafWake foliage=new FallLeafWake();
    private readonly RainRenderer rain=new RainRenderer();
    private readonly VisitEdgeRenderer visitEdges=new VisitEdgeRenderer();
    private readonly ActorFrameVisibility actorVisibility=new ActorFrameVisibility();
    private readonly AnimationFrameClock animationClock=new AnimationFrameClock(BuildingWorld.Actors);
    private readonly bool[] hasIdleClip=new bool[6];
    private readonly bool[] visualOverrides=new bool[BuildingWorld.Actors],visualIdle=new bool[BuildingWorld.Actors],visualAnimated=new bool[BuildingWorld.Actors];
    private readonly bool[] restartClips=new bool[BuildingWorld.Actors];
    private readonly int[] appliedClips=new int[BuildingWorld.Actors];
    private float desiredPlayerSpeed=1.05f,appliedPlayerSpeed=1.05f;
    private readonly VillagerJourney villagers=new VillagerJourney();
    private readonly InteriorCamera indoorCamera=new InteriorCamera();
    private readonly Camera indoorView=new Camera(CylinderWorld.CameraFieldOfView,0,.1f,64);
    private readonly BuildingRenderer buildings;
    private int room {get=>state.Space;set=>state.Space=value;}
    private readonly bool[] villagersMoving=new bool[Town.NpcActorCount];
    private readonly GraphicsDevice device=GraphicsDevice.Default;
    private readonly Renderer renderer=new Renderer(GraphicsDevice.Default,128);
    private readonly Camera camera=new Camera(CylinderWorld.CameraFieldOfView,0,CylinderWorld.Near,CylinderWorld.Far);
    private readonly Draw2D ui;
    private readonly UiFont font,headingFont,strongFont;
    private CachedTexture itemIcons=null!;
    private readonly Transform worldRoot=new Transform(),transform=new Transform(),identity=new Transform();
    private readonly TerrainRenderer terrain;
    private readonly VillagerShadows villagerShadows;
    private readonly int[] visibleTrees=new int[LowPolyTrees.Capacity];
    private readonly LowPolyTrees trees;
    private readonly DroppedItemRenderer droppedItems=new DroppedItemRenderer();
    private readonly UiShapes uiShapes=new UiShapes();
    private readonly MapBackground mapBackground=new MapBackground();
    private readonly RunningMinimap runningMinimap=new RunningMinimap();
    private readonly MenuNavigation menuNavigation=new MenuNavigation();
    private readonly DaylightLocationMenu daylightMenu=new DaylightLocationMenu();
    private readonly BuildingPrompt buildingPrompt=new BuildingPrompt();
    private readonly WrappedText dialogueText=new WrappedText(41),itemTitleText=new WrappedText(14),saveStatusText=new WrappedText(48);
    private string wrappedDialogue="";
    private int actionWidthItem=-1,actionWidthRoom=-1,actionWidthCount=-1;
    private string actionWidthTitle="";
    private float actionWidth,actionRotateOffset;
    private readonly RenderQueue actorQueue=new RenderQueue(BuildingWorld.Actors,BuildingWorld.Actors);
    private readonly Transform[] modelPoses=new Transform[16];
    private readonly BoxBatch boxes;
    private int modelUsed,loadingIndex,startupRequiredMask=-1,startupWaitingSpecies=-1;
    private Task<Model>? loadingModel;
    private bool resourcesReady,loadingFailed,loadingBuildingsStarted,loadingBuildingsReady,loadingTerrainStarted,loadingTerrainReady;
    private string loadingLabel="Preparing town assets";
    private float cachedPlayerHeight;
    private float profiledX=float.NaN,profiledZ;
    private string profileNativeLabel="",profileShadowLabel="";
    private int profileLabelFrames=30;

    private readonly Material plain=new Material{Cull=CullMode.Back,Lighting=LightingMode.Unlit};
    private readonly Model[] models=new Model[6];
    private readonly ModelInstance?[] actors=new ModelInstance?[BuildingWorld.Actors];
    private readonly string[] modelNames={"fox","bunny","giraffe","cat","hamster","spaniel"};
    private readonly Mesh cube;
    private readonly byte[] banner,icon;
    private readonly Sound syllable;
    private readonly DialogueVoice dialogueVoice;
    private MemoryCard? card;
    private string screen="world",message="",speaker="AnimalTown",saveStatus="Not saved";
    private bool returnToMenu;
    private int returnMenuTab,returnMenuSelection;
    partial void RetireSnowResources()
    {
        rain.RetireSnow();
        if(!terrain.RetireWinterResources(state.Town))throw new InvalidOperationException("Seasonal snow release failed");
        AnimalTown.GameCube.NativeTexturePages.Collect();
    }
    private int selected,slot,rotation,tick,day,month,menuTab,faceX,faceZ=-1,typeAt,voiceTick;
    private int moveClip=-1;
    private long sessionStart;
    private float facingYaw=3.1415927f;
    private bool loaded,relocate,damagedSave;
    // Keep the cursor on the starter plot's safe doorway landing, including
    // the southern town edge. Larger gardens retain the existing view offset.
    internal static int PlotCursorDepth(GameState state)=>Math.Min(6,state.HomePlotHeight(state.ActiveProfile));
    private int PlotX=>(int)state.X-3;
    private int PlotZ=>(int)state.Z-PlotCursorDepth(state);
    private static readonly string[] plotSizeHelp={"Your home and garden need 7 x 5 tiles.","Your home and garden need 9 x 7 tiles.","Your home and garden need 11 x 9 tiles.","Your home and garden need 13 x 11 tiles.","Your home and garden need 15 x 13 tiles."};
    private bool CanChoosePlot(out string reason)
    {
        if(!state.CanPlaceHouse(PlotX,PlotZ,out reason))return false;
        if(relocate&&!godMode&&state.Wallet<50000){reason="Save up 50,000 before moving your home.";return false;}
        reason="A lovely spot! Trees in the way will be cleared.";return true;
    }
    private static readonly bool batchedDrawing=true;
    private bool godMode=false; // TownGame lifetime only; never written to saves or DevConfig.
    private int uvDebug;
    private bool cliffPreview;
    private int waterInspection;
    private int buildingInspection;
    private const int SettingsTab=5,TestingTab=6,DaylightSettingsRow=6;
    // Stable IDs keep hidden/retired controls out of both debug menus.
    private const int SeasonPreviewOption=18,TimePreviewOption=39;
    private const int ProfilingModeOption=41,ExportProfileOption=42,VillagerVisualOption=44;
    private static readonly int[] testingOptions={0,2,3,11,16,17,35,SeasonPreviewOption,TimePreviewOption,19,21,22,23,34,36,37,38,31,32,ProfilingModeOption,ExportProfileOption,VillagerVisualOption};
    private static readonly string[] groundMipLabels={"0","1"};
    private static int TestingOption(int row)=>testingOptions[row];
    private static readonly string[] tabs={"Backpack","Jobs","Catalog","Home","Post office","Settings","Debug / testing"};
    internal TownGame(DevConfig config,TownSession session)
    {
        this.session=session;releaseIntro=session.Draft==null;
        // Font batches contain at most 128 quads / 768 vertices. Canvas fills
        // flush automatically; retained panel geometry has its own native mesh.
        ui=new Draw2D(device,new Draw2DOptions{Width=640,Height=480,VertexCapacity=1024,SpriteCapacity=UiFont.BatchCapacity});
        font=UiFont.LoadSystem();strongFont=font;clockHud=new FloatingClockHud(font);headingFont=UiFont.Load("gorditas-bold");cube=Cube();
        boxes=new BoxBatch(renderer,cube);for(int i=0;i<modelPoses.Length;i++)modelPoses[i]=new Transform{Parent=worldRoot};
        for(int i=0;i<appliedClips.Length;i++)appliedClips[i]=-2;
        LoadingScreen.Pulse();
        transform.Parent=worldRoot;identity.Parent=worldRoot;
        banner=File.ReadAllBytes(ContentPaths.Asset("savebanner.rgb5a3"));icon=File.ReadAllBytes(ContentPaths.Asset("saveicon.rgb5a3"));
        DebugOverlay.WriteLine("ANIMALTOWN GAME UI READY");
        var pcm=new byte[512];for(int i=0;i<pcm.Length;i++)pcm[i]=(byte)(128+(i%24<12?25:-25)*(pcm.Length-i)/pcm.Length);
        DebugOverlay.WriteLine("ANIMALTOWN GAME AUDIO BEGIN");
        syllable=Sound.FromPcm8(pcm,16000);syllable.Volume=.12f;dialogueVoice=new DialogueVoice(syllable);
        DebugOverlay.WriteLine("ANIMALTOWN GAME AUDIO READY");
        sessionStart=Clock.Timestamp;
        state=session.State;
        ReadCalendar();
        debugConfig=config;
        loaded=session.Loaded;damagedSave=session.Damaged;saveStatus=session.Status;day=Math.Max(day,state.Town.Day);month=Math.Max(month,state.Town.Month);
        BuildingCatalog.Configure(File.ReadAllBytes(ContentPaths.Asset("buildings.bin")));
        LoadingScreen.Pulse();
        screen=session.Draft!=null?"plot":"world";
        if(session.Draft!=null&&!session.PreviewStarted){
            DebugOverlay.WriteLine("ANIMALTOWN INITIAL PLOT BEGIN");
            FindStartingPlot();session.PreviewStarted=true;
            DebugOverlay.WriteLine("ANIMALTOWN INITIAL PLOT READY");
        }
        facingYaw=state.Facing;
        InitializeJobs();
        if(session.Draft==null&&!state.IsOnlineProjection)state.Rollover(day,month);
        if(room==0&&state.HousePlaced&&!state.CanWalk(state.X,state.Z,0))state.MoveToHouseEntrance();
        DebugOverlay.WriteLine("ANIMALTOWN WORLD RENDERERS BEGIN");
        buildings=new BuildingRenderer(worldRoot);
        trees=new LowPolyTrees(worldRoot);terrain=new TerrainRenderer();villagerShadows=new VillagerShadows(state.IsOnlineProjection?2:0);
        buildings.Memory=visualMemory.Pressure;modelResidency.Memory=visualMemory.Pressure;
        trees.Memory=visualMemory.Pressure;droppedItems.Memory=visualMemory.Pressure;
        terrain.Memory=visualMemory.Pressure;
        modelLod=new VillagerModelLod(models,actors,modelResidency){Memory=visualMemory.Pressure};
        startupAdmission=new StartupModelAdmission(visualMemory.Pressure);
        DebugOverlay.WriteLine("ANIMALTOWN WORLD RENDERERS READY");
        if(NativeShadows.Init(GroundShadow.Atlas(),VillagerShadowPatch.Texture(),CloudShadowPattern.Texture())!=1)throw new InvalidOperationException("Shadow union native budget");
        UiFont.Batched=true;
        uvDebug=config.Values[2]|config.Values[3]<<1;
        weather.RainEnabled=config.Values[16]!=0;weather.RoughOceanWhenRaining=config.Values[17]!=0;
        renderDebug.SetSeason(config.Values[SeasonPreviewOption]-1);
        renderDebug.ForceAttention=config.Values[19]!=0;
        renderDebug.GroundMipmaps=config.Values[22]!=0;for(int i=0;i<config.Values[23];i++)renderDebug.AdjustGroundMipDistance(1);
        renderDebug.ShowGroundMipBands=config.Values[DevConfig.GroundMipBandsIndex]!=0;
        terrain.FreezeMotion=config.Values[24]!=0;terrain.RecreateMeshes=config.Values[25]!=0;if(config.Values[26]!=0)terrain.ToggleGroundRing();
        NativeRim.Enable(0);
    }
    private void FindStartingPlot()
    {
        var draft=session.Draft!;int best=int.MaxValue,bx=15,bz=35;
        int width=state.HomePlotWidth(state.ActiveProfile),height=state.HomePlotHeight(state.ActiveProfile),depth=PlotCursorDepth(state);
        for(int z=1;z<=Town.Height-height;z++)for(int x=1;x<=Town.Width-width;x++){
            int distance=(x-15)*(x-15)+(z-35)*(z-35);
            if(distance>=best||!draft.CanPlaceHome(x,z,out _)||!state.CanWalk(x+3.5f,z+depth+.5f,0))continue;
            // Favor an unobstructed first view without removing garden trees.
            for(int i=0;i<state.Town.GroundCount;i++){
                var g=state.Town.Ground[i];
                if(IsTree(g)&&Math.Abs(g.X+.5f-(x+3.5f))<3&&g.Z+.5f>z+depth&&g.Z+.5f<z+depth+6)distance+=10000;
            }
            if(distance<best){best=distance;bx=x;bz=z;}
        }
        if(best<int.MaxValue){state.X=bx+3.5f;state.Z=bz+depth+.5f;}
    }
    private void OnboardingInput(ControllerState p)
    {
        // Do not load setup imagery beside the resident town. RunSession
        // disposes this renderer and unwinds before the next view is created.
        if(p.B.WasPressed){NextSetup=OnboardingFlow.ToLook;return;}
        if(p.A.WasPressed&&session.Draft!.PlaceHome(PlotX,PlotZ)){
            state.Facing=facingYaw;NextSetup=OnboardingFlow.ToReview;
        }
    }
    internal void ReleaseIntro()
    {
        if(!releaseIntro||!resourcesReady||room==0&&!terrain.Ready)return;
        if(session.Flight.Phase==FlightPhase.Cruise){
            if(!LoadingScreen.FlightFinished())return;
            session.Flight.Land();
            DebugOverlay.WriteLine("ANIMALTOWN FLIGHT LAND "+state.Town.Seed);
        }
        LoadingScreen.Finish();
        TextureCache.TrimUnused();AnimalTown.GameCube.NativeTexturePages.Collect();
        if(session.Draft!=null||OnboardingUi.LiveViews!=0||
           TextureCache.SourceBytesContaining(ContentPaths.Asset("picker-"))!=0||
           TextureCache.SourceBytesContaining(ContentPaths.Asset("wordmark.dndtex"))!=0)
            throw new InvalidOperationException("Onboarding resources survived gameplay transition");
        releaseIntro=false;
        DebugOverlay.WriteLine("ANIMALTOWN ONBOARDING RELEASED native bytes "+device.NativeBytes);
    }
    public void Dispose()
    {
        if(disposed)return;
        if(device.IsFrameActive)throw new InvalidOperationException("Dispose the town after EndFrame");
        disposed=true;
#if ANIMALTOWN_ONLINE_PLAY
        if(session.Online!=null)NativeFrame.OnlineProbe(50);
#endif
        LoadingScreen.Finish();ReleaseChatEntry();ReleaseLetterEntry();ClearChatPresentation();
        dialogueVoice.Stop();syllable.Dispose();
        Model? unadopted=null;
        if(loadingModel!=null){try{unadopted=loadingModel.GetAwaiter().GetResult();}catch(Exception){if(!loadingModel.IsCompleted)throw;}loadingModel=null;}
        modelLod.Dispose();
#if ANIMALTOWN_ONLINE_PLAY
        if(session.Online!=null)NativeFrame.OnlineProbe(51);
#endif
        modelResidency.Stop();
#if ANIMALTOWN_ONLINE_PLAY
        if(session.Online!=null)NativeFrame.OnlineProbe(52);
#endif
#if ANIMALTOWN_ONLINE_PLAY
        DisposeOnlineActors();NativeFrame.OnlineProbe(53);
#endif
        for(int i=0;i<actors.Length;i++){actors[i]?.Dispose();actors[i]=null;}
        visualSwap?.Dispose();visualSwap=null;
        if(unadopted!=null&&Array.IndexOf(models,unadopted)<0)unadopted.Dispose();
        for(int i=0;i<models.Length;i++){models[i]?.Dispose();models[i]=null!;}
        startupAdmission.Cancel();
        jobScene.Dispose();attention.Dispose();rain.Dispose();foliage.Dispose();trees.Dispose();droppedItems.Dispose();buildings.Dispose();terrain.Dispose();fences.Dispose();helipad.Dispose();
        clockHud.Dispose();uiShapes.Dispose();mapBackground.Dispose();cube.Dispose();ui.Dispose();
        font.Release();headingFont.Release();if(itemIcons!=null)TextureCache.Release(itemIcons);card?.Dispose();card=null;
        NativeRim.Enable(0);NativeRenderProbe.Uv(0);
        NativeWorldDiagnostics.PrepareEdges(0);
        if(NativeShadows.Shutdown()!=1)throw new InvalidOperationException("Shadow shutdown failed");
        TextureCache.TrimUnused();
#if ANIMALTOWN_ONLINE_PLAY
        if(session.Online!=null)NativeFrame.OnlineProbe(54);
#endif
    }
    internal void AdvanceLoading()
    {
        if(resourcesReady||loadingFailed)return;
        try{
            visualMemory.Sample(Clock.Timestamp);
            terrain.WarmCliffs(state.Town,Clock.Timestamp+1000);
            if(!loadingTerrainReady){
                if(!loadingTerrainStarted){loadingTerrainStarted=true;DebugOverlay.WriteLine("ANIMALTOWN STARTUP TERRAIN BEGIN");}
                loadingLabel="Preparing landscape";
                if(!PrepareStartupTerrain())return;
                // The helper has returned and its scratch-building frames have
                // unwound. Collect now; the next frame starts the compact kit.
                AnimalTown.GameCube.NativeTexturePages.Collect();
                loadingTerrainReady=true;
                DebugOverlay.WriteLine("ANIMALTOWN STARTUP TERRAIN READY native bytes "+device.NativeBytes);
                return;
            }
            if(room!=0&&!terrain.SuspendOutdoorCache())return;
            if(!loadingBuildingsReady){
                // Admit the compact kit before large species packages occupy MEM1.
                if(!loadingBuildingsStarted){loadingBuildingsStarted=true;DebugOverlay.WriteLine("ANIMALTOWN BUILDINGS LOAD BEGIN");}
                bool complete=buildings.AdvanceLoading(state);loadingLabel=buildings.LoadingLabel;if(!complete)return;
                buildings.Begin(villagers.Doors);
                for(int actor=0;actor<BuildingWorld.Actors;actor++){
                    if(actor>0&&!state.NpcPresent(actor-1))continue;
                    var location=BuildingWorld.Location(state,actor);if(location.Space.IsTown)continue;int building=BuildingWorld.Building(location.Space.Value);
                    if(!buildings.Ready(state.BuildingTypes[building])){BuildingWorld.Entrance(state,building,out float rx,out float rz);BuildingWorld.SetLocation(state,actor,new ActorLocation(0,rx,rz+1,0));saveStatus="A home is unavailable; its occupants are outside.";}
                }
                loadingBuildingsReady=true;
                DebugOverlay.WriteLine("ANIMALTOWN BUILDINGS LOAD READY");
                return;
            }
            if(loadingIndex<models.Length){
                if(startupRequiredMask<0){
                    if(!PrepareStartupResidency())return;
                    startupRequiredMask=0;
                    for(int i=0;i<models.Length;i++)if(modelResidency.Required(i))startupRequiredMask|=1<<i;
                }
                int species=ModelStartup.SpeciesAt(state.Species,loadingIndex,startupRequiredMask);
                if(loadingModel==null){
                    loadingLabel="Preparing villagers";
                    if(!PrepareStartupResidency())return;
                    if(!startupAdmission.Begin(species,StartupInstanceCount(species),Clock.Timestamp)){
                        if(startupWaitingSpecies!=species){
                            startupWaitingSpecies=species;
                            DebugOverlay.WriteLine("ANIMALTOWN STARTUP ADMISSION WAIT "+species+" free "+visualMemory.Pressure.FreeBytes+" reserved "+visualMemory.Pressure.ReservedBytes+" requested "+visualMemory.Pressure.WaitingPeakBytes);
                        }
                        return;
                    }
                    startupWaitingSpecies=-1;
                    loadingLabel="Loading "+modelNames[species];
                    DebugOverlay.WriteLine("ANIMALTOWN MODEL LOAD BEGIN "+species+" "+modelNames[species]+" progress "+(loadingIndex+1)+" / "+models.Length+" required species "+modelResidency.RequiredSpeciesCount);
                    try{loadingModel=ModelResources.LoadAsync(modelNames[species]);}
                    catch(Exception error){if(startupAdmission.Fail(error,Clock.Timestamp))return;throw;}
                    return;
                }
                if(!loadingModel.IsCompleted)return;
                if(!AdoptStartupModel(species))return;
                DebugOverlay.WriteLine("ANIMALTOWN MODEL LOAD READY "+species+" "+modelNames[species]+" progress "+(loadingIndex+1)+" / "+models.Length);
                loadingModel=null;loadingIndex++;return;
            }
            if(!PrepareStartupResidency()||!modelResidency.ReadyForSpace(state,villagers.Doors,room))return;
            if(actors[0]==null)throw new InvalidOperationException("Selected player model was not prepared");
            if(uvDebug!=0&&!modelResidency.AllReady)return;
            if(!droppedItems.Load())return;
            // Gameplay menus only draw after startup. Keep the icon atlas out
            // of the temporary MEM1 peak while villager models are admitted.
            itemIcons=TextureCache.Load(ContentPaths.Asset("item-icons.dndtex"),true);
            // UV regions require the fully loaded model set. Enabling them in
            // the constructor would leave an empty cache and hide the actors.
            if(uvDebug!=0&&NativeRenderProbe.Uv(uvDebug)!=uvDebug){uvDebug=0;saveStatus="UV preview unavailable: not enough memory.";}
            resourcesReady=true;
            DebugOverlay.WriteLine("BUILDINGS READY native bytes "+buildings.ResidentBytes);
            int populationMask=1<<state.Species,populationActors=1;
            for(int npc=0;npc<Town.NpcActorCount;npc++)if(state.NpcPresent(npc)){populationMask|=1<<state.NpcSpecies(npc);populationActors++;}
            DebugOverlay.WriteLine("ANIMALTOWN MODEL POPULATION mask "+populationMask+" actors "+populationActors);
            DebugOverlay.WriteLine("ANIMALTOWN READY "+BuildInfo.Version+" native bytes "+device.NativeBytes);
        }catch(Exception error){loadingFailed=true;loadingLabel="Could not load town assets. Restart with a valid disc.";DebugOverlay.WriteLine("ANIMALTOWN ASSET LOAD FAILED "+error.Message+" required species "+modelResidency.RequiredSpeciesCount);}
    }
    private int StartupInstanceCount(int species)
    {
        int count=species==state.Species?1:0;
        for(int npc=0;npc<Town.NpcActorCount;npc++)if(state.NpcPresent(npc)&&state.NpcSpecies(npc)==species)count++;
        return count;
    }
    private bool AdoptStartupModel(int species)
    {
        Model? model=null;var staged=new ModelInstance?[Town.NpcActorCount];ModelInstance? player=null;
        try{
            model=loadingModel!.GetAwaiter().GetResult();loadingModel=null;
            startupAdmission.Validate(model);
            ModelStartup.RetainSelected(state.Species,species,model);
            for(int npc=0;npc<Town.NpcActorCount;npc++)if(state.NpcPresent(npc)&&state.NpcSpecies(npc)==species)
                staged[npc]=model.CreateInstance(ModelMemoryPlan.MaximumInstanceBytes);
            player=ModelStartup.ReservePlayer(state.Species,species,model);
        }catch(Exception error){
            for(int npc=0;npc<staged.Length;npc++)staged[npc]?.Dispose();
            player?.Dispose();model?.Dispose();loadingModel=null;
            if(!startupAdmission.Fail(error,Clock.Timestamp))throw;
            DebugOverlay.WriteLine("ANIMALTOWN STARTUP MODEL RETRY "+species+": "+error.Message);return false;
        }
        // Publish a complete shared-species cohort before releasing admission.
        modelResidency.Register(species,model,Clock.Timestamp);models[species]=model;
        int instanceBytes=player?.NativeBytes??0;
        for(int npc=0;npc<staged.Length;npc++)if(staged[npc]!=null){actors[npc+1]=staged[npc];instanceBytes+=staged[npc]!.NativeBytes;}
        if(player!=null)actors[0]=player;
        modelResidency.RecordWarmPeak(species,instanceBytes);startupAdmission.Complete();
        hasIdleClip[species]=model.TryGetAnimation("Idle",out _);
        for(int npc=0;npc<staged.Length;npc++)if(staged[npc]!=null){
            attention.Register(npc,staged[npc]!);
            if(model.TryGetAnimation("Walking",out _))animationClock.Step(npc+1,0,false);
        }
        if(player!=null){
            moveClip=-1;animationClock.Reset(0);appliedClips[0]=-2;
            DebugOverlay.WriteLine("ANIMALTOWN PLAYER MODEL READY "+species+" "+modelNames[species]);
        }
        return true;
    }
    private bool PrepareStartupResidency()
    {
        long now=Clock.Timestamp;
        return modelResidency.PrepareStartup(state,villagers.Doors,loadingIndex,now,now+1000,
            uvDebug!=0||requestedUv>=0);
    }
    private bool PrepareStartupTerrain()
    {
        // Bake even for an indoor save: its first exit must reuse the completed
        // archive rather than overlap scratch geometry with resident villagers.
        UpdateWeather();terrain.RoughOcean=weather.RoughOcean(0);terrain.SeasonOverride=renderDebug.SeasonOverride;
        terrain.GroundMipmaps=renderDebug.GroundMipmaps;terrain.GroundMipDistance=renderDebug.GroundMipDistance;terrain.ShowGroundMipBands=renderDebug.ShowGroundMipBands;
        // Indoor actors have height zero; this selects the outdoor town cache.
        RenderView.Prepare(state.X,state.Z,TerrainElevation.At(state.Town,state.X,state.Z));
        terrain.Prepare(state,room,month,villagerShadows,Clock.Timestamp+2000,cliffPreview);
        return terrain.Ready;
    }
    private void DrawLoading(bool landscape=false)
    {
#if ANIMALTOWN_ONLINE_PLAY
        if(reviewUi!=null){reviewUi.DrawReviewLoading();return;}
#endif
        if(loadingFailed)LoadingScreen.Draw("Could not load town assets.\n"+(session.Draft!=null||session.Flight.Active?"B: return to the title.":"Restart with a valid disc."),font);
        else LoadingScreen.Draw();
    }
    // Two opportunities per presented frame overlap DMA with drawing.
    // Executable work shares one millisecond after demand/sampling. All actual
    // preparation, including that bookkeeping, is charged to preparation telemetry.
    internal void AdvanceResidency(bool afterDraw=false)
    {
        if(!resourcesReady)return;
        long at=Clock.Timestamp;
#if ANIMALTOWN_ONLINE_PLAY
        if(!afterDraw)PrepareOnlineActors(at,false);
#endif
        if(!afterDraw)RefreshResidencyDemand(at);
        visualMemory.RefreshDemand(state,villagers.Doors,buildings,at);
        if(terrain.ReclaimWaterOptics())visualMemory.Sample(Clock.Timestamp,true);
        long deadline=residencyBudget.Begin(afterDraw);
        var crossing=villagers.Doors.Actors[0];
        bool exiting=villagers.Doors.Busy(0)&&crossing.Destination.Space.IsTown;
        snowExitReady=false;
        visualMemory.Prepare(state,villagers.Doors,room==0||exiting||outdoorModelLookahead,
            trees,droppedItems,buildings,at,deadline);
        if(visualMemory.Pressure.UnderPressure){
            if(!trimmedForPressure){uiShapes.Trim();if(screen!="map"&&!(room==0&&runningMinimap.Opacity>0))mapBackground.Dispose();trimmedForPressure=true;visualMemory.Sample(Clock.Timestamp,true);}
        }else trimmedForPressure=false;
        if(room==0||exiting||outdoorModelLookahead){
            float x=state.X,z=state.Z;
            if(room!=0){if(exiting){x=crossing.Destination.X;z=crossing.Destination.Z;}else BuildingWorld.Entrance(state,BuildingWorld.Building(room),out x,out z);}
            if((room==0||exiting)&&waterInspection!=0){
                float savedX=state.X,savedZ=state.Z;
                try{state.X=x;state.Z=z;WaterInspection.Focus(state,waterInspection);x=state.X;z=state.Z;}
                finally{state.X=savedX;state.Z=savedZ;}
            }
            int allowance=Math.Min(500,residencyBudget.Remaining);
            terrainExitReady=terrain.PrepareOutdoorResidency(x,z,Clock.Timestamp+allowance,room!=0);
            if(room!=0&&exiting&&terrainExitReady&&state.Town.Traffic.Winter&&!renderDebug.ShowGroundMipBands)
                snowExitReady=terrain.PrepareSnowForExit(state.Town,x,z);
        }else if(!villagers.Doors.Busy(0)&&at-spaceChangedAt>=2000000){
            terrainExitReady=terrain.SuspendOutdoorCache()&&!terrain.CacheSuspended;
        }
        if(afterDraw&&room!=0&&state.Town.Traffic.Winter&&!villagers.Doors.Busy(0))terrain.ReleaseSnowMask(state.Town);
        bool allowDemotion=session.Draft==null&&(terrain.ArchiveReady||visualMemory.Pressure.UnderPressure)&&(room==0||at-playerStillSince>=ModelResidency.IdleDelayUs)&&
            !villagers.Doors.Busy(0)&&requestedUv<0&&uvDebug==0;
        modelResidency.Pump(at,allowDemotion,deadline);
        modelLod.Pump(Clock.Timestamp,deadline);
        PumpVillagerVisuals();
        int changedActors=modelLod.ConsumeChangedActors();
#if ANIMALTOWN_ONLINE_PLAY
        PrepareOnlineActors(Clock.Timestamp,true);
#endif
        for(int actor=1;actor<actors.Length;actor++)if((changedActors&(1<<actor))!=0){restartClips[actor]=true;appliedClips[actor]=-2;if(actors[actor]!=null)attention.Register(actor-1,actors[actor]!);}
        long optionalAt=Clock.Timestamp;
        visualMemory.PrepareOptionalBuildings(state,villagers.Doors,buildings,optionalAt,deadline,
            !terrainExitReady||!modelResidency.ReadyForSpace(state,villagers.Doors,room)||!modelLod.FullForSpace(state,villagers.Doors,room));
        residencyBudget.End();
        if(modelResidency.Failures!=residencyFailures){residencyFailures=modelResidency.Failures;TextureCache.TrimUnused();}
        if(requestedUv>=0&&modelResidency.AllReady&&modelLod.FullReady){int next=requestedUv;requestedUv=-1;ApplyUv(next);}
        modelSceneReady=room==0||modelResidency.ReadyForSpace(state,villagers.Doors,room)&&buildings.ReadyForSpace(state,room)&&modelLod.FullForSpace(state,villagers.Doors,room);
        UpdatePortalReadiness();
        FrameProfile.Add(2,Clock.Timestamp-at);
    }
    private void RefreshResidencyDemand(long now)
    {
        if(state.X!=residencyX||state.Z!=residencyZ||room!=residencySpace){
            playerStillSince=now;residencyX=state.X;residencyZ=state.Z;
            if(room!=residencySpace){spaceChangedAt=now;residencySpace=room;}
        }
        // Only approaching the exit creates outdoor demand while indoors.
        bool nearExit=false;
        if(room!=0){BuildingWorld.InsideDoor(state,room,out float x,out float z);float dx=state.X-x,dz=state.Z-z;nearExit=dx*dx+dz*dz<1.1025f;}
        outdoorModelLookahead=room!=0&&nearExit;
        modelResidency.RequireForView(state,villagers.Doors,now,waterInspection,uvDebug!=0||requestedUv>=0,outdoorModelLookahead?0:-1);
        modelLod.Paused=uvDebug!=0;
        modelLod.Require(state,actorVisibility,uvDebug!=0||requestedUv>=0,villagers.Doors);
        modelSceneReady=room==0||modelResidency.ReadyForSpace(state,villagers.Doors,room)&&buildings.ReadyForSpace(state,room)&&modelLod.FullForSpace(state,villagers.Doors,room);
        UpdatePortalReadiness();
    }
    private void UpdatePortalReadiness()
    {
        var doors=villagers.Doors;
        for(int actor=0;actor<BuildingWorld.Actors;actor++){
            if(!doors.Busy(actor)||actor>0&&!state.NpcPresent(actor-1)){doors.ResourcesReady[actor]=true;continue;}
            var crossing=doors.Actors[actor];
            if(actor==0)doors.ResourcesReady[actor]=modelResidency.ReadyForSpace(state,doors,crossing.Destination.Space.Value)&&
                modelResidency.ReadyForSpace(state,doors,room)&&buildings.ReadyForSpace(state,crossing.Destination.Space.Value)&&
                buildings.ReadyForSpace(state,room)&&modelLod.FullForSpace(state,doors,crossing.Destination.Space.Value)&&
                modelLod.FullForSpace(state,doors,room)&&(!crossing.Destination.Space.IsTown||terrainExitReady&&(room==0||!state.Town.Traffic.Winter||renderDebug.ShowGroundMipBands||snowExitReady));
            else{int species=state.NpcSpecies(actor-1);doors.ResourcesReady[actor]=!modelResidency.Required(species)||modelResidency.Ready(species);}
        }
    }
    private void ApplyUv(int next)
    {
        if(NativeRenderProbe.Uv(next)==next)uvDebug=next;
        else Say("UV display","Not enough memory to prepare UV regions. The current view is unchanged.");
    }
    // Gameplay saves require this explicit menu action. The profile picker's
    // separately confirmed Delete action also commits, via TownSession.
    private bool SaveFromMenu(bool quit=false,bool travelCheckpoint=false)
    {
        if(!SaveCards.Enabled){saveStatus="Saving is disabled for this session. Restart to choose a card.";return false;}
        if(damagedSave){saveStatus="Damaged saves preserved. Use another card.";return false;}
        long started=Clock.Timestamp, mountAt=started, saveAt=started;
        string previousScreen=screen;screen="saving";Draw();screen=previousScreen;
        clockHud.Trim();uiShapes.Trim();
        bool terrainSuspended=false,saved=false;string phase="terrain cache";
        try
        {
            terrainSuspended=terrain.SuspendForSave();
            phase="snow mask";
            if(!terrain.ReleaseSnowMask(state.Town))throw new InvalidOperationException("Snow mask release failed");
            phase="mount";mountAt=Clock.Timestamp;
            if(card==null)card=SaveCards.Mount();
            state.Facing=facingYaw;
            phase="save set";
            saveAt=Clock.Timestamp;
            var storage=new CardSaveStorage(card,banner,icon,SaveCards.Device,state,preserveVillagerLayout:travelCheckpoint);
            var reads=new SaveReadCache(storage);
            var repository=new SaveRepository(reads);
            bool movedVillagers=false;
            if(travelCheckpoint){
                phase="travel checkpoint";
                CardSaveStorage.SaveTravelCheckpoint(repository,state);
            }else{
                repository.Save(state);
                phase="villager card layout";
                movedVillagers=storage.UpdateVillagerFiles(state);
                storage.MigrateSettings();
            }
            saved=true;
            saveStatus=repository.LastWrittenRecords==0&&!movedVillagers?"No changes to save.":"Saved / "+BuildInfo.Version;
            DebugOverlay.WriteLine("SAVE: "+repository.LastWrittenRecords+" records / "+repository.LastWrittenBytes+" bytes");
            DebugOverlay.WriteLine("SAVE I/O: "+((Clock.Timestamp-saveAt)/1000)+" ms / "+reads.PhysicalReads+" reads / "+reads.CacheHits+" cached reads");
        }
        catch(Exception error){
            // Built-in native exceptions may have no managed message. The
            // recovery screen must remain usable even after an allocation fails.
            string detail=error.Message??"Native save error (no message)";if(detail.Length>64)detail=detail.Substring(0,61)+"...";
            saveStatus=(travelCheckpoint?"Trip save failed: ":"Save failed: ")+detail;
            DebugOverlay.WriteLine("SAVE ERROR: "+phase+" (code "+NativeCalendar.CardError()+"): "+detail);
        }
        finally{
            long closeAt=Clock.Timestamp;
            try{card?.Dispose();}catch(Exception error){
                saved=false;saveStatus="Could not close the memory card. Try saving again.";
                DebugOverlay.WriteLine("CARD CLOSE: "+(error.Message??"Native close error"));
            }
            card=null;
            long resumeAt=Clock.Timestamp;
            ExitToTitle=quit&&saved;
            if(terrainSuspended&&!ExitToTitle)terrain.ResumeAfterSave();
            if(saved){
                long finished=Clock.Timestamp;
                DebugOverlay.WriteLine("ANIMALTOWN SAVE TOTAL: "+((finished-started)/1000)+" ms / prepare "+((mountAt-started)/1000)
                    +" / mount "+((saveAt-mountAt)/1000)+" / save "+((closeAt-saveAt)/1000)
                    +" / close "+((resumeAt-closeAt)/1000)+" / restore "+((finished-resumeAt)/1000)+" / quit "+(quit?1:0));
            }
        }
        return saved;
    }
    private void Say(string name,string text){ClearChatPresentation();if(!jobWritingDialogue)ResetJobConversation();dialogueVoice.Begin();voiceTick=0;returnToMenu=screen=="menu"&&(menuTab==SettingsTab||menuTab==TestingTab||godMode&&menuTab==2);if(returnToMenu){returnMenuTab=menuTab;returnMenuSelection=selected;}speaker=name;message=text;wrappedDialogue=dialogueText.Get(text);typeAt=0;screen="dialogue";}
    private bool Press(ControllerState p)=>p.A.WasPressed;
    internal void Input(ControllerState p)
    {
#if ANIMALTOWN_ONLINE_PLAY
        if(session.Online!=null){OnlineInput(p);return;}
#endif
        if(session.Flight.Active){if(loadingFailed&&p.B.WasPressed){session.Flight.Cancel();ExitToTitle=true;}return;}
        if(!resourcesReady||!modelSceneReady||room==0&&!terrain.Ready){if(loadingFailed&&session.Draft!=null&&p.B.WasPressed)ExitToTitle=true;return;}
        if(DebugMenuSettings.Read(p)){
            if(!DebugMenuSettings.Visible){
                if(screen==VillagerVisualScreen){screen="menu";menuTab=SettingsTab;selected=0;}
                ClearChatAttention();
                if(menuTab==TestingTab){menuTab=SettingsTab;selected=0;}
                if(returnToMenu&&returnMenuTab==TestingTab){returnMenuTab=SettingsTab;returnMenuSelection=0;}
                if(menuTab==SettingsTab)selected=Math.Min(selected,DaylightSettingsRow);
            }
            saveStatus=UserOptionsStorage.Status;return;
        }
        if(session.Draft!=null){OnboardingInput(p);return;}
        if(screen==VillagerVisualScreen){VillagerVisualInput(p);return;}
        bool chord=p.L.IsHeld&&p.R.IsHeld;
        if(chord&&p.Y.WasPressed){TraceJobView();return;}
        if(screen==ChatEntry.Screen){ChatInput(p);return;}
        if(TravelInput(p)||MailUiInput(p)||JobInput(p))return;
        if(AaStudy.Input(p))return;
        if(chord&&p.Z.IsHeld&&p.DPadDown.WasPressed){terrain.FreezeMotion=!terrain.FreezeMotion;return;}
        if(chord&&p.Z.IsHeld&&p.X.WasPressed){terrain.RecreateMeshes=!terrain.RecreateMeshes;return;}
        // These scene shortcuts are used by the automated emulator captures.
        if(chord&&p.A.WasPressed&&p.CStick.Y<-.8f&&screen=="world"){InspectShop();return;}
        if(chord&&p.A.WasPressed&&screen=="world"){InspectBuilding();return;}
        if(chord&&p.CStick.Y>.8f&&p.Z.WasPressed&&room==0){terrain.ToggleGroundRing();return;}
        if(chord&&p.B.WasPressed){RuntimeOptions.ToggleFramePacing();return;}
        if(chord&&p.DPadUp.WasPressed&&room==0){cliffPreview=!cliffPreview;return;}
        if(chord&&p.DPadLeft.WasPressed&&room==0&&screen=="world"){waterInspection=(waterInspection+1)%6;return;}
        if(waterInspection!=0&&screen=="world"&&!p.Start.WasPressed){if(p.B.WasPressed)waterInspection=0;return;}
        if(chord&&p.Start.WasPressed){InspectTerrain();return;}
        bool navigating=screen==DaylightLocationMenu.Screen||screen=="save-quit"||screen=="menu"||screen=="item"||screen=="sell"||screen=="shop"||screen=="store"||screen=="post"||screen=="mail"||screen=="lost"||ShopScreen;
        int direction=menuNavigation.Read(screen,screen=="menu"?menuTab:-1,p.Stick.X,p.Stick.Y,(int)p.PressedButtons,Clock.Timestamp,navigating&&!chord);
        bool up=direction==MenuNavigation.Up,down=direction==MenuNavigation.Down,left=direction==MenuNavigation.Left,right=direction==MenuNavigation.Right;
        bool seasonFocused=TestingFocused(SeasonPreviewOption),timeFocused=TestingFocused(TimePreviewOption);
        if(!seasonFocused)renderDebug.CancelSeasonSelection();
        if(!timeFocused)renderDebug.CancelTimeSelection();
        if(!chord){
            if(seasonFocused&&(left||right)){renderDebug.AdjustSeason(right?1:-1);return;}
            if(timeFocused){
                if(left||right){renderDebug.AdjustTime(right?1:-1,townClock.MinuteOfDay);return;}
                if(p.X.WasPressed){renderDebug.SelectLiveTime();return;}
            }
        }
        if(screen=="menu"&&menuTab==TestingTab&&TestingOption(selected)==23&&!chord){
            if(left||right){renderDebug.AdjustGroundMipDistance(right?1:-1);return;}
            if(p.X.WasPressed){renderDebug.ResetGroundMipDistance();return;}
        }
        if(p.L.IsHeld&&p.R.IsHeld&&p.Z.WasPressed){FrameProfile.Enabled=!FrameProfile.OverlayVisible;debugConfig.SelectProfilingMode(FrameProfile.Mode);return;}
        if(p.L.IsHeld&&p.R.IsHeld)return;
        if(screen==DaylightLocationMenu.Screen){
            if(p.B.WasPressed||p.Start.WasPressed){screen="menu";return;}
            daylightMenu.Move(direction);
            if(Press(p)){
                state.Town.DaylightLocation=daylightMenu.Location;
                UpdateDaylight();
                screen="menu";
            }
            return;
        }
        if(screen=="save-quit"){
            if(p.B.WasPressed||p.Start.WasPressed){screen="menu";selected=1;return;}
            if(up||down)selected=1-selected;
            if(Press(p)){
                bool quit=selected==1;
                screen="menu";selected=1;
                if(quit)SaveFromMenu(quit:true);
            }
            return;
        }
        if(ShopInput(p,direction))return;
        if(screen=="dialogue")
        { if(Press(p)||p.B.WasPressed){dialogueVoice.Stop();if(typeAt<message.Length)typeAt=message.Length;else if(!NextChatPage()){ClearChatPresentation();screen=returnToMenu?"menu":"world";if(returnToMenu){menuTab=returnMenuTab;selected=returnMenuSelection;}}}return; }
        if(screen=="plot")
        {
            if(Press(p)&&CanChoosePlot(out _)&&state.PlaceHouse(PlotX,PlotZ,relocate,godMode)){
                bool moved=relocate;relocate=false;
                Say("Hearth",moved?"Your home has moved, along with your garden furniture.":"Your home is ready! Visit the store for your first job.");
            }
            if(p.B.WasPressed&&state.HousePlaced){relocate=false;screen="world";}return;
        }
        if(screen=="world"&&villagers.Doors.Busy(0))return;
        if(screen=="world"&&!chord&&p.R.WasPressed){OpenChat();return;}
        if(p.Start.WasPressed){screen="menu";menuTab=SettingsTab;selected=0;return;}
        if(p.Y.WasPressed){screen=screen=="map"?"world":"map";return;}
        if(p.B.WasPressed&&screen=="item"){screen="menu";menuTab=0;selected=slot;return;}
        if(p.B.WasPressed&&screen!="world"){screen="world";return;}
        if(screen=="lost"&&p.X.WasPressed){
            int index=LostIndex(selected),id=state.Town.LostAndFound.RecoveryIdAt(index);
            bool restored=KobanSite.InReach(state)&&TownItemActions.RestoreRecovery(state,state,TownItemActor.ForActivePlayer(state),id);
            Say("Koban",state.Visiting?"Lost property stays with this town's residents.":restored?"The visitor's removal has been undone.":id==0?"This item has no visitor removal to undo.":"The original spot is occupied. The item is still safe here.");return;
        }
        if(p.X.WasPressed){screen=screen=="menu"?"world":"menu";selected=0;menuTab=0;return;}
        if(screen=="map")return;
        if(screen=="world")
        {
            if(p.Z.WasPressed){for(int i=0;i<state.Capacity;i++)if(Catalog.Has(state.Pockets[i],ItemFlags.Tool)){state.Use(i,4,0,0);break;}}
            if(Press(p))Interact();return;
        }
        if(screen=="item")
        {
            int item=state.Pockets[slot],actionCount=PocketMenu.Count(state,slot,room);
            selected=Math.Max(0,Math.Min(selected,actionCount-1));
            if(down)selected=(selected+1)%actionCount;if(up)selected=(selected+actionCount-1)%actionCount;
            if(p.R.WasPressed&&PocketMenu.HasAction(state,slot,room,PocketAction.Place))rotation=(rotation+1)%4;
            if(Press(p))
            {
                PocketAction action=PocketMenu.At(state,slot,room,selected);
                if(action==PocketAction.WriteLetter||action==PocketAction.AttachToLetter){OpenPocketLetter(action);return;}
                if(action==PocketAction.Read){Say("Letter","Thank you for being a part of our town!");return;}
                if(action==PocketAction.Quit){screen="menu";menuTab=0;selected=slot;return;}
                int tx=(int)state.X+faceX,tz=(int)state.Z+faceZ;
                bool ok=PocketMenu.Perform(state,slot,action,tx,tz,rotation,room,out string failureReason);
                if(ok&&action==PocketAction.Wear&&state.Tutorial==0&&state.Shirt==Catalog.WorkShirt)state.CreditTutorial(0);
                if(ok&&action==PocketAction.Plant&&state.Tutorial==1)state.CreditTutorial(1);
                if(action==PocketAction.OpenMoneyBag){int amount=Catalog.MoneyValue(item);Say("Wallet",ok?UiFont.Money(amount)+" added. Wallet: "+UiFont.Money(state.Wallet)+".":"No room for "+UiFont.Money(amount)+". Wallet limit: "+UiFont.Money(GameState.WalletLimit)+".");}
                else Say("Backpack",ok?"Done!":failureReason.Length!=0?failureReason:"That action is not possible here.");
            }
            return;
        }
        bool backpack=screen=="sell"||(screen=="menu"&&menuTab==0);
        if(backpack)
        {
            if(down)selected=(selected+5)%state.Capacity;if(up)selected=(selected+state.Capacity-5)%state.Capacity;
            if(right)selected=(selected+1)%state.Capacity;if(left)selected=(selected+state.Capacity-1)%state.Capacity;
            if(screen=="menu"&&p.R.WasPressed){menuTab=1;selected=0;return;}if(screen=="menu"&&p.L.WasPressed){menuTab=MenuTabCount-1;selected=0;return;}
            if(Press(p)&&state.Pockets[selected]!=0){if(screen=="sell"){if(CanSellFromPockets(selected))Say("Hearth",state.Sell(selected)?"Thank you! Acorns added to your wallet.":"Your wallet is full.");}else{slot=selected;screen="item";selected=0;}}return;
        }
        int count=screen=="shop"?TownShop.CabinetCount:screen=="post"?7:screen=="store"?StoreMenuCount:screen=="mail"?Math.Max(1,state.MailCount):screen=="lost"?Math.Max(1,LostCount()):TabCount();
        if(down)selected=(selected+1)%count;if(up)selected=(selected+count-1)%count;
        if(screen=="menu"&&(p.R.WasPressed||right)){menuTab=(menuTab+1)%MenuTabCount;selected=0;}
        if(screen=="menu"&&(p.L.WasPressed||left)){menuTab=(menuTab+MenuTabCount-1)%MenuTabCount;selected=0;}
        if(!Press(p))return;
        if(screen=="shop"){Say("Hearth",state.BuyCabinet(selected,godMode)?"Thank you! Cabinet essentials are always in stock.":"You need enough acorns and a free pocket.");return;}
        if(screen=="store"){StoreAction();return;}
        if(screen=="post"){PostAction();return;}
        if(screen=="mail")
        { if(selected<state.MailCount&&state.Mail[selected]>0&&state.Receive(state.Mail[selected])){if(JobsLive)state.ResidentJobs.RemoveMailboxSlot(selected);else{for(int i=selected;i<state.MailCount-1;i++)state.Mail[i]=state.Mail[i+1];state.MailCount--;}Say("Mailbox","Delivery collected.");}return; }
        if(screen=="lost")
        { int index=LostIndex(selected);Say("Koban",state.Visiting?"Only a villager who lives in this town may collect lost property.":KobanSite.InReach(state)&&state.ClaimLost(index,state.ActiveProfile)?"Recovered!":"Make room in your pockets and speak to the koban counter.");return; }
        if(screen=="menu")MenuAction();
    }
    private bool FoliageScene=>room==0&&session.Draft==null&&waterInspection==0&&!cliffPreview;
    private float FoliageSeconds()=>(Clock.Timestamp-sessionStart)*.000001f;
    partial void RecordFallFootstep(float beforeX,float beforeZ)
    {
        if(screen=="world"&&FoliageScene&&terrain.SeasonAt(month,renderDebug.SeasonOverride)==2)
            foliage.TrackStep(state.Town,beforeX,beforeZ,state.X,state.Z,FoliageSeconds());
    }
    private void PrepareFoliage(int tint)
    {
        int season=terrain.RenderSeason;
        bool bloom=season==0&&townClock.CherryBlossomsBloom;
        if(!FoliageScene||(!bloom&&season!=2)){foliage.Dispose();return;}
        if(villagers.Doors.Busy(0)){foliage.Clear();return;}
        float seconds=FoliageSeconds();
        if(bloom)foliage.TrackPetals(state.Town,state.X,state.Z,cachedPlayerHeight,seconds);
        else foliage.SetPetalMode(false);
        foliage.Prepare(state.Z,seconds,tint);
    }
    private int MenuTabCount=>DebugMenuSettings.Visible?tabs.Length:tabs.Length-1;
    private int TabCount()=>menuTab==0?state.Capacity:menuTab==1?(JobsLive?1:7):menuTab==2?Catalog.EntryCount:menuTab==3?7:menuTab==4?7:menuTab==TestingTab?testingOptions.Length:DaylightSettingsRow+(DebugMenuSettings.Visible?2:1);
    private int LostCount()=>state.Town.LostAndFound.Count;
    private int LostIndex(int n)=>state.Town.LostAndFound.IndexAt(n);
    private void StoreAction()
    {
        if(selected==8&&StoredShopCount>0){screen="shop-stored";selected=0;return;}
        if(selected==7){Say("Hearth",state.UpgradeBackpack(godMode)?"A bigger backpack! You can carry "+state.Capacity+" items now.":state.BackpackTier==2?"Your backpack already holds 30 items.":"The next backpack costs "+UiFont.Money(state.BackpackUpgradePrice)+".");return;}
        if(selected==0){screen="shop";selected=0;return;}
        if(selected==1&&JobsLive&&state.Tutorial==3){Say("Hearth",state.ResidentJobs.OpeningReturn()?"Good work! Use this paper to write a letter at town hall.":"Collect the parcel from "+state.Town.Residents[4].Name+" first.");return;}
        if(selected==1){int i=state.Find(18);if(state.Tutorial==3&&i>=0){state.Pockets[i]=0;state.CreditTutorial(3);state.Receive(Catalog.WritingPaper);Say("Hearth","Good work! Use this paper to write a letter at town hall.");return;}screen="sell";selected=0;return;}
        if(selected==2){Say("Hearth",TutorialText());return;}
        if(selected==3){Say("Hearth",state.Expand(false,day,godMode)?"Your house expansion arrives after 06:00.":godMode?"No expansion is available yet.":"Pay off your debt before expanding.");return;}
        if(selected==4){Say("Hearth",state.Expand(true,day,out string reason,godMode)?"Your garden expands after 06:00.":reason);return;}
        if(selected==5){Say("Hearth",RepayLoan()?"Thank you for your repayment!":"No repayment is possible.");return;}
        if(state.Tutorial<7){Say("Hearth",TutorialText());return;}
        if(state.Jobs>=2){Say("Hearth","All jobs are done for today.");return;}
        int fruit=state.Find(1);if(fruit>=0&&state.Wallet<=99199){state.Pockets[fruit]=0;state.Wallet+=800;state.Jobs++;Say("Hearth","A peach for our customer! Here is "+UiFont.Money(800)+".");}else Say("Hearth","Today's job: bring a peach for a customer. Reward: "+UiFont.Money(800)+". Make space in your wallet.");
    }
    private string TutorialText()
    { string[] text={"Welcome! Wear the work shirt in your pockets.","Plant your flower seed near the store.","Deliver the parcel to a villager.","Collect "+state.Town.Residents[4].Name+"'s parcel and return it to this counter.","Write a letter at town hall.","Introduce yourself to your neighbours.","Post a message on the notice board near the town gate.","Your part-time job is complete. Your debt is "+UiFont.Money(18000)+"."};return text[state.Tutorial]; }
    private void PostAction()
    {
        if(selected==0){Say("Fern",state.Deposit(Math.Min(1000,state.Wallet))?"Deposit complete.":"No acorns to deposit.");return;}
        if(selected==1){Say("Fern",state.Withdraw((int)Math.Min(1000,state.Savings))?"Withdrawal complete.":"No room in wallet, or insufficient savings.");return;}
        if(selected==2){Say("Fern",RepayLoan()?"Loan repayment complete.":"No payment is possible.");return;}
        if(selected==3){Say("Fern","Lost property is kept at the koban. Visit its counter or shelves to collect it.");return;}
        if(selected==4){screen="mail";selected=0;return;}
        if(selected==5&&JobsLive){OpenLetterComposer();return;}
        if(selected==5){if(state.Tutorial==4){state.CreditTutorial(4);if(state.MailCount<128)state.Mail[state.MailCount++]=-19;}Say("Fern","Your letter has been sent. A reply will arrive tomorrow.");return;}
        if(JobsLive){OpenTravel();return;}
        Say("Gate","Complete your town introduction to use the Gate.");
    }
    private void MenuAction()
    {
        if(menuTab==0){slot=selected;screen="item";selected=0;return;}
        if(menuTab==1){if(JobsLive){jobNpc=-1;OpenJobLog();return;}Say("Jobs",selected==0?TutorialText():state.Town.Residents[selected-1].Name+": "+(state.Town.Residents[selected-1].Request==0?"No request":"Bring "+Catalog.Items[state.Town.Residents[selected-1].Request].Name));return;}
        if(menuTab==2&&state.Visiting){Say("Catalog","Place catalogue orders when you return home.");return;}
        if(menuTab==2){Say("Catalog",godMode?state.Receive(Catalog.EntryAt(selected))?"Added to your pockets for free.":"Your pockets are full.":state.Order(Catalog.EntryAt(selected))?"Ordered! Check your mailbox after the next rollover.":"Only previously owned store items can be ordered.");return;}
        if(menuTab==3&&state.Visiting){Say("Home","Your home is waiting in your own town.");return;}
        if(menuTab==3)
        {
            if(selected==0){state.Roof=(state.Roof+1)%12;Say("Home","Roof palette selected.");}
            if(selected==1){state.Exterior=(state.Exterior+1)%5;Say("Home","Exterior palette selected.");}
            if(selected==2){state.Fence=(state.Fence+1)%6;Say("Home","Fence style selected.");}
            if(selected==3){state.Mailbox=(state.Mailbox+1)%6;Say("Home","Mailbox selected.");}
            if(selected==4){if(room!=0){Say("Home","Step outside before relocating your home.");return;}relocate=true;screen="plot";}
            if(selected==5){Say("Home",state.Expand(false,day,godMode)?"Construction ordered.":godMode?"No expansion is available yet.":"Pay off your loan first.");}
            if(selected==6){Say("Home",state.Expand(true,day,out string reason,godMode)?"Garden construction ordered.":reason);}return;
        }
        if(menuTab==4){PostAction();return;}
        if(menuTab==TestingTab){TestingAction();return;}
        if(menuTab!=SettingsTab)return;
        if(selected==DaylightSettingsRow+1&&DebugMenuSettings.Visible){menuTab=TestingTab;selected=0;return;}
        if(selected==DaylightSettingsRow){daylightMenu.Open(state.Town.DaylightLocation);screen=DaylightLocationMenu.Screen;return;}
        if(selected==0){SaveFromMenu();}
        else if(selected==1){screen="save-quit";selected=0;}
        else if(selected==2){Say("Controls","Stick / D-pad: walk / menus. L/R: tabs. R: chat while playing. B: run / back. A: interact / select. X: backpack. Y: map. Z: tool. START: settings. Choose Save Now to save.");}
        else if(selected==3)Say("About","AnimalTown / "+BuildInfo.Version+" / hypercube-engine. Original homebrew for Nintendo GameCube.");
        else if(selected==4){Say("Calendar","Console RTC / day "+day+". New days begin at 06:00.");}
        else OpenChat();
    }
    private bool RepayLoan()=>state.Repay((int)Math.Min(state.Debt,godMode?int.MaxValue:state.Wallet),godMode);
    private void TestingAction()
    {
        clockHud.Trim();uiShapes.Trim();
        switch(TestingOption(selected)){
            case 31:godMode=!godMode;break;
            case 32:
                int added=state.GiveMoney(10000,godMode);
                Say("God mode",!godMode?"Enable God mode to give yourself acorns.":added==0?"Your wallet is full.":"Added "+UiFont.Money(added)+". Wallet: "+UiFont.Money(state.Wallet)+".");break;
            case 0:FrameProfile.Enabled=!FrameProfile.OverlayVisible;debugConfig.SelectProfilingMode(FrameProfile.Mode);break;
            case ProfilingModeOption:FrameProfile.SetMode((FrameProfile.Mode+1)%4);debugConfig.SelectProfilingMode(FrameProfile.Mode);if(FrameProfile.Status.StartsWith("Capture refused"))Say("Profiling",FrameProfile.Status);break;
            case ExportProfileOption:FrameProfile.DumpCapture();debugConfig.SelectProfilingMode(FrameProfile.Mode);break;
            case VillagerVisualOption:OpenVillagerVisuals();break;
            case 2:case 3:
                int next=uvDebug^(TestingOption(selected)==2?1:2);
                if(modelResidency.AllReady&&modelLod.FullReady)ApplyUv(next);else requestedUv=next;
                break;
            case 11:RuntimeOptions.ToggleFramePacing();break;
            case 16:weather.RainEnabled=!weather.RainEnabled;break;
            case 35:weather.SnowEnabled=!weather.SnowEnabled;break;
            case 17:weather.RoughOceanWhenRaining=!weather.RoughOceanWhenRaining;break;
            case SeasonPreviewOption:renderDebug.ApplySeason();break;
            case TimePreviewOption:renderDebug.ApplyTime();UpdateDaylight();break;
            case 19:renderDebug.ForceAttention=!renderDebug.ForceAttention;break;
            case DevConfig.SdkPerformanceIndex:
                debugConfig.Adjust(DevConfig.SdkPerformanceIndex,1);
                DebugOverlay.PerformanceVisible=debugConfig.SdkPerformanceOverlay;
                break;
            case 22:renderDebug.GroundMipmaps=!renderDebug.GroundMipmaps;break;
            case 23:renderDebug.AdjustGroundMipDistance(1);break;
            case 34:renderDebug.ShowGroundMipBands=!renderDebug.ShowGroundMipBands;break;
            case 36:renderDebug.ShowWorldRim=!renderDebug.ShowWorldRim;break;
            case 37:renderDebug.ShowObstructedEdges=!renderDebug.ShowObstructedEdges;break;
            case 38:renderDebug.ShowVisitEdges=!renderDebug.ShowVisitEdges;break;
        }
    }
    private static string OnOff(bool value)=>value?"On":"Off";
    private bool TestingFocused(int option)=>screen=="menu"&&menuTab==TestingTab&&TestingOption(selected)==option;
    private static string PreviewSeasonName(int season)=>season<0?"Auto":TerrainTiles.SeasonName(season);
    private static string PreviewTimeName(int minute)=>minute<0?"Live":TownClock.FormatTime(minute);
    private string TestingLabel(int row)
    {
        switch(TestingOption(row)){
            case 31:return "God mode: "+OnOff(godMode)+" (session)";
            case 32:return "Give "+UiFont.Money(10000)+(godMode?"":" (God mode required)");
            case 0:return "Performance display: "+OnOff(FrameProfile.OverlayVisible);
            case ProfilingModeOption:return "Profiling: "+DevConfig.ProfilingLabel(FrameProfile.Mode)+" (session)";
            case ExportProfileOption:return "Export profiling capture to debug log";
            case VillagerVisualOption:return "Villager visuals (session)";
            case 2:return "UV island colors: "+OnOff((uvDebug&1)!=0);
            case 3:return "UV island outlines: "+OnOff((uvDebug&2)!=0);
            case 11:return "Frame pacing: "+((RuntimeOptions.Mode&1)!=0?"Uncapped":"Yield");
            case 16:return "Rain preview: "+OnOff(weather.RainEnabled);
            case 35:return "Winter snowfall: "+OnOff(weather.SnowEnabled)+" (session)";
            case 17:return "Rough ocean in rain: "+OnOff(weather.RoughOceanWhenRaining);
            case SeasonPreviewOption:
                string season=PreviewSeasonName(renderDebug.SeasonOverride);
                return "Season preview: "+(row==selected?"< "+PreviewSeasonName(renderDebug.SeasonSelection)+" >   Active: "+season:season);
            case TimePreviewOption:
                string time=PreviewTimeName(renderDebug.TimeOverride);
                return "Time preview: "+(row==selected?"< "+PreviewTimeName(renderDebug.TimeSelection)+" >   Active: "+time:time);
            case 19:return "Villager attention bubbles: "+OnOff(renderDebug.ForceAttention);
            case DevConfig.SdkPerformanceIndex:return "SDK performance overlay: "+OnOff(DebugOverlay.PerformanceVisible)+" (session)";
            case 22:return "Ground mipmaps: "+OnOff(renderDebug.GroundMipmaps);
            case 23:return "Ground mip switch: "+RenderDebugOptions.GroundMipDistanceLabel(renderDebug.GroundMipDistance);
            case 34:return "Ground mip bands: "+OnOff(renderDebug.ShowGroundMipBands);
            case 36:return "World rim: "+OnOff(renderDebug.ShowWorldRim)+" (session)";
            case 37:return "Obstructed object edges: "+OnOff(renderDebug.ShowObstructedEdges);
            case 38:return "Tile visit edges: "+OnOff(renderDebug.ShowVisitEdges);
            default:throw new InvalidOperationException("Unknown debug option");
        }
    }
    private void Interact()
    {
        if(JobsLive&&room==0&&Math.Abs(state.X-43)<2&&Math.Abs(state.Z-13)<2){OpenNoticeBoard();return;}
        int x=(int)state.X,z=(int)state.Z,tx=x+faceX,tz=z+faceZ;
        if(villagers.Doors.Busy(0))return;
        int door=villagers.Doors.Nearby(state,0,facingYaw);
        if(door>=0){if(!buildings.Ready(state.BuildingTypes[door])){Say("Home","This home is unavailable right now.");return;}if(!villagers.Doors.Request(state,0,door))Say("Door","Please wait for the doorway to clear.");return;}
        if(room!=0){
            if(room==TownShop.Room&&OpenShopFixture())return;
            if(KobanSite.InReach(state)){screen="lost";selected=0;return;}
            if(TalkNearby())return;
            if(room==25){screen="post";selected=0;return;}
            // No nearby villager or usable doorway: A does nothing.
            return;
        }
        if(HelipadSite.AtGate(x,z)&&JobsLive){OpenTravel();return;}
        if(HelipadSite.AtGate(x,z)){Say("Gate","Complete your town introduction to visit another town.");return;}
        if(TalkNearby())return;
        if(JobsLive&&state.Tool==Catalog.Shovel)for(int i=0;i<AnimalTown.Jobs.OfflineJobLedger.WorldCapacity;i++){var o=state.Town.Jobs.WorldObject(i);float dx=o.X-state.X,dz=o.Z-state.Z;if(o.Kind==AnimalTown.Jobs.JobWorldKind.Snowman&&dx*dx+dz*dz<2.25f){state.ResidentJobs.DestroyWorldObject(i);Say("Snow","The snowman crumbles back into the snow.");return;}}
        if(state.Pickup(tx,tz)||state.Pickup(x,z)){Say(state.Visiting?"Koban":"Backpack",state.Visiting?"The item is safe in this town's lost and found.":"Picked up!");return;}
        int ground=state.Town.FindGround(tx,tz);
        if(ground>=0)
        {
            var g=state.Town.Ground[ground];
            if(g.Kind==3){
                if(state.Tool==Catalog.Axe){Say("Tree",TownItemActions.Chop(state,ground)?"The removed tree is kept at the koban for replanting.":"There is no room to store the tree safely.");}
                else if(TownItemActions.Harvest(state,ground))Say("Tree",state.Visiting?"The fruit is kept at the koban for this town's residents.":"Ripe fruit drops into your pockets!");
                return;
            }
            if(g.Kind==1&&state.Tool==Catalog.Shovel&&TownItemActions.Dig(state,ground)){Say("Digging",state.Visiting?"The find is kept at this town's koban.":"You found something!");return;}
            if(g.Kind==2&&state.Tool==Catalog.Shovel&&TownItemActions.RemoveGround(state,state,TownItemActor.ForActivePlayer(state),ground)){Say("Koban","The removed plant is safe in lost and found.");return;}
        }
        if(state.Tool==Catalog.FishingRod&&(WaterSurface.IsWater(state.Town,tx,tz)||WaterSurface.IsWater(state.Town,tx+1,tz)))
        { Say("Fishing",TownItemActions.Catch(state,14)?state.Visiting?"A river fish! It is kept at this town's koban.":"A river fish!":state.Visiting?"Lost and found is full.":"Your pockets are full.");return; }
        if(state.Tool==Catalog.BugNet){
            // Keep successful bug catches; misses are silent like all other
            // empty-space interactions.
            if(tick%3==0&&TownItemActions.Catch(state,15))Say("Bug net",state.Visiting?"A butterfly! It is kept at this town's koban.":"Caught a butterfly!");
            return;
        }
        if(state.Tool==Catalog.Shovel&&state.Town.AddGround(16,tx,tz,1,day)){Say("Digging","A spot for something buried.");return;}
        // Nothing in reach. Do not interrupt movement with an idle dialogue.
    }
    private void UpdateAnimationFrame()
    {
        long start=FrameProfile.Start();
        for(int i=0;i<actors.Length;i++){
            bool visible=actorVisibility.Visible[i];float seconds=animationClock.Consume(i,visible,actorVisibility.AnimationStride[i]);
            // Clip controls can reskin too. Defer every native animation call,
            // including Stop/Play/speed changes, until this actor can be drawn.
            if(!visible)continue;
            var instance=actors[i]!;
            int clip=animationClock.Clip(i);
            if(restartClips[i]||appliedClips[i]!=clip){
                restartClips[i]=false;
                string name=clip==IdleClip?"Idle":clip==1?"Running":"Walking";
                if(clip<0)instance.Animator.Stop();
                else if(instance.Model.TryGetAnimation(name,out _)){
                    float speed=i==0&&clip!=IdleClip?desiredPlayerSpeed:1;
                    instance.Animator.Play(name,true);instance.Animator.SetSpeed(speed);
                    if(i==0)appliedPlayerSpeed=speed;
                }
                appliedClips[i]=clip;
            }
            if(i==0&&clip>=0&&clip!=IdleClip&&appliedPlayerSpeed!=desiredPlayerSpeed){instance.Animator.SetSpeed(desiredPlayerSpeed);appliedPlayerSpeed=desiredPlayerSpeed;}
            if(seconds>0){instance.Update(seconds);FrameProfile.AnimationUpdates++;}
        }
        FrameProfile.End(1,start);
    }
    private Vector3 SurfacePoint(float x,float z,float height=0)
    { if(room!=0)return new Vector3(x,height,z);var p=CylinderWorld.Surface(z,state.Z,height+TerrainElevation.At(state.Town,x,z));return new Vector3(x,p.Y,p.Z); }
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
#if ANIMALTOWN_ONLINE_PLAY
        if(reviewUi!=null&&actor==0)return;
#endif
        if(actor==0&&session.Flight.PlayerOnboard)return;
        if(!actorVisibility.Visible[actor]){FrameProfile.CulledObjects++;return;}
        var location=actorVisibility.Locations[actor];float x=location.X,z=location.Z,scale=1.8f,facing=actor==0?facingYaw:location.Yaw,height=0;
        if(actor==0&&session.Flight.PlayerWalking)session.Flight.PlayerPose(state.X,state.Z,out x,out z,out height,out facing);
        var pose=batchedDrawing?modelPoses[modelUsed++]:transform;pose.LocalPosition=SurfacePoint(x,z,height);pose.LocalScale=new Vector3(scale,scale,scale);pose.LocalRotation=SurfaceRotation(z,facing);
        if(batchedDrawing){actorQueue.Submit(actors[actor]!,pose);}else{NativeRim.Enable(RenderDebugOptions.DefaultRimPercent);NativeShadows.Objects(room==0?1:0);try{renderer.DrawModel(actors[actor]!,pose);}finally{NativeShadows.Objects(0);NativeRim.Enable(0);}}
    }
    private void DrawActors()
    {NativeRim.Enable(RenderDebugOptions.DefaultRimPercent);NativeShadows.Objects(room==0?1:0);try{actorQueue.Execute(renderer);}finally{NativeShadows.Objects(0);NativeRim.Enable(0);}}
    private void PrepareVillagerShadows()
    {
        villagerShadows.BeginFrame(state.Town);
        NativeShadows.Begin(room,1,room==0?0:BuildingWorld.Size(state,room));
        int size=room==0?0:BuildingWorld.Size(state,room);
        bool mat=room!=0&&BuildingWorld.Definition(state,BuildingWorld.Building(room)).Floor==BuildingCatalog.Cottage.Floor;
        for(int actor=0;actor<BuildingWorld.Actors;actor++){
#if ANIMALTOWN_ONLINE_PLAY
            if(reviewUi!=null&&actor==0)continue;
#endif
            if(actor>0&&(!state.NpcPresent(actor-1)||!modelResidency.Ready(state.NpcSpecies(actor-1))))continue;
            if(BuildingWorld.Location(state,actor).Space.Value!=room||actor==0&&(waterInspection!=0||session.Flight.PlayerOnboard))continue;
            var p=villagers.Doors.VisualLocation(state,actor);
            float x=p.X,z=p.Z;
            if(actor==0&&session.Flight.PlayerWalking)session.Flight.PlayerPose(state.X,state.Z,out x,out z,out _,out _);
            if(Visible(x,z,1.8f))villagerShadows.Set(actor,room,x,z,size,mat);
        }
#if ANIMALTOWN_ONLINE_PLAY
        if(session.Online!=null)PrepareOnlineActorShadows(size,mat);
#endif
    }
    internal void Draw()
    {
        if(!resourcesReady||session.Flight.Phase==FlightPhase.Cruise){
            clockFade=default;
            DrawLoading();
            return;
        }
        if(screen=="world"||screen=="plot")clockFade.Update(Clock.Timestamp,!playerRunning);
        else clockFade=default;
        float x=state.X,z=state.Z;
        try{if(waterInspection!=0)WaterInspection.Focus(state,waterInspection);DrawScene();}
        finally{state.X=x;state.Z=z;}
    }
    private readonly FenceRenderer fences=new FenceRenderer();
    private void DrawScene()
    {
        NativeWaterProbe.Enable(room==0?waterInspection:0);
        // Input/debug camera changes may occur on a frame with no simulation
        // tick. Resolve final drawable residency after the effective view changes.
        // Water inspection uses this focused camera's normal demand; pinning
        // every species can consume the cache needed to restore archived terrain.
        long residencyAt=FrameProfile.Start();
        modelResidency.Require(state,villagers.Doors,Clock.Timestamp,uvDebug!=0||requestedUv>=0,outdoorModelLookahead?0:-1);
        modelSceneReady=room==0||modelResidency.ReadyForSpace(state,villagers.Doors,room)&&buildings.ReadyForSpace(state,room)&&modelLod.FullForSpace(state,villagers.Doors,room);
        FrameProfile.End(2,residencyAt);
        if(!modelSceneReady){modelResidency.WaitForScene();DrawLoading();return;}
        UpdateWeather();terrain.RoughOcean=weather.RoughOcean(room);terrain.SeasonOverride=renderDebug.SeasonOverride;
        terrain.GroundMipmaps=renderDebug.GroundMipmaps;terrain.GroundMipDistance=renderDebug.GroundMipDistance;terrain.ShowGroundMipBands=renderDebug.ShowGroundMipBands;
        cachedPlayerHeight=PlayerHeight();if(room==0)RenderView.Prepare(state.X,state.Z,cachedPlayerHeight);
        if(room!=0)indoorCamera.Configure(BuildingWorld.Definition(state,BuildingWorld.Building(room)));
        var lighting=daylight.Lighting;
        if(screen==DaylightLocationMenu.Screen)daylightMenu.Prepare(townClock);
        int sunlight=room==0?lighting.Sunlight:255;
        if(room==0)terrain.AnimateShadows((Clock.Timestamp-sessionStart)/1000000.0,month,sunlight);
        int environmentTint=room==0?lighting.Tint:0xffffff;
        int lampStrength=room==0?lighting.Lamps:0;
        townLights.Prepare(state,lampStrength);
        long phase=FrameProfile.Start(),preparationDeadline=Clock.Timestamp+2000;PrepareVillagerShadows();
        long masksAt=FrameProfile.Start();villagerShadows.PrepareMasks(preparationDeadline);FrameProfile.DetailEnd(4,masksAt);
        if(room==0){
            terrain.Prepare(state,room,month,villagerShadows,preparationDeadline,cliffPreview);
            terrain.Animate((Clock.Timestamp-sessionStart)/1000000.0);
        }
        else{terrain.ReleasePaths();int steps=0;while(steps<8&&Clock.Timestamp<preparationDeadline){if(!villagerShadows.Advance())break;steps++;}}
        FrameProfile.End(2,phase);
        if(room==0&&!terrain.Ready){DrawLoading(landscape:true);return;}
        NativeWorldDiagnostics.View((int)(state.X*1024),(int)(state.Z*1024),renderDebug.GroundMipmaps?renderDebug.GroundMipRadius:0,room==0&&renderDebug.ShowWorldRim?1:0);
        if(NativeWorldDiagnostics.PrepareEdges(renderDebug.ShowObstructedEdges?1:0)!=1){
            renderDebug.ShowObstructedEdges=false;
            DebugOverlay.WriteLine("Obstructed object edges require available graphics memory");
        }
        visitEdges.Prepare(renderDebug.ShowVisitEdges);
        // Only a drawable scene may evaluate native poses. Shadow receivers
        // use logical positions and can prepare while the landscape loads.
        objectCandidates.Prepare(state,state.X,state.Z,cachedPlayerHeight,room);
        if(room==0){
            if(!trees.Prepare(terrain.RenderSeason,townClock.CherryBlossomsBloom)){DrawLoading();return;}
            fences.Prepare(state);
            if(!droppedItems.Prepare(state,cachedPlayerHeight)){DrawLoading();return;}
        }
        PrepareHelipad();
        LoadingScreen.Finish();
        terrain.PrepareWaterOptics(state.Z,sunlight,environmentTint,room==0);
        if((weather.Raining(room)||weather.Snowing(room))&&villagers.Doors.PlayerFade<1)rain.Prepare(state,(Clock.Timestamp-sessionStart)/1000000.0,cachedPlayerHeight,weather.Snowing(room));
        else rain.Dispose();
        PrepareFoliage(environmentTint);
        int opaqueBuildingTypes=0;for(int type=0;type<BuildingCatalog.Count;type++)if(buildings.Ready(type))opaqueBuildingTypes|=1<<type;
        actorVisibility.Prepare(state,villagers.Doors,indoorCamera,waterInspection,cachedPlayerHeight,opaqueBuildingTypes,terrain.Ready);
        for(int actor=1;actor<actors.Length;actor++)if(actorVisibility.Visible[actor]&&(actors[actor]==null||!visualOverrides[actor]&&!modelResidency.Ready(state.NpcSpecies(actor-1))))actorVisibility.Visible[actor]=false;
        modelLod.Require(state,actorVisibility,uvDebug!=0||requestedUv>=0,villagers.Doors);
        UpdateAnimationFrame();
        if((renderDebug.ForceAttention||attendingVillager>=0)&&villagers.Doors.PlayerFade<1)attention.Prepare();
        phase=FrameProfile.Start();
        if(clockFade.Opacity>0)clockHud.Prepare(townClock);
        uiShapes.Prepare(screen,screen=="menu"?menuTab:-1,batchedDrawing&&!visualMemory.Pressure.UnderPressure);
        if((batchedDrawing&&screen=="map")||(runningMinimap.Opacity>0&&screen=="world"&&room==0))
            mapBackground.Prepare(state.Town);
        modelUsed=0;FrameProfile.End(8,phase);
        if(JobsLive||state.IsOnlineProjection)jobScene.Prepare();
        UiNoise.Prepare();preparedSubmitMicroseconds=0;preparedEnvironmentTint=environmentTint;preparedLampStrength=lampStrength;preparedShadowStrength=sunlight;
        phase=FrameProfile.Start();Hypercube.GameCube.FrameRenderer.Draw(device,room==0?TownLights.Tint(weather.Raining(room)?new Color32(112,143,151):new Color32(149,198,186),environmentTint):new Color32(32,40,39),this);FrameProfile.End(9,phase+preparedSubmitMicroseconds);
    }
    private int preparedEnvironmentTint,preparedLampStrength,preparedShadowStrength,tracedLightingMinute=-1;private long preparedSubmitMicroseconds;
    public void SubmitFramePart()
    {
        int environmentTint=preparedEnvironmentTint,lampStrength=preparedLampStrength;
        modelUsed=0;long submitAt=FrameProfile.Start(),phase=submitAt;
        NativeShadows.Environment(environmentTint,preparedShadowStrength);
        if(room==0&&tracedLightingMinute!=EnvironmentMinute){
            tracedLightingMinute=EnvironmentMinute;
            DebugOverlay.WriteLine("ANIMALTOWN DAYLIGHT minute "+tracedLightingMinute+" shadow "+NativeShadows.Stats(11)+" cloud "+NativeShadows.Stats(12)+" lamp "+NativeShadows.Stats(13)+" bytes "+NativeShadows.Stats(3));
        }
        float eyeHeight=PlayerHeight();
        if(room==0)camera.LookAt(new Vector3(state.X,CylinderWorld.CameraHeight+eyeHeight,state.Z+CylinderWorld.CameraBack),new Vector3(state.X,eyeHeight,state.Z-CylinderWorld.CameraAimAhead),new Vector3(0,1,0));
        else{float size=BuildingWorld.Size(state,room);indoorView.LookAt(new Vector3(indoorCamera.EyeX(size),indoorCamera.EyeY(size),indoorCamera.EyeZ(size)),new Vector3(size*.5f,.7f,size*.5f),new Vector3(0,1,0));}
#if ANIMALTOWN_ONLINE_PLAY
        if(reviewUi!=null)
        {
            BeginReviewViewport();
            if(room==0)reviewCamera!.LookAt(new Vector3(state.X,CylinderWorld.CameraHeight+eyeHeight,state.Z+CylinderWorld.CameraBack),new Vector3(state.X,eyeHeight,state.Z-CylinderWorld.CameraAimAhead),new Vector3(0,1,0));
            else{float size=BuildingWorld.Size(state,room);reviewCamera!.LookAt(new Vector3(indoorCamera.EyeX(size),indoorCamera.EyeY(size),indoorCamera.EyeZ(size)),new Vector3(size*.5f,.7f,size*.5f),new Vector3(0,1,0));}
        }
        renderer.Begin3D(reviewUi!=null?reviewCamera!:(room==0?camera:indoorView));
#else
        renderer.Begin3D(room==0?camera:indoorView);
#endif
        buildings.Begin(villagers.Doors);FrameProfile.End(4,phase);
        phase=FrameProfile.Start();int submissionAt=FrameProfile.SubmissionStart();if(room==0){terrain.Draw(renderer,terrain.ScrollTransform,room);}else buildings.Interior(renderer,state,worldRoot,indoorCamera,villagers.Doors);
        FrameProfile.SubmissionEnd(0,submissionAt);FrameProfile.End(4,phase);phase=FrameProfile.Start();
        if(renderDebug.ShowObstructedEdges)NativeWorldDiagnostics.EdgePass(1);
        if(room==0)
        {
            for(int i=1;i<BuildingWorld.Actors;i++)ModelAt(i);
            if(batchedDrawing)DrawActors();
            for(int i=1;i<BuildingWorld.Actors;i++)DrawJobOutfit(i);
            FrameProfile.End(5,phase);phase=FrameProfile.Start();submissionAt=FrameProfile.SubmissionStart();DrawTrees();FrameProfile.SubmissionEnd(1,submissionAt);FrameProfile.End(6,phase);phase=FrameProfile.Start();
            long groundAt=FrameProfile.Start();
            for(int n=0;n<objectCandidates.GroundCount;n++)
            {
                int i=objectCandidates.GroundIndices[n];
                var g=state.Town.Ground[i];if(g.Kind==0||g.Kind==4)continue;
                float y=0;
                bool buried=state.Town.Traffic.Winter&&!renderDebug.ShowGroundMipBands&&SnowVisibility.Buried(state.Town,g.X+.25f,g.Z+.25f,.5f,.5f,g.Kind==1?.05f:.27f);
                if(!IsTree(g)&&!buried)Box(g.X+.25f,y+.02f,g.Z+.25f,.5f,g.Kind==1?.03f:.25f,.5f,g.Kind==1?new Color32(105,81,54):g.Kind==2?new Color32(242,174,170):new Color32(229,190,82));
            }
            if(batchedDrawing)boxes.Flush();FrameProfile.End(3,groundAt);
            submissionAt=FrameProfile.SubmissionStart();
            NativeShadows.Objects(2);
            try{for(int building=0;building<BuildingWorld.Count;building++)buildings.Exterior(renderer,state,building,villagers.Doors);}
            finally{NativeShadows.Objects(0);}
            DrawHelipad(environmentTint);
            FrameProfile.SubmissionEnd(2,submissionAt);submissionAt=FrameProfile.SubmissionStart();
            fences.Draw(renderer,state,terrain.ScrollTransform,cachedPlayerHeight);FrameProfile.SubmissionEnd(3,submissionAt);
            groundAt=FrameProfile.Start();droppedItems.Draw(renderer);FrameProfile.End(3,groundAt);
            DrawStreetLights(environmentTint,lampStrength);DrawJobObjects();
        }
        if(room!=0){
            DrawJobRoomCoverings();
            if(!indoorCamera.WallVisible(2)){
                float size=BuildingWorld.Size(state,room),scale=size/4;
                Box(size*.5f-.63f*scale,0,size,.08f,2.23f,.08f,new Color32(133,80,48));
                Box(size*.5f+.55f*scale,0,size,.08f,2.23f,.08f,new Color32(133,80,48));
                Box(size*.5f-.63f*scale,2.15f,size,1.26f*scale,.08f,.08f,new Color32(133,80,48));
            }
            for(int i=1;i<BuildingWorld.Actors;i++)ModelAt(i);
            for(int i=0;i<BuildingWorld.FixtureCount(state,room);i++)if(BuildingWorld.Fixture(state,room,i,out float fx,out float fz,out float fw,out float fd,out float fh)){
                int kind=BuildingWorld.FixtureKind(state,room,i);if(kind==0)continue;
                Box(fx,0,fz,fw,fh,fd,kind==1?new Color32(176,119,81):new Color32(127,85,58));
                if(kind==1){Box(fx+.06f,fh,fz+.06f,fw-.12f,.12f,fd-.12f,new Color32(106,155,144));Box(fx+.1f,fh+.12f,fz+.1f,fw-.2f,.1f,.35f,new Color32(245,231,199));}
            }
            if(room==TownShop.Room)DrawShopStock();
        }
        for(int n=0;n<objectCandidates.PlacementCount;n++){var q=state.Town.Placed[objectCandidates.PlacementIndices[n]];if(q.Room==room)Box(q.X,0,q.Z,.7f,.7f,.7f,new Color32(176,119,81));}
        if(batchedDrawing)boxes.Flush();
        FrameProfile.End(7,phase);phase=FrameProfile.Start();
        ModelAt(0);
        if(batchedDrawing)DrawActors();
#if ANIMALTOWN_ONLINE_PLAY
        DrawOnlineActors();
#endif
        if(!session.Flight.Active)DrawJobOutfit(0);if(room!=0)for(int i=1;i<BuildingWorld.Actors;i++)DrawJobOutfit(i);
        FrameProfile.End(5,phase);phase=FrameProfile.Start();
        if(screen=="plot")
        {
            int x=PlotX,z=PlotZ,width=state.HomePlotWidth(state.ActiveProfile),height=state.HomePlotHeight(state.ActiveProfile);
            bool valid=CanChoosePlot(out _);
            var color=valid?new Color32(115,202,114):new Color32(229,116,89);
            for(int i=0;i<width;i++){Box(x+i,.035f,z,.9f,.04f,.9f,color);Box(x+i,.035f,z+height-1,.9f,.04f,.9f,color);}
            for(int i=1;i<height-1;i++){Box(x,.035f,z+i,.9f,.04f,.9f,color);Box(x+width-1,.035f,z+i,.9f,.04f,.9f,color);}
            if(session.Draft!=null)buildings.PreviewHome(renderer,state,x,z);
        }
        if(batchedDrawing)boxes.Flush();
        if(renderDebug.ShowObstructedEdges)NativeWorldDiagnostics.EdgePass(0);
        if(room==0){
            submissionAt=FrameProfile.SubmissionStart();
            terrain.DrawWater(renderer);
            FrameProfile.SubmissionEnd(0,submissionAt);
        }
        if(room==0&&renderDebug.ShowVisitEdges)visitEdges.Draw(renderer,identity,state,terrain.RenderSeason==3&&!renderDebug.ShowGroundMipBands);
        if((renderDebug.ForceAttention||attendingVillager>=0)&&villagers.Doors.PlayerFade<1)attention.Draw(renderer,state,actorVisibility,indoorCamera,cachedPlayerHeight,renderDebug.ForceAttention,attendingVillager);
        submissionAt=FrameProfile.SubmissionStart();
        foliage.Draw(renderer);
        if((weather.Raining(room)||weather.Snowing(room))&&villagers.Doors.PlayerFade<1)rain.Draw(renderer);
        FrameProfile.SubmissionEnd(4,submissionAt);
        renderer.End3D();NativeShadows.Environment(0xffffff,preparedShadowStrength);
#if ANIMALTOWN_ONLINE_PLAY
        if(reviewUi!=null){SubmitReviewUi();return;}
#endif
        ui.Begin();
        submissionAt=FrameProfile.SubmissionStart();
        if(!session.Flight.Active)DrawHud();
        if(FrameProfile.OverlayVisible)ui.Flush();
        FrameProfile.SubmissionEnd(5,submissionAt);FrameProfile.End(8,phase);FrameProfile.Draw(ui);
        float fade=villagers.Doors.PlayerFade;if(fade>0)ui.FillRectangle(new RectF(0,0,640,480),new Color32(12,19,25,(byte)(fade*255)));
        phase=FrameProfile.Start();submissionAt=FrameProfile.SubmissionStart();ui.End();FrameProfile.SubmissionEnd(6,submissionAt);FrameProfile.End(8,phase);
        if(FrameProfile.Enabled)preparedSubmitMicroseconds+=Clock.Timestamp-submitAt;

    }
    internal void RecordFrame()
    {
        if(!FrameProfile.Enabled)return;
        FrameProfile.TerrainState(terrain.WarmTerrainPrefetch,terrain.WarmQueued);
        int scene=screen=="map"?10:screen=="menu"&&menuTab==0?1:room!=0?2:screen=="world"?0:3;
        int drawingScene=batchedDrawing?(UiFont.Batched?scene:scene+32):scene+16;
        if(room==0){if(!terrain.Ready)drawingScene|=32768;if(terrain.MissingVisible>0)drawingScene|=16384;if(terrain.UrgentStages>0)drawingScene|=8192;}
        bool moved=!float.IsNaN(profiledX)&&(state.X!=profiledX||state.Z!=profiledZ);profiledX=state.X;profiledZ=state.Z;
        FrameProfile.Complete(GpuProfiler.Snapshot().Triangles,room==0?terrain.VisibleTriangles:0,room==0?terrain.RejectedQuads:0,drawingScene+RuntimeOptions.Mode*64,room==0?terrain.BuiltThisFrame:0,moved?(moveClip==1?2:1):0,room==0&&terrain.GroundRingActive,room==0&&terrain.GroundRingUploaded,NativeShadows.Stats(8)>0);
    }
    private void DrawStreetLights(int tint,int strength)
    {
        var metal=new Color32(51,74,65);
        for(int i=0;i<townLights.Count;i++){
            float x=townLights.X[i],z=townLights.Z[i],y=TerrainElevation.At(state.Town,x,z);
            Box(x-.18f,y,z-.18f,.36f,.15f,.36f,metal);
            Box(x-.06f,y+.15f,z-.06f,.12f,1.9f,.12f,metal);
            Box(x-.25f,y+2.03f,z-.25f,.5f,.09f,.5f,metal);
            Box(x-.29f,y+2.52f,z-.29f,.58f,.12f,.58f,metal);
        }
        if(batchedDrawing)boxes.Flush();
        NativeShadows.Environment(0xffffff,preparedShadowStrength);
        try{
            var glass=new Color32((byte)(150+105*strength/255),(byte)(166+55*strength/255),(byte)(146-12*strength/255));
            for(int i=0;i<townLights.Count;i++){
                float x=townLights.X[i],z=townLights.Z[i],y=TerrainElevation.At(state.Town,x,z);
                Box(x-.19f,y+2.12f,z-.19f,.38f,.4f,.38f,glass);
            }
            if(batchedDrawing)boxes.Flush();
        }finally{NativeShadows.Environment(tint,preparedShadowStrength);}
    }
    private bool IsTree(GroundItem g)=>TreeDrawBatch.IsTree(g);
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
        int cursor=0;
        while(cursor<objectCandidates.GroundCount){
            long selectionAt=FrameProfile.Start();
            int count=TreeDrawBatch.Fill(state.Town,objectCandidates,state.X,state.Z,cachedPlayerHeight,visibleTrees,ref cursor);
            FrameProfile.End(15,selectionAt);
            if(count>0)trees.Draw(renderer,state.Town,visibleTrees,count,state.Z);
        }
    }
    private void Text(string text,float x,float y,Color32 c,float scale=1.5f,int visibleCharacters=-1)=>font.Draw(ui,text,x,y,c,scale/1.5f,0,visibleCharacters);
    private void Strong(string text,float x,float y,Color32 c,float scale=1.5f)=>strongFont.Draw(ui,text,x,y,c,scale/1.5f);
    private void Heading(string text,float x,float y,Color32 c,float scale=1)=>headingFont.Draw(ui,text,x,y,c,scale);
    private void ButtonGlyph(string key,float x,float y)
    {
        bool shoulder=key=="R";float width=shoulder?20:14,radius=shoulder?4:7;
        Color32 fill=key=="A"?new Color32(53,139,101):key=="B"?new Color32(205,71,69):new Color32(118,129,130);
        Rounded(x-1,y-1,width+2,16,radius+1,new Color32(49,72,62));
        Rounded(x,y,width,14,radius,fill);
        Strong(key,x+(width-strongFont.Measure(key,1/1.5f))*.5f,y-.25f,Color32.White,1);
    }
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
        float nameWidth=Math.Max(180,Math.Min(504,strongFont.Measure(speaker,1.35f/1.5f)+28));
        Rounded(48,299,nameWidth,38,19,new Color32(53,105,84));
        Strong(speaker,62,306,new Color32(255,245,221),1.35f);Text(wrappedDialogue,57,344,new Color32(49,72,62),1.4f,typeAt);
        if(chatSpeech!=null){ButtonGlyph("A",500,448);Text(typeAt<message.Length?"Show all":chatPage+1<chatSpeech.Count?"Next":"Close",522,444,new Color32(49,72,62),1.1f);}
    }
    private static string Letter(char c)=>new string(new[]{c});
    private void DrawGroundMipLegend(Color32 ink)
    {
        Panel(384,24,224,94);
        Strong("Ground mip levels",400,34,ink,1);
        for(int level=0;level<GroundMipPalette.Levels;level++){
            uint rgba=GroundMipPalette.Color(level);float x=400+28*level;
            ui.FillRectangle(new RectF(x,57,20,12),new Color32((byte)(rgba>>24),(byte)(rgba>>16),(byte)(rgba>>8)));
            Text(groundMipLabels[level],x+7,70,ink,.9f);
        }
        Text(renderDebug.GroundMipmaps?"Pink line: "+renderDebug.GroundMipRadius+" tiles from player":"Mipmaps disabled",400,94,ink,.75f);
    }
    private void DrawHud()
    {
        var ink=new Color32(49,72,62);
        if(screen==ChatEntry.Screen){DrawChat(ink);return;}
        if(screen=="plot"){
            CanChoosePlot(out string reason);
            Panel(150,24,340,42);Heading(relocate?"Move your little corner":"Find your little corner",168,31,ink,1.05f);
            Panel(28,355,584,101);Rounded(44,342,104,28,14,new Color32(53,105,84));
            Strong("Hearth",58,346,new Color32(255,245,221),1.1f);
            Text(reason,48,372,ink,1.13f);
            Text(session.Draft!=null?"Start with 7 x 5 tiles. Expand when there is room.":plotSizeHelp[state.HomeReservedGardenTier(state.ActiveProfile)],48,402,ink,1);
            Text("Stick move   A choose spot   B back",48,430,ink,1.05f);return;
        }
        if(FrameProfile.OverlayVisible&&!AaStudy.Active){
            if(++profileLabelFrames>=30){profileLabelFrames=0;profileNativeLabel="Native: "+NativeShadows.Stats(9)+" slots / "+NativeShadows.Stats(10)/1024+" KiB";profileShadowLabel=(room==0&&terrain.GroundRingActive?"Ring / ":"")+"Shadows: "+NativeShadows.Stats(0)+" pages / "+NativeShadows.Stats(2)+" updated";}
            Text("GX indexed / batched",420,397,ink,.8f);
            Text(profileNativeLabel,420,408,ink,.55f);
            Text(profileShadowLabel,420,419,ink,.55f);
        }
        if(screen=="world"&&room!=0){Panel(205,371,410,61);Text("C-stick turn view / L recenter",220,380,ink,.95f);Text(buildingPrompt.Name(state,BuildingWorld.Building(room)),220,402,ink,1.1f);}
        if(screen=="world"){
            int door=villagers.Doors.Nearby(state,0,facingYaw);
            if(door>=0||villagers.Doors.Busy(0)){
                bool action=!villagers.Doors.Busy(0);
                string label=action?room==0?buildingPrompt.Enter(state,door):"Exit":villagers.Doors.Actors[0].Phase==DoorPhase.Waiting?"Waiting at door":"Through the doorway";
                DoorPopover(label,action);
            }else if(KobanSite.InReach(state))DoorPopover("Browse lost property",true);
        }
        if(clockFade.Opacity>0)clockHud.Draw(ui,clockFade.Opacity);
#if ANIMALTOWN_ONLINE_PLAY
        if(DrawOnlineUi(ink))return;
#endif
        if(room==TownShop.Room&&screen=="world")DrawShopHint(ink);
        if(DrawShopPanel(ink))return;
        if(DrawTravelUi(ink)||DrawMailUi(ink)||DrawJobUi(ink))return;
        if(screen=="dialogue")
        { SpeechBubble();return; }
        if(screen=="map"){DrawMap();return;}
        if(screen=="saving")
        { Panel(115,181,410,132);Heading("Saving your town",142,199,ink);Text("Please keep the memory\ncard inserted.",142,245,ink,1.25f);return; }
        if(screen=="save-quit"){
            ui.FillRectangle(new RectF(0,0,640,480),new Color32(19,37,24,96));
            Panel(99,130,442,212);
            Heading("Save & Quit?",127,152,ink);
            Text("Save your progress and return to the title screen.",127,190,ink,1.2f);
            for(int i=0;i<2;i++){
                float y=233+i*27;
                string label=i==0?"Keep playing":"Save & Quit";
                if(selected==i){Rounded(115,y,410,25,12,new Color32(221,231,188));Strong("> "+label,127,y,ink,1.2f);}
                else Text("  "+label,127,y,ink,1.2f);
            }
            Text("A select   B back",127,314,ink,1);
            return;
        }
        if(screen==DaylightLocationMenu.Screen){daylightMenu.Draw(ui,font,headingFont,batchedDrawing?uiShapes:null);return;}
        if(screen==VillagerVisualScreen){DrawVillagerVisuals(ink);return;}
        if(screen=="world"){if(room==0&&runningMinimap.Opacity>0)DrawMinimap();if(room==0&&renderDebug.ShowGroundMipBands)DrawGroundMipLegend(ink);return;}
        if((screen=="menu"&&menuTab==0)||screen=="item"){DrawBackpack();return;}
        Panel(45,75,550,350);string title=screen=="menu"?tabs[menuTab]:screen=="post"?"Town hall":screen=="mail"?"Mailbox":"Koban / Lost and Found";
        Heading(title,68,91,ink);
        int total=screen=="menu"?TabCount():screen=="mail"?Math.Max(1,state.MailCount):screen=="lost"?Math.Max(1,LostCount()):7;
        bool compact=screen=="menu"&&(menuTab==SettingsTab||menuTab==TestingTab);int page=compact?9:8,step=compact?24:29;float rowScale=compact?1.2f:1.4f;int first=selected/page*page;
        for(int i=first;i<Math.Min(total,first+page);i++){float y=131+(i-first)*step;if(i==selected){Rounded(57,y,520,step-2,(step-2)*.5f,new Color32(221,231,188));Strong("> "+RowLabel(i),68,y,ink,rowScale);}else Text("  "+RowLabel(i),68,y,ink,rowScale);}
        Text(screen=="lost"?(state.Visiting?"Town residents may collect these items. B close":"A collect   X undo visitor removal   B close"):
            TestingFocused(SeasonPreviewOption)?"Left/Right: choose   A: apply   B: close":
            TestingFocused(TimePreviewOption)?"Left/Right: 15 min   A: apply   X: select Live   B: close":
            screen=="menu"&&menuTab==TestingTab&&TestingOption(selected)==23?"Left/Right: 1 square   A: +1   X: Auto":
            screen=="menu"&&menuTab==TestingTab&&TestingOption(selected)==34?"A: toggle   Pink line marks the mip switch":"Stick / D-pad choose   L/R tabs   A select   B close",65,392,ink,1);
        if(screen=="menu"&&menuTab==SettingsTab){
            Text("Manual saves only.",65,331,ink,1);
            Text(saveStatusText.Get(saveStatus),65,357,ink,1);
        }
        if(screen=="menu"&&menuTab==TestingTab)Text("Debug options "+(first+1)+"-"+Math.Min(total,first+page)+" / "+total,65,357,ink,1);
    }
    private string RowLabel(int i)
    {
        if(screen=="post"||(screen=="menu"&&menuTab==4)){string[] a={"Deposit up to "+UiFont.Money(1000),"Withdraw up to "+UiFont.Money(1000),"Repay from wallet","Lost and Found","Mailbox","Write a letter","Visit / gate"};return a[i];}
        if(screen=="mail")return MailboxRow(i);
        if(screen=="lost"){int n=LostIndex(i);return n<0?"No lost items":Catalog.Items[state.Town.LostAndFound.ItemAt(n)].Name+(state.Town.LostAndFound.RecoveryIdAt(n)>0?" / visitor removal":"");}
        if(menuTab==0)return (i+1)+". "+Catalog.Items[state.Pockets[i]].Name;
        if(menuTab==1&&JobsLive)return "View errands and waiting rewards";
        if(menuTab==1)return i==0?"Tutorial / daily jobs":state.Town.Residents[i-1].Name+" / friendship "+state.Town.Residents[i-1].Friendship;
        if(menuTab==2){int id=Catalog.EntryAt(i);return Catalog.Items[id].Name+(godMode?" / Free (instant)":state.Owned[id]==0?" (not owned)":" / "+UiFont.Money(Catalog.Items[id].Buy));}
        if(menuTab==3){string[] a={"Roof palette","Exterior palette","Fence style","Mailbox",godMode?"Relocate (free)":"Relocate ("+UiFont.Money(50000)+")","Expand house","Expand garden"};return a[i];}
        if(menuTab==TestingTab)return TestingLabel(i);
        string[] settings={"Save Now","Save & Quit","Controls","About / build identity","Calendar","Chat","Daylight location","Debug / testing menu"};return settings[i];
    }
    private static string ItemActionLabel(PocketAction action,int item)=>action==PocketAction.Place?"Place":PocketMenu.Label(action,item);
    private float ItemActionWidth(int item,string title,int focus)
    {
        int count=PocketMenu.Count(state,focus,room);
        if(actionWidthItem==item&&actionWidthRoom==room&&actionWidthCount==count&&actionWidthTitle==title)return actionWidth;
        float widest=strongFont.Measure(title,1.3f/1.5f);actionRotateOffset=0;
        for(int i=0;i<count;i++){
            PocketAction action=PocketMenu.At(state,focus,room,i);
            float width=20+font.Measure(ItemActionLabel(action,item),1.05f/1.5f);
            if(action==PocketAction.Place){actionRotateOffset=width+10;width=actionRotateOffset+26+font.Measure("Rotate",.9f/1.5f);}
            widest=Math.Max(widest,width);
        }
        actionWidthItem=item;actionWidthRoom=room;actionWidthCount=count;actionWidthTitle=title;
        return actionWidth=(float)Math.Ceiling(widest)+32; // Same 16px padding on both sides.
    }
    private void DrawBackpack()
    {
        var ink=new Color32(49,72,62);int rows=state.Capacity/5;float height=78+rows*44;
        bool actions=screen=="item";int focus=actions?slot:selected;
        Rounded(198,57,82,50,24,new Color32(195,109,84));Rounded(210,66,58,28,14,new Color32(255,245,221));
        Bulged(70,73,350,height+7,32,new Color32(53,105,84));Panel(76,77,338,height);
        Heading("Backpack",96,92,ink);
        Rounded(286,94,105,30,15,new Color32(232,183,86));Strong(UiFont.Money(state.Wallet),298,98,ink,1.15f);
        for(int i=0;i<state.Capacity;i++)
        {
            float x=99+i%5*58,y=144+i/5*44;
            if(i==focus)Rounded(x-3,y-3,54,46,16,new Color32(232,183,86));
            Rounded(x,y,48,40,13,i==focus?new Color32(255,251,238):new Color32(231,228,202));
            int id=state.Pockets[i];
            if(id!=0){int visual=Catalog.IconId(id);itemIcons.Draw(ui,new RectF(visual%8*64,visual/8*64,64,64),new RectF(x+5,y+1,38,38),Color32.White);}
            else Rounded(x+21,y+17,6,6,3,new Color32(188,195,166));
        }
        if(state.BackpackTier<2){Text("Next pack: "+(state.Capacity+10)+" slots",436,325,ink,1);Text("Hearth's store",436,349,ink,1);}
        int item=state.Pockets[focus];
        if(item==0)return;
        // Follow the actual slot for both the title and action menu. Clamp the
        // panel (including bulges/shadow) above the HUD for all backpack sizes.
        float slotX=99+focus%5*58,slotY=144+focus/5*44;
        bool bound=state.IsJobItem(focus);int actionCount=PocketMenu.Count(state,focus,room);
        string title=itemTitleText.Get(BackpackJobTitle(focus,item));
        float popWidth=actions?ItemActionWidth(item,title,focus):168,popHeight=actions?72+actionCount*18:141;
        float popX=Math.Min(slotX+66,607-popWidth),popY=Math.Max(80,Math.Min(slotY-12,428-popHeight));
        float pointerY=Math.Max(popY+36,Math.Min(slotY+20,popY+popHeight-36));
        Panel(popX,popY,popWidth,popHeight);
        ui.FillTriangle(new Vector2(popX+2,pointerY-12),new Vector2(popX+2,pointerY+12),new Vector2(slotX+52,slotY+20),new Color32(255,245,221));
        float textX=popX+16;
        Strong(title,textX,popY+12,ink,1.3f);
        if(actions)
        {
            for(int i=0;i<actionCount;i++){
                float y=popY+56+i*18;
                PocketAction action=PocketMenu.At(state,focus,room,i);
                if(i==selected)Rounded(popX+10,y-1,popWidth-20,18,9,new Color32(221,231,188));
                if(action==PocketAction.Quit)ButtonGlyph("B",textX,y+1);
                else if(i==selected)ButtonGlyph("A",textX,y+1);
                Text(bound?(i==0?"Details":"Close"):ItemActionLabel(action,item),textX+20,y,ink,1.05f);
                if(action==PocketAction.Place){ButtonGlyph("R",textX+actionRotateOffset,y+1);Text("Rotate",textX+actionRotateOffset+26,y+1,ink,.9f);}
            }
        }
        else
        {
            int money=Catalog.MoneyValue(item);
            Text(money>0?"Value: "+UiFont.Money(money):"One item",textX,popY+69,ink,1.05f);
            ButtonGlyph("A",textX,popY+102);
            Strong("Item actions",textX+20,popY+101,ink,1.05f);
        }
    }
    private void DrawMinimap()
    {
        byte opacity=runningMinimap.Opacity;
        Bulged(MapBackground.MinimapLeft+4,MapBackground.MinimapTop+5,MapBackground.MinimapSize,MapBackground.MinimapSize,MapBackground.MinimapRadius,new Color32(49,72,62,(byte)(130*opacity/255)));
        mapBackground.DrawMinimap(ui,opacity);
        MinimapMarker(32,26,new Color32(187,107,71),3);
        MinimapMarker(50,26,new Color32(132,96,155),3);
        if(state.Town.Koban.Enabled)MinimapMarker(state.Town.Koban.X+1.5f,state.Town.Koban.Z+1.5f,new Color32(73,119,144),3);
        for(int p=0;p<GameState.ProfileCount;p++)if(state.HomePlaced(p))MinimapMarker(state.HomeX(p)+3,state.HomeZ(p)+3,new Color32(220,182,124),4);
        MinimapMarker(state.X,state.Z,new Color32(35,49,44),6);
        MinimapMarker(state.X,state.Z,Color32.White,4);
    }
    private void MinimapMarker(float x,float z,Color32 color,float size)
    {
        var p=MapBackground.MinimapPosition(x,z);
        color.A=runningMinimap.Opacity;
        ui.FillRectangle(new RectF(p.X-size*.5f,p.Y-size*.5f,size,size),color);
    }
    private Color32 MapTileColor(int x,int z)
    { return MapBackground.TileColor(state.Town.At(x,z)); }
    private void DrawMap()
    {
        Panel(65,65,510,365);Heading("Town map",85,79,new Color32(49,72,62));Text(Town.Width+" x "+Town.Height,310,83,new Color32(49,72,62),1.2f);
        const float left=160,top=115,scale=MapBackground.Scale;
        var storeColor=new Color32(187,107,71);
        var postOfficeColor=new Color32(132,96,155);
        var kobanColor=new Color32(73,119,144);
        var helipadColor=new Color32(81,98,130);
        var homeColor=new Color32(220,182,124,180);
        var villagerHouseColor=new Color32(94,64,48);
        var villagerColor=new Color32(153,76,103);
        if(batchedDrawing)mapBackground.Draw(ui);
        else for(int z=0;z<Town.Height;z+=5)for(int x=0;x<Town.Width;x+=5)
            ui.FillRectangle(new RectF(left+x*scale,top+z*scale,Math.Min(5,Town.Width-x)*scale,Math.Min(5,Town.Height-z)*scale),MapTileColor(Math.Min(x+2,Town.Width-1),Math.Min(z+2,Town.Height-1)));
        for(int i=0;i<=Town.ScreenColumns;i++)
        {
            float offset=Math.Min(i*Town.ScreenTileSize,Town.Width)*scale;
            ui.FillRectangle(new RectF(left+offset,top,1,MapBackground.MapHeight),new Color32(235,235,206,140));
            if(i<Town.ScreenColumns)Text(Letter((char)('A'+i)),left+offset+3,top+MapBackground.MapHeight+7,Color32.Black,.75f);
        }
        for(int i=0;i<=Town.ScreenRows;i++)
        {
            float offset=Math.Min(i*Town.ScreenTileSize,Town.Height)*scale;
            ui.FillRectangle(new RectF(left,top+offset,MapBackground.Size,1),new Color32(235,235,206,140));
            if(i<Town.ScreenRows)Text((i+1).ToString(),445,top+offset+3,Color32.Black,.8f);
        }
        for(int i=0;i<state.Town.GroundCount;i++){var g=state.Town.Ground[i];if(g.Kind==3)ui.FillRectangle(new RectF(left+g.X*scale,top+g.Z*scale,2,2),new Color32(45,106,60));}
        ui.FillRectangle(new RectF(left+32*scale,top+26*scale,4,4),storeColor);
        ui.FillRectangle(new RectF(left+50*scale,top+26*scale,4,4),postOfficeColor);
        if(state.Town.Koban.Enabled)ui.FillRectangle(new RectF(left+(state.Town.Koban.X+1.5f)*scale-2,top+(state.Town.Koban.Z+1.5f)*scale-2,4,4),kobanColor);
        ui.FillRectangle(new RectF(left+36*scale,top+9*scale,4,4),helipadColor);
        for(int p=0;p<GameState.ProfileCount;p++)if(state.HomePlaced(p))ui.FillRectangle(new RectF(left+state.HomeX(p)*scale,top+state.HomeZ(p)*scale,GameState.GardenWidths[state.HomeGardenTier(p)]*scale,GameState.GardenHeights[state.HomeGardenTier(p)]*scale),homeColor);
        foreach(var villager in state.Town.Residents)
            ui.FillRectangle(new RectF(left+(villager.HomeX+1.5f)*scale-2,top+(villager.HomeZ+1.5f)*scale-2,4,4),villagerHouseColor);
        for(int npc=0;npc<Town.NpcActorCount;npc++){
            if(!state.NpcPresent(npc))continue;
            var villager=state.Town.NpcAt(npc);
            ui.FillRectangle(new RectF(left+villager.X*scale,top+villager.Z*scale,3,3),villagerColor);
        }
        float mapX=state.X,mapZ=state.Z;if(room!=0)BuildingWorld.Entrance(state,BuildingWorld.Building(room),out mapX,out mapZ);
        ui.FillRectangle(new RectF(left+mapX*scale,top+mapZ*scale,4,4),Color32.White);

        var ink=new Color32(49,72,62);
        Strong("Legend",464,126,ink,1.15f);
        DrawMapLegendEntry("Villager houses",148,villagerHouseColor);
        DrawMapLegendEntry("Player homes",168,homeColor);
        DrawMapLegendEntry("Store",188,storeColor);
        DrawMapLegendEntry("Post office",208,postOfficeColor);
        DrawMapLegendEntry("Koban",228,kobanColor);
        DrawMapLegendEntry("Helipad",248,helipadColor);
        DrawMapLegendEntry("Villagers",268,villagerColor);
        DrawMapLegendEntry("You",288,Color32.White);
    }

    private void DrawMapLegendEntry(string label,float y,Color32 color)
    {
        var ink=new Color32(49,72,62);
        ui.FillRectangle(new RectF(464,y+3,10,10),ink);
        ui.FillRectangle(new RectF(465,y+4,8,8),color);
        Text(label,479,y,ink,.95f);
    }
}
