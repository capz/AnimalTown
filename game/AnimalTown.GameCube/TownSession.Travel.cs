using System;
using System.IO;
using AnimalTown;
using Dolphin.Diagnostics;

internal sealed partial class TownSession
{
    internal bool CompleteTravel()
    {
        // RunSession has disposed/unwound its models, terrain, UI and temporary
        // roots. Full-card validation gets the same budget as startup recovery.
        LoadingScreen.Draw();TextureCache.TrimUnused();AnimalTown.GameCube.NativeTexturePages.Collect();
        try{
            using var cards=new OfflineTravelCards(State,File.ReadAllBytes(ContentPaths.Asset("savebanner.rgb5a3")),File.ReadAllBytes(ContentPaths.Asset("saveicon.rgb5a3")));
            var result=cards.Execute(travelAction,travelProfile,travelFrom,travelTo,travelAt);cards.SelectResult(result);AdoptTravel(result);TravelError="";return true;
        }catch(Exception error){TravelError=error.Message??"Trip interrupted. Keep both town cards.";DebugOverlay.WriteLine("ANIMALTOWN TRAVEL RECOVERY "+TravelError);var saved=Load();if(saved.Loaded&&saved.State.Town.Jobs.TownId==State.Town.Jobs.TownId){State=saved.State;Loaded=true;}return false;}
    }
}
