namespace AnimalTown;

// Appearance IDs are session-only. The six saved species IDs never change.
public static class VillagerAppearance
{
    public static readonly string[] Models={"fox","bunny","giraffe","cat","hamster","spaniel","penguin","whitetiger","lion","robin","raccoon","pugofficer"};
    public static readonly string[] Names={"Fox","Bunny","Giraffe","Maine coon","Hamster","Cocker spaniel","Penguin","White tiger","Lion","Robin","Raccoon","Grumpy pug officer"};
    public const int Count=12;
    public static bool Valid(int appearance)=>(uint)appearance<Count;
}

public sealed class VillagerAppearanceSelection
{
    private readonly int[] overrides=new int[BuildingWorld.Actors];
    public VillagerAppearanceSelection(){for(int i=0;i<overrides.Length;i++)overrides[i]=-1;}
    public int Current(GameState state,int actor)
    {
        if((uint)actor>=overrides.Length)throw new ArgumentOutOfRangeException(nameof(actor));
        return overrides[actor]>=0?overrides[actor]:actor==0?state.Species:state.NpcSpecies(actor-1);
    }
    public static bool Present(GameState state,int actor)=>(uint)actor<BuildingWorld.Actors&&(actor==0||state.NpcPresent(actor-1));
    public bool Used(GameState state,int appearance)
    {
        if(!VillagerAppearance.Valid(appearance))return true;
        for(int actor=0;actor<overrides.Length;actor++)if(Present(state,actor)&&Current(state,actor)==appearance)return true;
        return false;
    }
    public int NextUnused(GameState state,int appearance,int direction)
    {
        for(int i=0;i<VillagerAppearance.Count;i++){
            appearance=(appearance+VillagerAppearance.Count+(direction<0?-1:1))%VillagerAppearance.Count;
            if(!Used(state,appearance))return appearance;
        }
        return -1;
    }
    public bool Apply(GameState state,int actor,int appearance)
    {
        if(!Present(state,actor)||Used(state,appearance))return false;
        overrides[actor]=appearance;return true;
    }
}
