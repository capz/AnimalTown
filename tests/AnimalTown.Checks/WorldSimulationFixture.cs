using AnimalTown;
using Dolphin.Graphics;

// Link the actual console fixed step, replacing only hardware/rendering owners.
// Navigation, portals, movement, clocks and traffic use their production types.
internal sealed class TownSession
{
    internal OnboardingDraft? Draft;
}
internal static class NativeCalendar
{
    internal static int Time=30|(12<<6)|(10<<11)|(6<<16)|(6<<20);
    internal static int ReadClock()=>Time;
    internal static int Second;
    internal static int ReadSecond()=>Second;
}
internal sealed partial class TownGame
{
    internal readonly GameState state;
    internal readonly TownSession session=new TownSession();
    internal readonly VillagerJourney villagers=new VillagerJourney();
    internal readonly InteriorCamera indoorCamera=new InteriorCamera();
    internal readonly TownClock townClock=new TownClock();
    internal readonly RenderDebugOptions renderDebug=new RenderDebugOptions();
    internal readonly AnimationFrameClock animationClock=new AnimationFrameClock(BuildingWorld.Actors);
    internal readonly RunningMinimap runningMinimap=new RunningMinimap();
    internal readonly TerrainRenderer terrain=new TerrainRenderer();
    internal readonly ModelResidency modelResidency=new ModelResidency();
    internal readonly Model[] models=new Model[6];
    internal readonly bool[] hasIdleClip=new bool[6];
    internal readonly bool[] visualOverrides=new bool[BuildingWorld.Actors],visualIdle=new bool[BuildingWorld.Actors],visualAnimated=new bool[BuildingWorld.Actors];
    internal readonly bool[] villagersMoving=new bool[Town.NpcActorCount],restartClips=new bool[BuildingWorld.Actors];
    internal readonly DialogueVoice dialogueVoice=new DialogueVoice(new Dolphin.Audio.Sound());
    internal bool resourcesReady=true,playerRunning,cliffPreview=false;
    internal string screen="world",message="";
    internal int tick,day,month,typeAt,voiceTick,moveClip=-1,faceX,faceZ=-1,waterInspection=0;
    internal float facingYaw,desiredPlayerSpeed=1.05f,cachedPlayerHeight;
    internal int ResidencyRefreshes;
    internal int SnowRetirements;
    partial void RetireSnowResources(){SnowRetirements++;}
    private int room=>state.Space;
    internal TownGame(GameState state)
    {
        this.state=state;
        for(int i=0;i<models.Length;i++)models[i]=new Model();
    }
    private void RefreshResidencyDemand(long now){ResidencyRefreshes++;}
    internal void SetSnowfall(bool enabled)=>weather.SnowEnabled=enabled;
}
