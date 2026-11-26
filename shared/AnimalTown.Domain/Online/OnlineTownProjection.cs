namespace AnimalTown.Online;

// The public world has its own allowlisted format. It is intentionally not a
// SaveCodec payload: credentials, other inventories, mail, balances, job ledgers
// and lost-and-found custody never enter these bytes.
public static class OnlineTownProjection
{
    public const int MaximumBytes = 96 * 1024;
    const int Magic = 0x3157544f;
    public static int Write(GameState state, byte[] bytes)
    {
        var w = new OnlinePacketWriter(bytes); var town = state.Town;
        w.Int(Magic); w.Int(town.Seed); w.Long(unchecked((long)town.Jobs.TownId));
        w.Int(town.Day); w.Int(town.Month); w.Int(town.DaylightLocation);
        w.Int(town.Koban.X); w.Int(town.Koban.Z);
        w.Bytes(town.Tiles, 0, Town.Cells); w.Bytes(town.Elevation, 0, Town.Cells);
        w.Bytes(town.Paths.Packed, 0, TownPaths.PackedBytes);
        w.Bytes(town.Traffic.Visits, 0, town.Traffic.Visits.Length);
        state.CaptureProfile();
        for (int i = 0; i < GameState.ProfileCount; i++)
        {
            var p = state.Profiles[i]; w.Byte(p.Created ? (byte)1 : (byte)0); w.Text(p.Name, 32);
            w.Int(p.HousePlaced ? 1 : 0); w.Int(p.HouseX); w.Int(p.HouseZ); w.Int(p.HouseTier);
            w.Int(p.GardenTier); w.Int(p.Roof); w.Int(p.Wall); w.Int(p.Fence); w.Int(p.Mailbox); w.Int(p.Exterior);
        }
        for (int i = 0; i < BuildingWorld.Count; i++) w.Int(state.BuildingTypes[i]);
        for (int i = 0; i < Town.SavedVillagerCount; i++)
        {
            var v = town.Residents[i]; var id = town.Jobs.Npc(i);
            w.Long(unchecked((long)id.Town)); w.Long(unchecked((long)id.Serial));
            w.Text(v.Name, 32); w.Text(v.Phrase, 64); w.Int(town.Jobs.Species(i)); w.Int(town.Jobs.Outfit(i));
            w.Int(v.HomeX); w.Int(v.HomeZ); w.Int(v.Space); w.Int((int)(v.PosX * 65536)); w.Int((int)(v.PosZ * 65536));
            w.Int((int)(v.Yaw * 65536));
        }
        int visibleGround = 0;
        for (int i = 0; i < town.GroundCount; i++) if (town.Ground[i].Kind != 4) visibleGround++;
        w.Int(visibleGround);
        for (int i = 0; i < town.GroundCount; i++)
        {
            var g = town.Ground[i];
            if (g.Kind == 4) continue;
            // Custody is private. Ordinary visible ground objects are public.
            w.Int(g.Item); w.Int(g.X); w.Int(g.Z); w.Int(g.PackedKind);
        }
        w.Int(town.PlacedCount);
        for (int i = 0; i < town.PlacedCount; i++)
        {
            var p = town.Placed[i]; w.Int(p.Item); w.Int(p.X); w.Int(p.Z); w.Int(p.Rotation); w.Int(p.Room); w.Int(p.Owner);
        }
        w.Int(town.StockCount); w.Int(town.Shop.Revision);
        for (int i = 0; i < town.StockCount; i++) w.Int(town.Stock[i]);
        for (int i = 0; i < TownShop.StoredSaleCount; i++) { w.Int(town.Shop.SaleItems[i]); w.Int(town.Shop.SalePrices[i]); w.Int(town.Shop.SaleOwners[i]); }
        if (w.Count > MaximumBytes) throw new InvalidOperationException("Public town exceeds its bound.");
        return w.Count;
    }

    public static GameState Read(byte[] bytes, int count)
    {
        if (count > MaximumBytes) throw new InvalidOperationException("Public town exceeds its bound.");
        var r = new OnlinePacketReader(bytes, 0, count);
        int Range(int minimum, int maximum) { int value = r.Int(); if (value < minimum || value > maximum) throw new InvalidOperationException("Invalid public town field."); return value; }
        int Item(bool empty = true) => Range(empty ? 0 : 1, Catalog.Items.Length - 1);
        if (r.Int() != Magic) throw new InvalidOperationException("Unsupported public town.");
        int seed = r.Int(); ulong identity = unchecked((ulong)r.Long());
        if (identity == 0) throw new InvalidOperationException("Public town identity is missing.");
        var state = new GameState(seed) { IsOnlineProjection = true }; var town = state.Town;
        town.Jobs.Initialize(identity); town.Day = r.Int(); town.Month = r.Int(); town.DaylightLocation = Range(0, DaylightLocations.Count);
        int kobanX = r.Int(), kobanZ = r.Int(); town.Koban.Restore(kobanX, kobanZ);
        r.Bytes(town.Tiles, 0, Town.Cells); r.Bytes(town.Elevation, 0, Town.Cells);
        for (int i = 0; i < Town.Cells; i++) if (town.Tiles[i] > (byte)Terrain.CoastWest) throw new InvalidOperationException("Invalid public terrain.");
        r.Bytes(town.Paths.Packed, 0, TownPaths.PackedBytes); town.Paths.Validate();
        r.Bytes(town.Traffic.Visits, 0, town.Traffic.Visits.Length);
        for (int i = 0; i < GameState.ProfileCount; i++)
        {
            var p = state.Profiles[i]; int created = r.Byte(); if (created > 1) throw new InvalidOperationException("Invalid public resident.");
            p.Created = created == 1; p.Name = r.Text(32); p.HousePlaced = Range(0, 1) == 1;
            p.HouseX = Range(0, Town.Width - 1); p.HouseZ = Range(0, Town.Height - 1); p.HouseTier = Range(0, 5);
            p.GardenTier = Range(0, 4); p.Roof = Range(0, 255); p.Wall = Range(0, 255); p.Fence = Range(0, 255);
            p.Mailbox = Range(0, 255); p.Exterior = Range(0, 255);
            Array.Clear(p.Pockets, 0, p.Pockets.Length); Array.Clear(p.Mail, 0, p.Mail.Length); Array.Clear(p.Owned, 0, p.Owned.Length);
        }
        for (int i = 0; i < BuildingWorld.Count; i++) state.BuildingTypes[i] = (byte)Range(0, BuildingCatalog.Count - 1);
        for (int i = 0; i < Town.SavedVillagerCount; i++)
        {
            var id = new Jobs.JobIdentity(unchecked((ulong)r.Long()), unchecked((ulong)r.Long()));
            if (!id.IsValid) throw new InvalidOperationException("Invalid public resident identity.");
            town.Jobs.SetNpcIdentity(i, id); var v = town.Residents[i]; v.Name = r.Text(32); v.Phrase = r.Text(64);
            town.Jobs.NpcSpecies[i] = Range(0, ModelResidencyDemand.SpeciesCount - 1); town.Jobs.SetOutfit(i, Item());
            v.HomeX = Range(0, Town.Width - 1); v.HomeZ = Range(0, Town.Height - 1); v.Space = r.Int();
            if (!BuildingWorld.ValidSpace(v.Space)) throw new InvalidOperationException("Invalid public room.");
            v.PosX = Range(0, Town.Width * 65536 - 1) / 65536f; v.PosZ = Range(0, Town.Height * 65536 - 1) / 65536f;
            v.X = (int)v.PosX; v.Z = (int)v.PosZ; v.Yaw = r.Int() / 65536f;
        }
        town.GroundCount = Range(0, town.Ground.Length);
        for (int i = 0; i < town.GroundCount; i++)
        { var g = town.Ground[i]; g.Item = Item(); g.X = Range(0, Town.Width - 1); g.Z = Range(0, Town.Height - 1); g.RestoreKind(Range(0, 12)); }
        town.PlacedCount = Range(0, town.Placed.Length);
        for (int i = 0; i < town.PlacedCount; i++)
        { var p = town.Placed[i]; p.Item = Item(false); p.X = Range(0, Town.Width - 1); p.Z = Range(0, Town.Height - 1); p.Rotation = Range(0, 3); p.Room = r.Int(); p.Owner = Range(0, 9); if (!BuildingWorld.ValidSpace(p.Room)) throw new InvalidOperationException("Invalid public room."); }
        town.StockCount = Range(0, town.Stock.Length); town.Shop.Revision = Range(0, int.MaxValue);
        for (int i = 0; i < town.StockCount; i++) town.Stock[i] = Item();
        for (int i = 0; i < TownShop.StoredSaleCount; i++) { town.Shop.SaleItems[i] = Item(); town.Shop.SalePrices[i] = Range(0, GameState.WalletLimit); town.Shop.SaleOwners[i] = Range(0, GameState.ProfileCount - 1); }
        r.End();
        // Public house fields are independent of the recipient's private self.
        var owner = state.Profiles[0]; state.HousePlaced = owner.HousePlaced; state.HouseTier = owner.HouseTier; state.GardenTier = owner.GardenTier;
        town.HouseX = owner.HouseX; town.HouseZ = owner.HouseZ;
        state.Roof = owner.Roof; state.Wall = owner.Wall; state.Fence = owner.Fence; state.Mailbox = owner.Mailbox; state.Exterior = owner.Exterior;
        state.Wallet = 0; state.Savings = 0; state.Debt = 0; state.MailCount = 0;
        Array.Clear(state.Pockets, 0, state.Pockets.Length); Array.Clear(state.Mail, 0, state.Mail.Length); Array.Clear(state.Owned, 0, state.Owned.Length);
        town.NotifyTerrainChanged(); town.NotifyObjectsChanged(); return state;
    }
}
