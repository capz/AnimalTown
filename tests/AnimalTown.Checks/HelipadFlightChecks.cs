using AnimalTown;

internal static class HelipadFlightChecks
{
    internal static void Run(Action<bool,string> check)
    {
        for(int seed=0;seed<20;seed++){
            var state=new GameState(seed);
            check(HelipadSite.Exists(state.Town)&&HelipadSite.TryArrival(state,out _,out _),
                "every generated town has a dry approach to its fixed helipad");
        }
        var town=new GameState(42);
        check(HelipadSite.TryArrival(town,out float x,out float z)&&x==HelipadSite.CenterX&&z>HelipadSite.Z+HelipadSite.Depth,
            "usual arrival is on grass directly in front of the pad");
        town.Town.SetTerrain((int)x,(int)z,Terrain.Water);
        check(HelipadSite.TryArrival(town,out float alternate,out float alternateZ)&&(alternate!=x||alternateZ!=z),
            "a blocked arrival chooses another clear spot");
        town.Town.SetTerrain((int)HelipadSite.DoorX,(int)HelipadSite.StepZ,Terrain.Water);
        check(!HelipadSite.TryArrival(town,out _,out _),"a blocked step cannot board or disembark");

        foreach(int direction in new[]{-1,1}){
            var flight=new HelipadFlight();
            flight.Board(direction);
            flight.Land();
            check(flight.Phase==FlightPhase.Boarding&&flight.PlayerWalking,"an early load signal cannot skip boarding");
            flight.PlayerPose(x,z,out float startX,out float startZ,out float height,out _);
            check(startX==x&&startZ==z&&height==0,"boarding starts at the outdoor player position");
            for(int i=0;i<38;i++)flight.Tick();
            check(flight.Phase==FlightPhase.Takeoff&&flight.PlayerOnboard,"the player is inside the helicopter for takeoff");
            for(int i=0;i<30;i++)flight.Tick();
            flight.CraftPose(out float takeoff,out float rise,out _,out float door);
            check((takeoff-HelipadSite.CenterX)*direction>0&&rise>HelipadSite.DeckHeight&&door==0,
                "takeoff climbs toward the chosen edge with the door closed");
            for(int i=0;i<3000;i++)flight.Tick();
            check(flight.Phase==FlightPhase.Cruise&&flight.PlayerOnboard,"flight can wait indefinitely for the destination scene");
            flight.Land();flight.CraftPose(out float entry,out _,out _,out _);
            check(flight.Phase==FlightPhase.Landing&&(entry-HelipadSite.CenterX)*direction<0,
                "arrival flies in from the opposite edge");
            for(int i=0;i<42;i++)flight.Tick();
            flight.CraftPose(out float parked,out float deck,out _,out door);
            check(flight.PlayerWalking&&parked==HelipadSite.CenterX&&deck==HelipadSite.DeckHeight&&door==1,
                "disembarking starts after touchdown and the door is open");
            for(int i=0;i<34;i++)flight.Tick();
            check(!flight.Active,"the flight restores input after disembarking");
        }
        Console.WriteLine("Helipad approaches, both flight directions, loading hold and arrival checks passed");
    }
}
