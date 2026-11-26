namespace AnimalTown;

// Current split records already validate their own wire fields and checksums.
// Validate cross-record semantics directly instead of encoding/decoding a second
// complete copy of a large mailbox merely to validate a private readback.
internal static class SaveStateValidation
{
    private static void Require(bool value){if(!value)throw new InvalidOperationException("Invalid saved town state");}
    internal static GameState Finish(GameState s)
    {
        var t=s.Town;Require(t.StockCount>=6&&t.StockCount<=12&&t.Spending>=0);
        for(int i=0;i<12;i++)Require(t.Stock[i]>=0&&t.Stock[i]<Catalog.Items.Length);
        Require(s.ValidSavedHomePlots());
        for(int i=0;i<6;i++){
            var v=t.Residents[i];Require(v.X>=0&&v.X<Town.Width&&v.Z>=0&&v.Z<Town.Height&&v.PosX>=0&&v.PosX<Town.Width&&v.PosZ>=0&&v.PosZ<Town.Height);
            Require(v.HomeX>=1&&v.HomeX<=Town.Width-4&&v.HomeZ>=1&&v.HomeZ<=Town.Height-4);
        }
        for(int i=0;i<t.GroundCount;i++){var g=t.Ground[i];Require(Catalog.Valid(g.Item)&&g.X>=0&&g.X<Town.Width&&g.Z>=0&&g.Z<Town.Height&&g.Owner>=0&&g.Owner<GameState.ProfileCount);}
        BuildingSave.Decode(s,BuildingSave.Encode(s));
        int placed=t.PlacedCount;t.PlacedCount=0;
        for(int i=0;i<placed;i++){var p=t.Placed[i];Require(p.Item>0&&p.Item<Catalog.Items.Length&&p.Owner>=0&&p.Owner<=9&&s.CanPlace(p.Item,p.X,p.Z,p.Rotation,p.Room,p.Owner,true));t.PlacedCount++;}
        s.NormalizeBuildingLocations();s.CaptureProfile();t.NotifyTerrainChanged();s.ActivateSavedVisitor();return s;
    }
}
