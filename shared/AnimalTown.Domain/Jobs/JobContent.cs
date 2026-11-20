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
