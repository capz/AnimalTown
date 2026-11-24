using System.IO;
using AnimalTown;
using Dolphin.Graphics;

internal static class WaterEffectChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var indirect=WaterMaterial.WindWakerDefinition().GetPackage();
        check(indirect.Length==208&&indirect[4]==3&&indirect[8]==4&&indirect[12]==3&&indirect[20]==76,
            "Wind Waker keeps its original bounded v3 indirect material");
        check(indirect[191]==0&&indirect[199]==2&&indirect[207]==0,
            "Wind Waker still selects alternate UV then resolves base opacity");
        ShallowWaterMaterialChecks.Run(check);
        WaterBumpPatternChecks.Run(check);
        CheckIdleInteraction(check);
    }
    private static void CheckIdleInteraction(Action<bool,string> check)
    {
        // Source-level contract for the native TownGame handler. The full
        // GameCube assembly is independently compiled by the same CI workflow.
        DirectoryInfo? dir=new DirectoryInfo(AppContext.BaseDirectory);
        const string filename="game/AnimalTown.GameCube/Program.cs";
        while(dir!=null&&!File.Exists(Path.Combine(dir.FullName,filename)))dir=dir.Parent;
        if(dir==null)throw new Exception("Could not locate TownGame interaction source");
        string source=File.ReadAllText(Path.Combine(dir.FullName,filename));
        int start=source.IndexOf("private void Interact()",StringComparison.Ordinal);
        int end=source.IndexOf("private void UpdateAnimationFrame(",start,StringComparison.Ordinal);
        check(start>=0&&end>start,"GameCube player interaction handler is still available");
        string action=source.Substring(start,end-start);
        check(!action.Contains("A quiet moment")&&!action.Contains("A at the doorway to leave"),
            "empty-space A press has no default outdoor or indoor chat bubble");
        check(action.Contains("if(tick%3==0&&TownItemActions.Catch(state,15))Say")&&
              !action.Contains("No butterfly in reach"),
            "bug-net misses are silent without removing successful catches");
        string conversations=File.ReadAllText(Path.Combine(dir.FullName,"game/AnimalTown.GameCube/TownGame.JobScene.cs"));
        check(action.Contains("if(TalkNearby())return;")&&conversations.Contains("Say(villager.Name,state.Talk(target))")&&
              action.Contains("if(state.Pickup(tx,tz)||state.Pickup(x,z))")&&action.Contains("\"Picked up!\""),
            "genuine villager conversation and item-pickup feedback remain enabled");
    }
}
