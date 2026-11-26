using AnimalTown.Jobs;

namespace AnimalTown.Online;

public static class OnlineJobPolicy
{
    public static bool Supported(JobTemplate template) => template is JobTemplate.DeliveryClothing or
        JobTemplate.DeliveryProperty or JobTemplate.RetrievalBorrowed or JobTemplate.FavorFruit or JobTemplate.FavorLetter;
    public static bool CanOffer(GameState town, GameState participant, int npc, JobTemplate template, long now)
    {
        if (!Supported(template) || (uint)npc >= Town.SavedVillagerCount || !town.NpcPresent(npc) ||
            !JobCalendar.Season(template, now, true)) return false;
        if (template == JobTemplate.FavorFruit)
        {
            for (int i = 0; i < participant.Capacity; i++)
                if (!participant.IsJobItem(i) && participant.Pockets[i] != town.Town.Jobs.NativeFruit && Catalog.Has(participant.Pockets[i], ItemFlags.Fruit)) return true;
            return false;
        }
        return true;
    }
}
