using System;
using Dolphin.Timing;

// Demand/sampling must finish before this grant starts. Both opportunities in
// one presentation spend the same credit; draw time does not spend the remainder.
internal sealed class VisualResidencyBudget
{
    internal const int FrameCreditUs=1000;
    private int remaining;
    private long deadline;
    internal long Begin(bool afterDraw)
    {
        if(!afterDraw)remaining=FrameCreditUs;
        deadline=Clock.Timestamp+remaining;return deadline;
    }
    internal int Remaining=> (int)Math.Min(FrameCreditUs,Math.Max(0,deadline-Clock.Timestamp));
    internal void End(){remaining=Remaining;}
}
