using AnimalTown;
using Dolphin.Timing;

internal sealed partial class TerrainRenderer
{
    // Profiling reports the same fixed policy as the archive's sole DMA owner.
    internal bool WarmTerrainPrefetch=>true;

    private void BakeArchive(GameState state,FloorStrips staging,byte[] data)
    {
        long deadline=Clock.Timestamp+12000;
        for(int stage=0;stage<256&&!archive!.Ready&&(stage==0||Clock.Timestamp<deadline);stage++){
            if(!archive.Bake(state,this,staging,data))break;
            BuiltThisFrame++;
        }
    }
}
