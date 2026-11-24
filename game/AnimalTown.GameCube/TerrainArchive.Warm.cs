// The shared archive pump handles startup, travel and cold restoration. Keep
// profiler compatibility without creating a second native DMA owner.
internal sealed partial class TerrainArchive
{
    // Legacy dev-config values cannot switch ordinary travel back to blocking
    // transfers. The UI no longer offers this retired experiment.
    internal bool WarmPrefetch {get=>true;set{}}
    internal bool WarmActive=>Ready&&!CacheSuspended;
    internal int WarmQueued
    {
        get{int pending=0;for(int i=0;i<selection.Count;i++){int section=selection.Sections[i];if(visible[section]&&!bound[section])pending++;}return pending;}
    }
    internal int WarmLateSelections {get;private set;}
    internal int WarmEvictions {get;private set;}
    internal int WarmCancellations {get;private set;}
}
