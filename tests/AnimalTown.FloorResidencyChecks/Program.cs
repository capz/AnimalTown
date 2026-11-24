using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Timing;

int checks=0;
void Check(bool condition,string message){checks++;if(!condition)throw new Exception(message);}
TerrainRenderer Fixture()
{
    Clock.Timestamp=0;GraphicsDevice.Default.NativeBytes=0;GraphicsDevice.Default.IsFrameActive=false;
    Dolphin.IO.File.Reset();TextureCache.Reset();Dolphin.Diagnostics.DebugOverlay.Messages.Clear();return new TerrainRenderer();
}
void Restore(TerrainRenderer renderer)
{
    for(int step=0;step<16;step++){
        if(renderer.Resume(Clock.Timestamp+1000))return;
        Dolphin.IO.File.CompleteNext();
    }
    throw new Exception("Floor texture restoration did not finish");
}

var floor=Fixture();byte[] metadata=floor.Metadata;int season=floor.Season;
floor.Suspend();
Check(floor.Suspended&&GraphicsDevice.Default.NativeBytes==0,"indoor suspension physically releases every owning floor lease");
Check(ReferenceEquals(metadata,floor.Metadata)&&floor.Season==season,"floor suspension preserves season and metadata for the existing ARAM geometry");
Check(floor.NoBorrowers&&floor.Mips.Borrowed==0,"floor suspension clears material, secondary, path fallback and debug mip borrowers before eviction");
Check(Dolphin.Diagnostics.DebugOverlay.Messages.Last().Contains("owned bytes 3072 -> 0 source bytes 0"),
    "suspension reports independently verified cache-source eviction, not only cleared renderer references");
int releases=TextureCache.Releases;floor.Suspend();
Check(TextureCache.Releases==releases,"repeated indoor suspension is idempotent");
Check(!floor.Resume(Clock.Timestamp)&&Dolphin.IO.File.Reads==0,"expired restore budget performs no disc read or native allocation");
floor.Memory=new VisualMemoryPressure();floor.Memory.Observe(128*1024,0,0,0);
Check(!floor.Resume(1000)&&Dolphin.IO.File.Reads==0,"essential floor restoration waits for usable global headroom");
floor.Memory.Observe(8*1024*1024,0,0,0);
Check(!floor.Resume(1000)&&Dolphin.IO.File.Reads==1&&TextureCache.Loads==0,"restoration starts a byte-only read without exposing a partial floor");
Check(!floor.Resume(1000)&&TextureCache.Loads==0,"an incomplete floor read never blocks or creates native resources");
Dolphin.IO.File.CompleteNext();
Check(!floor.Resume(1000)&&TextureCache.Loads==1&&Dolphin.IO.File.Reads==1,"one pump adopts at most one source and waits for the remaining source");
floor.Memory.Observe(8*1024*1024,0,0,0);
Check(!floor.Resume(1000)&&Dolphin.IO.File.Reads==2,"a shared source is restored once for all matching material slots");
Dolphin.IO.File.CompleteNext();
Check(floor.Resume(1000)&&!floor.Suspended&&floor.AllBound,"the exit gate opens only after every floor source has a valid binding");
Check(Dolphin.Diagnostics.DebugOverlay.Messages.Last().Contains("owned bytes 6144 source bytes 6144"),
    "restoration reports the exact reloaded owner and source bytes");
floor.Memory.Observe(8*1024*1024,0,0,0);
Check(floor.Memory.ReservedBytes==0,"completed floor reservations retire on the next physical observation");
Check(floor.NeutralPreview&&floor.Source(0)!.Pages[0].Wrap==TextureWrapMode.Repeat&&floor.Source(1)!.Pages[0].Wrap==TextureWrapMode.Mirror,
    "restore uses the small neutral preview and preserves per-source addressing");

floor=Fixture();floor.MarkPreviewVisible();
Check(!floor.Upgrade()&&Dolphin.IO.File.Reads==1,"full ground detail begins as a byte-only optional read");
floor.Suspend();
Check(floor.Suspended&&TextureCache.Loads==0&&!Dolphin.IO.File.Requests[0].Task.IsCompleted,"suspending during a read does not wait for I/O");
Dolphin.IO.File.CompleteNext();floor.Suspend();
Check(TextureCache.Loads==0&&floor.PendingGround==false,"completed stale full-ground bytes are discarded without recreating indoor resources");
Restore(floor);
Check(floor.NeutralPreview&&floor.AllBound,"a rapid exit restores the preview rather than adopting a stale upgrade");

floor=Fixture();floor.Suspend();floor.Resume(1000);
Check(Dolphin.IO.File.Reads==1,"restoration owns one outstanding source read");
floor.Suspend();Dolphin.IO.File.CompleteNext();floor.Suspend();
Check(TextureCache.Loads==0&&floor.Suspended,"returning indoors invalidates a partially restored generation");
Restore(floor);
Check(TextureCache.Loads==2&&floor.AllBound,"a later exit restores fresh sources once after stale completion drains");

floor=Fixture();floor.Suspend();floor.Resume(1000);Dolphin.IO.File.CompleteNext();floor.Resume(1000);
Check(floor.Suspended&&GraphicsDevice.Default.NativeBytes>0,"an incomplete destination can own a restored source without becoming drawable");
floor.Suspend();
Check(floor.Suspended&&GraphicsDevice.Default.NativeBytes==0&&floor.NoBorrowers,
    "reversing after one source upload reclaims partial restoration instead of retaining it indoors");
Restore(floor);Check(floor.AllBound,"a canceled partial restore remains retryable");

floor=Fixture();floor.Memory=new VisualMemoryPressure();floor.Memory.Observe(8*1024*1024,0,0,0);floor.MarkPreviewVisible();
floor.Upgrade();Check(floor.Memory.ReservedBytes>0,"optional detail reserves its native peak before the read starts");
floor.Memory.Observe(512*1024,0,0,1);floor.Upgrade();
Check(floor.Memory.ReservedBytes==0&&floor.NeutralPreview,"pressure cancels an unadopted optional upgrade while retaining the drawable preview");
Dolphin.IO.File.CompleteNext();floor.Upgrade();
int reads=Dolphin.IO.File.Reads;
for(int repeat=0;repeat<5;repeat++)floor.Upgrade();
Check(TextureCache.Loads==0&&Dolphin.IO.File.Reads==reads,"sustained pressure causes no upload or repeated disc-read churn");

floor=Fixture();floor.Suspend();floor.Memory=new VisualMemoryPressure();floor.Memory.Observe(8*1024*1024,0,0,0);
floor.Resume(1000);Dolphin.IO.File.CompleteNext();TextureCache.FailNext=true;
Check(!floor.Resume(1000)&&floor.Suspended&&floor.Memory.ReservedBytes==0,"failed adoption cancels its reservation without publishing a partial floor");
Check(floor.Memory.UnderPressure&&floor.Memory.FailedRequestBytes>0,"owner budget/allocation rejection feeds the global pressure policy");
reads=Dolphin.IO.File.Reads;
Check(!floor.Resume(1000)&&Dolphin.IO.File.Reads==reads,"failed floor restoration backs off instead of retrying each frame");
Clock.Timestamp=VisualMemoryPressure.FailureBackoffUs+1;
floor.Memory.Observe(8*1024*1024,0,0,Clock.Timestamp);
Restore(floor);
Check(floor.AllBound&&!floor.Suspended,"floor allocation failure is recoverable after headroom and retry delay recover");

floor=Fixture();floor.MarkPreviewVisible();var old=floor.Source(0)!;
floor.Upgrade();Dolphin.IO.File.CompleteNext();
Check(floor.Upgrade()&&floor.Source(0)!=old&&old.Pages[0].Disposed&&floor.AllBound,"successful upgrade atomically rebinds materials before disposing the preview");
Check(!floor.Mips.Contains(old.Pages[0]),"upgrading explicitly drops debug mip pages borrowing the previous source");
GraphicsDevice.Default.IsFrameActive=true;bool rejected=false;
try{floor.Suspend();}catch(InvalidOperationException){rejected=true;}
Check(rejected,"floor retirement refuses to mutate native resources during a frame");

floor=Fixture();floor.EmptyStartup();floor.Memory=new VisualMemoryPressure();floor.Memory.Observe(128*1024,0,0,0);
floor.ChangeSeason(0);
Check(floor.Suspended&&TextureCache.Loads==0&&Dolphin.IO.File.Reads==0&&GraphicsDevice.Default.NativeBytes==0,
    "initial season setup creates no native floor resources outside global admission");
Check(!floor.GeometryRetired,"new season setup leaves old geometry retirement pending before new cache admission");
Check(!floor.Resume(1000)&&Dolphin.IO.File.Reads==0,"startup floor reads wait for essential admission under low headroom");
Check(floor.NeutralRepeats&&floor.OtherClamps&&floor.Tint.R==120,"async season setup still configures authored material samplers and tints");
floor.Memory.Observe(8*1024*1024,0,0,0);Restore(floor);
Check(floor.GeometryPending,"the season geometry invalidation survives every asynchronous source-read frame");
floor.PublishGeometry();floor.Suspend();Restore(floor);
Check(!floor.GeometryPending,"unchanged-season indoor floor restoration does not request another immutable terrain bake");

floor=Fixture();floor.ChangeSeason(0);
floor.Resume(1000);Dolphin.IO.File.CompleteNext();floor.Resume(1000);floor.Resume(1000);
Check(Dolphin.IO.File.Paths.Last()=="floor-spring-1.dndtex","the old generation has a season-specific source read in flight");
floor.ChangeSeason(3);reads=Dolphin.IO.File.Reads;
Check(floor.Suspended&&GraphicsDevice.Default.NativeBytes==0&&!floor.Resume(1000)&&Dolphin.IO.File.Reads==reads,
    "season override discards partial resources and waits without blocking an old source read");
Dolphin.IO.File.CompleteNext();Restore(floor);
Check(floor.Season==3&&floor.AllBound&&TextureCache.LoadedPaths.Last()=="floor-winter-1.dndtex"&&Dolphin.IO.File.MismatchedAdoptions==0,
    "bytes read for the old season cannot be adopted under the new season's source identity");

floor=Fixture();floor.Memory=new VisualMemoryPressure();floor.Memory.Observe(8*1024*1024,0,0,0);floor.MarkPreviewVisible();floor.Upgrade();
floor.ChangeSeason(3);
Check(floor.Memory.ReservedBytes==0,"season changes cancel reservations for stale optional full-ground detail");
Restore(floor);
Check(floor.NeutralPreview&&!TextureCache.LoadedPaths.Contains("floor-outdoor-neutral.dndtex"),
    "a completed stale full-ground task cannot upgrade the new season before it has presented its preview");
Console.WriteLine(checks+" floor residency checks passed using production texture lifecycle code");

namespace Dolphin.Timing {internal static class Clock {internal static long Timestamp;}}
namespace Dolphin.Diagnostics
{
    internal static class DebugOverlay
    {
        internal static readonly List<string> Messages=new List<string>();
        internal static void WriteLine(string message)=>Messages.Add(message);
    }
}
namespace Dolphin.Graphics
{
    internal sealed class GraphicsDevice
    {
        internal static readonly GraphicsDevice Default=new GraphicsDevice();
        internal bool IsFrameActive;internal int NativeBytes;
    }
    internal enum TextureWrapMode {Repeat,Mirror}
    internal enum TextureMinFilter {Linear}
    internal enum TextureMagFilter {Linear}
    internal enum TextureAddressMode {Clamp,Repeat}
    internal readonly struct SamplerState
    {
        internal readonly TextureAddressMode U,V;
        internal SamplerState(TextureAddressMode u,TextureAddressMode v){U=u;V=v;}
    }
    internal readonly struct Color32
    {
        internal readonly byte R,G,B;
        internal Color32(byte r,byte g,byte b){R=r;G=g;B=b;}
    }
    internal sealed class Texture2D
    {
        internal bool Disposed;internal int Bytes;internal TextureWrapMode Wrap;
        internal void ConfigureSampler(TextureWrapMode u,TextureWrapMode v,TextureMinFilter min,TextureMagFilter mag,int lod=0){Wrap=u;}
    }
    internal sealed class Material {internal Texture2D? Texture,SecondaryTexture;internal SamplerState Sampler;internal Color32 Color;}
}
namespace Dolphin.IO
{
    internal static class File
    {
        internal static readonly List<TaskCompletionSource<byte[]>> Requests=new List<TaskCompletionSource<byte[]>>();
        internal static readonly List<string> Paths=new List<string>();
        private static readonly Dictionary<byte[],string> sources=new Dictionary<byte[],string>();
        internal static int MismatchedAdoptions;
        internal static int Reads=>Requests.Count;
        internal static void Reset(){Requests.Clear();Paths.Clear();sources.Clear();MismatchedAdoptions=0;}
        internal static Task<byte[]> ReadAllBytesAsync(string path){var request=new TaskCompletionSource<byte[]>();Requests.Add(request);Paths.Add(path);return request.Task;}
        internal static void CompleteNext(){for(int i=0;i<Requests.Count;i++)if(!Requests[i].Task.IsCompleted){var source=BitConverter.GetBytes(Paths[i].Contains("-64")?64:256);sources.Add(source,Paths[i]);Requests[i].SetResult(source);return;}}
        internal static void ValidateIdentity(string path,byte[] source){if(sources[source]!=path){MismatchedAdoptions++;throw new InvalidDataException("A stale read was adopted under a different source");}}
    }
}
namespace AnimalTown
{
    internal static class FloorStrips
    {
        internal static int Read(byte[] data,int at)=>BitConverter.ToInt32(data,at);
        internal static int TextureSlot(byte[] data,int slot)=>data[16+slot];
        internal static void Validate(byte[] data){if(data.Length!=32||Read(data,12)!=3)throw new InvalidDataException();}
        internal static uint Tint(byte[] data,int slot)=>0x789abcff;
    }
    internal static class TerrainTiles {internal static string SeasonName(int season)=>season==0?"spring":"winter";}
    internal static class ContentPaths {internal static string Asset(string value)=>value;}
    internal static class FloorTexture {internal const int Maximum=256;}
    internal static class TownPaths {internal const int Material=7;}
    internal static class TexturePixels
    {
        internal static void Validate(byte[] source){if(source.Length!=4)throw new InvalidDataException();}
        internal static int Width(byte[] source)=>BitConverter.ToInt32(source,0);
        internal static int Height(byte[] source)=>Width(source);
    }
}
internal sealed class CachedTexture
{
    internal readonly Texture2D[] Pages;
    internal readonly string Source;
    internal CachedTexture(int bytes){Pages=new[]{new Texture2D{Bytes=bytes}};GraphicsDevice.Default.NativeBytes+=bytes;Source=TextureCache.Track(this);}
    internal int Bytes=>Pages[0].Bytes;
}
internal static class TextureCache
{
    internal static int Loads,Releases;internal static bool FailNext;
    internal static readonly List<string> LoadedPaths=new List<string>();
    private static readonly List<CachedTexture> entries=new List<CachedTexture>();
    internal static Action<CachedTexture>? BeforeRelease;
    internal static void Reset(){Loads=Releases=0;FailNext=false;BeforeRelease=null;entries.Clear();LoadedPaths.Clear();}
    internal static string Track(CachedTexture entry){entries.Add(entry);return "source-"+entries.Count+":";}
    internal static int SourceBytesContaining(string source)=>entries.Where(entry=>entry.Source.Contains(source)&&!entry.Pages[0].Disposed).Sum(entry=>entry.Bytes);
    internal static CachedTexture LoadFloor(string path,bool repeat=false,byte[]? source=null)
    {
        if(FailNext){FailNext=false;throw new OutOfMemoryException("Injected native texture rejection");}
        if(source!=null)Dolphin.IO.File.ValidateIdentity(path,source);
        Loads++;LoadedPaths.Add(path);return new CachedTexture(path.Contains("-64")?2048:4096);
    }
    internal static void Release(CachedTexture texture,bool evict=false)
    {
        BeforeRelease?.Invoke(texture);
        if(texture.Pages[0].Disposed)throw new InvalidOperationException("Released a floor lease twice");
        texture.Pages[0].Disposed=true;GraphicsDevice.Default.NativeBytes-=texture.Bytes;Releases++;
    }
}
internal sealed class GroundMipMaterials
{
    private readonly HashSet<Texture2D> borrowed=new HashSet<Texture2D>();
    internal int Borrowed=>borrowed.Count;
    internal void Add(Texture2D page)=>borrowed.Add(page);
    internal bool Contains(Texture2D page)=>borrowed.Contains(page);
    internal void Forget(Texture2D? page){if(page!=null)borrowed.Remove(page);}
}
internal sealed class PathTextureResidency {internal int Bytes;}
internal sealed partial class TerrainRenderer
{
    private readonly Material[] materials=Enumerable.Range(0,8).Select(_=>new Material()).ToArray();
    private readonly CachedTexture?[] textures=new CachedTexture?[4];
    private readonly GroundMipMaterials mipBands=new GroundMipMaterials();
    private readonly PathTextureResidency paths=new PathTextureResidency();
    private enum GroundLoad {Unloaded,Preview,Visible,Reading,Complete,Failed}
    private GroundLoad groundLoad=GroundLoad.Preview;
    private Task<byte[]>? pendingGround;
    private byte[] data=new byte[32];
    private int season=0,slots=3;
    private int MaterialRevision;
    internal TerrainRenderer()
    {
        BitConverter.GetBytes(2).CopyTo(data,4);BitConverter.GetBytes(3).CopyTo(data,12);data[16]=data[17]=0;data[18]=1;floorGeometrySeason=season;
        textures[0]=new CachedTexture(2048);textures[1]=new CachedTexture(1024);BindFloorSources();
        materials[TownPaths.Material].Texture=textures[0]!.Pages[0];
        foreach(var texture in textures)if(texture!=null)mipBands.Add(texture.Pages[0]);
        foreach(var material in materials)material.SecondaryTexture=textures[0]!.Pages[0];
        TextureCache.BeforeRelease=entry=>{
            if(materials.Any(material=>material.Texture==entry.Pages[0]||material.SecondaryTexture==entry.Pages[0])||mipBands.Contains(entry.Pages[0]))
                throw new InvalidOperationException("Disposed an owner while a material/debug page still borrowed it");
        };
    }
    private void ClearPathBindings(){foreach(var material in materials)material.SecondaryTexture=null;}
    private void ReleasePaths(){ClearPathBindings();materials[TownPaths.Material].Texture=null;paths.Bytes=0;}
    internal void Suspend()=>SuspendFloorTextures();
    internal bool Resume(long deadline)=>RestoreFloorTextures(deadline);
    internal bool Upgrade()=>AdvanceGroundTexture();
    internal void MarkPreviewVisible()=>groundLoad=GroundLoad.Visible;
    internal bool Suspended=>floorTexturesSuspended;
    internal bool NeutralPreview=>textures[0]?.Bytes==2048;
    internal bool PendingGround=>pendingGround!=null;
    internal bool AllBound=>Enumerable.Range(0,slots).All(slot=>materials[slot].Texture==textures[FloorStrips.TextureSlot(data,slot)]?.Pages[0]&&materials[slot].Texture!=null);
    internal bool NoBorrowers=>materials.All(material=>material.Texture==null&&material.SecondaryTexture==null);
    internal CachedTexture? Source(int slot)=>textures[slot];
    internal GroundMipMaterials Mips=>mipBands;
    internal byte[] Metadata=>data;
    internal int Season=>season;
    internal void EmptyStartup(){SuspendFloorTextures();season=-1;floorGeometrySeason=-1;floorTexturesSuspended=false;}
    internal void ChangeSeason(int next)=>BeginFloorSeason((byte[])data.Clone(),next);
    internal bool GeometryPending=>FloorGeometryChanged;
    internal bool GeometryRetired=>floorGeometryRetired;
    internal void PublishGeometry(){floorGeometryRetired=true;floorGeometrySeason=season;}
    internal bool NeutralRepeats=>materials[0].Sampler.U==TextureAddressMode.Repeat;
    internal bool OtherClamps=>materials[2].Sampler.U==TextureAddressMode.Clamp;
    internal Color32 Tint=>materials[0].Color;
}
