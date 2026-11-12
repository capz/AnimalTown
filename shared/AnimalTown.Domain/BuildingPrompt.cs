namespace AnimalTown;

// Names are instance roles/owners, not mesh filenames. Refresh only when an
// owner changes; a stationary HUD does not concatenate strings every frame.
public sealed class BuildingPrompt
{
    private readonly string?[] owners=new string?[BuildingWorld.Count],names=new string?[BuildingWorld.Count],entries=new string?[BuildingWorld.Count];
    public string Name(GameState state,int building)
    {
        var role=BuildingWorld.Role(building);
        if(role==BuildingRole.Store)return "Hearth's Store";
        if(role==BuildingRole.TownHall)return "Town Hall";
        string owner=BuildingWorld.OwnerName(state,building);
        if(owners[building]!=owner){owners[building]=owner;names[building]=owner+"'s House";entries[building]="Enter "+names[building];}
        return names[building]!;
    }
    public string Enter(GameState state,int building)
    {
        Name(state,building);var role=BuildingWorld.Role(building);
        return role==BuildingRole.Store?"Enter Hearth's Store":role==BuildingRole.TownHall?"Enter Town Hall":entries[building]!;
    }
}
