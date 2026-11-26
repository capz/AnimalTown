namespace AnimalTown;

public sealed partial class GameState
{
    // The hosted writer owns public stock and NPC availability. Personal
    // interest, house work and ordinary card jobs/mail advance on the card;
    // running those twice would create conflicting offline economic changes.
    public bool AdvanceHostedCalendar(int day,int month)
    {
        if(IsOnlineProjection||!Town.Jobs.Initialized)throw new InvalidOperationException("A canonical hosted town is required.");
        if(day<=Town.Day)return false;
        Town.Day=day;Town.Month=month;
        Town.StockCount=Town.Spending>=100000?12:Town.Spending>=25000?9:6;
        for(int i=0;i<Town.StockCount;i++)Town.Stock[i]=10+(day+i)%4;
        Town.Shop.NewDay();return true;
    }
    public void AdvanceHostedPersonalCalendar(int day,int month,int previousMonth)
    {
        if(IsOnlineProjection||!Town.Jobs.Initialized)throw new InvalidOperationException("A canonical home is required.");
        CaptureProfile();
        for(int profile=0;profile<ProfileCount;profile++)if(Profiles[profile].Created)
        {CompleteGardenExpansion(profile,day);Profiles[profile].Rollover(day,month,previousMonth);}
        RestoreProfile(ActiveProfile);
    }
}
