namespace AnimalTown.Jobs;

public static class JobContent
{
    public const int NativeFruit=1,Parcel=18,Letter=19;
    public static bool Matches(int item,JobRewardCategory category)
    {
        if(!Catalog.Valid(item)||!Catalog.Has(item,ItemFlags.Gift))return false;
        return category==JobRewardCategory.Furniture?Catalog.Has(item,ItemFlags.Place):
            category==JobRewardCategory.Stationery?Catalog.Has(item,ItemFlags.Stationery):
            category==JobRewardCategory.Clothing||category==JobRewardCategory.WornClothing?Catalog.Has(item,ItemFlags.Wear):
            category==JobRewardCategory.Flooring?Catalog.Has(item,ItemFlags.Flooring):
            category==JobRewardCategory.Wallpaper&&Catalog.Has(item,ItemFlags.Wallpaper);
    }
    public static int CountCategory(JobRewardCategory category,int exclude=0)
    {int count=0;for(int i=1;i<Catalog.Items.Length;i++)if(i!=exclude&&Matches(i,category))count++;return count;}
    internal static JobRewardCategory CategoryOf(int item)
    {for(int c=0;c<5;c++)if(Matches(item,(JobRewardCategory)c))return (JobRewardCategory)c;return JobRewardCategory.Currency;}
    internal static void MigrateReward(ref VillagerJobReward reward)
    {
        if(reward.IsReply){if(reward.Item!=0&&!Catalog.Valid(reward.Item))throw new InvalidOperationException("Unsupported reply gift migration");return;}
        if(reward.State!=JobRewardState.Owed||reward.Item==0||Matches(reward.Item,reward.Category))return;
        if(reward.Category==JobRewardCategory.Currency)throw new InvalidOperationException("Currency reward has an item");
        // Append-only IDs remain valid today. A retired item in a later catalog
        // receives the first ordinary item in its frozen category, once, without
        // advancing any RNG stream or changing the reward's economic identity.
        for(int i=1;i<Catalog.Items.Length;i++)if(Matches(i,reward.Category)){if(reward.MigratedFromItem==0)reward.MigratedFromItem=reward.Item;reward.Item=i;return;}
        throw new InvalidOperationException("Missing migrated job reward category");
    }
    public static int PickCategory(IJobRandom random,JobRewardCategory category,int exclude=0)
    {
        int count=CountCategory(category,exclude);if(count==0)throw new InvalidOperationException("Empty enabled job reward category");
        int draw=random.NextInt(count);for(int i=1;i<Catalog.Items.Length;i++)if(i!=exclude&&Matches(i,category)&&draw--==0)return i;
        throw new InvalidOperationException("Invalid job reward content");
    }
    public static int PickTagged(IJobRandom random,ItemFlags tag,int exclude=0)
    {
        int count=0;for(int i=1;i<Catalog.Items.Length;i++)if(i!=exclude&&Catalog.Has(i,tag))count++;
        if(count==0)throw new InvalidOperationException("Empty enabled job objective pool");
        int draw=random.NextInt(count);for(int i=1;i<Catalog.Items.Length;i++)if(i!=exclude&&Catalog.Has(i,tag)&&draw--==0)return i;
        throw new InvalidOperationException("Invalid job objective content");
    }
    public static void Validate()
    {
        for(int c=0;c<5;c++)if(CountCategory((JobRewardCategory)c)==0)throw new InvalidOperationException("Missing required job reward category");
        int fruits=0,properties=0;for(int i=1;i<Catalog.Items.Length;i++){if(Catalog.Has(i,ItemFlags.Fruit)&&i!=NativeFruit)fruits++;if(Catalog.Has(i,ItemFlags.Property))properties++;}
        if(fruits==0||properties==0||CountCategory(JobRewardCategory.Clothing)<2||!Catalog.Has(14,ItemFlags.Fish)||!Catalog.Has(15,ItemFlags.Insect))throw new InvalidOperationException("Incomplete villager job objective content");
    }
}
