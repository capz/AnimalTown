using System;
using System.IO;
using AnimalTown;
using Dolphin.Diagnostics;

internal sealed partial class TownSession
{
    internal readonly HelipadFlight Flight=new HelipadFlight();

    internal bool CompleteTravel()
    {
        // RunSession has disposed/unwound its models, terrain, UI and temporary
        // roots. Full-card validation gets the same budget as startup recovery.
        LoadingScreen.Draw();TextureCache.TrimUnused();AnimalTown.GameCube.NativeTexturePages.Collect();
        try{
            using var cards=new OfflineTravelCards(State,File.ReadAllBytes(ContentPaths.Asset("savebanner.rgb5a3")),File.ReadAllBytes(ContentPaths.Asset("saveicon.rgb5a3")));
            var result=cards.Execute(travelAction,travelProfile,travelFrom,travelTo,travelAt);cards.SelectResult(result);AdoptTravel(result);
            if(Flight.Phase==FlightPhase.Cruise){
                if(HelipadSite.TryArrival(result,out float x,out float z)){
                    result.Space=0;result.X=x;result.Z=z;result.Facing=0;
                    DebugOverlay.WriteLine("ANIMALTOWN FLIGHT DESTINATION "+result.Town.Seed);
                }else{Flight.Cancel();LoadingScreen.Finish();}
            }
            TravelError="";return true;
        }catch(Exception error){Flight.Cancel();LoadingScreen.Finish();TravelError=error.Message??"Trip interrupted. Keep both town cards.";DebugOverlay.WriteLine("ANIMALTOWN TRAVEL RECOVERY "+TravelError);var saved=Load();if(saved.Loaded&&saved.State.Town.Jobs.TownId==State.Town.Jobs.TownId){State=saved.State;Loaded=true;}return false;}
    }
}
