using AnimalTown;

internal static class RenderDebugChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var preview=new RenderDebugOptions();var state=new GameState(73);var before=SaveCodec.Encode(state);
        check(preview.SeasonOverride==-1&&!preview.ForceAttention&&preview.RimPercent==100,"render previews start automatic, with attention off and original rim strength");
        for(int month=-24;month<36;month++){
            check(preview.EffectiveSeason(month)==TerrainTiles.Season(month),"automatic preview follows the existing calendar and negative-month wrapping");
            for(int season=0;season<4;season++){
                preview.CycleSeason();
                check(preview.SeasonOverride==season&&preview.EffectiveSeason(month)==season,"every forced season overrides the calendar independently");
                check(preview.EffectiveSeason(month,3-season)==season,"debug choice overrides a fixed build season too");
            }
            preview.CycleSeason();check(preview.SeasonOverride==-1,"cycling winter restores automatic season selection");
        }
        for(int n=0;n<100;n++)preview.AdjustRim(-1);
        check(preview.RimPercent==0,"repeated decreases clamp at zero rather than wrapping");
        for(int n=0;n<40;n++){preview.AdjustRim(1);check(preview.RimPercent==(n+1)*5,"rim intensity supports precise five-percent steps");}
        preview.AdjustRim(1);check(preview.RimPercent==200,"maximum intensity remains bounded");
        preview.ResetRim();check(preview.RimPercent==100,"reset restores the original TEV contribution");
        preview.ForceAttention=true;preview.CycleSeason();preview.AdjustRim(1);
        check(before.SequenceEqual(SaveCodec.Encode(state)),"render previews never change serialized player, calendar, requests or terrain state");
        check(RenderDebugOptions.AttentionHeight(1.7f)>RenderDebugOptions.AttentionHeight(1)&&RenderDebugOptions.AttentionHeight(float.NaN)==3.6f,"attention anchor follows rig height and has a finite missing-rig fallback");
        float previousRadius=360f/6.28318530718f;
        var p=CylinderWorld.Surface(12,0);
        check(Math.Abs(p.Pitch/(12/previousRadius)-1.08f)<.000001f,"world turns eight percent farther for the same logical arc distance");
        float previousY=previousRadius*((float)Math.Cos(12/previousRadius)-1);
        check(p.Y<previousY&&p.Z>0,"stronger curvature bends downward while preserving logical north/south orientation");
        long allocated=GC.GetAllocatedBytesForCurrentThread();
        for(int n=0;n<1000;n++){preview.AdjustRim(1);preview.AdjustRim(-1);preview.CycleSeason();preview.EffectiveSeason(n);}
        check(GC.GetAllocatedBytesForCurrentThread()==allocated,"preview adjustments allocate no managed frame data");
    }
}
