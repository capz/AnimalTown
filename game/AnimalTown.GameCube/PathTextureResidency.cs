using System;
using System.Threading.Tasks;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Diagnostics;
using Hypercube.GameCube;

// One owning cache lease. All meshes borrow the one material. Disc workers
// return bytes only; native creation and retirement happen between frames.
internal sealed class PathTextureResidency : IDisposable
{
    private readonly string FileName=ContentPaths.Asset("path-dirt.dndtex");
    private CachedTexture? texture;
    private Task<byte[]>? pending;
    private int generation,readingGeneration;
    private Texture2D? bound;
    private readonly SamplerState sampler=new SamplerState(TextureAddressMode.Repeat,TextureAddressMode.Repeat);
    private bool wanted,failed;
    internal int Bytes=>texture?.Bytes??0;
    internal Texture2D? Page=>texture?.Pages[0];
    internal int Reads {get;private set;}
    internal void Prepare(bool demand,Material material,Texture2D? fallback)
    {
        if(GraphicsDevice.Default.IsFrameActive)throw new InvalidOperationException("Path residency changes require EndFrame");
        if(wanted!=demand){wanted=demand;generation++;failed=false;}
        if(!demand&&texture!=null){material.Texture=null;var old=texture;int bytes=old.Bytes;texture=null;TextureCache.Release(old,evict:true);DebugOverlay.WriteLine("PATH PAGE EVICTED bytes "+bytes);}
        if(pending!=null&&pending.IsCompleted){
            var read=pending;pending=null;
            try{
                var bytes=read.GetAwaiter().GetResult();
                if(wanted&&readingGeneration==generation){
                    TexturePixels.Validate(bytes);
                    if(TexturePixels.Width(bytes)!=64||TexturePixels.Height(bytes)!=64||bytes.Length>12*1024+32)throw new InvalidOperationException("Invalid painted path page");
                    texture=TextureCache.LoadFloor(FileName,repeat:true,source:bytes);
                    DebugOverlay.WriteLine("PATH PAGE READY bytes "+texture.Bytes+" reads "+Reads);
                }
            }catch(Exception error){if(wanted&&readingGeneration==generation){failed=true;DebugOverlay.WriteLine("PATH PAGE FAILED using dirt tint: "+error.Message);}}
        }
        if(wanted&&texture==null&&pending==null&&!failed){
            try{readingGeneration=generation;pending=Dolphin.IO.File.ReadAllBytesAsync(FileName);Reads++;}
            catch(Exception error){failed=true;DebugOverlay.WriteLine("PATH READ FAILED using dirt tint: "+error.Message);}
        }
        material.Texture=texture?.Pages[0]??fallback;
        material.Color=texture==null?new Color32(154,128,93):Color32.White;
        material.Sampler=sampler;
        if(texture!=null&&bound!=texture.Pages[0]){var page=texture.Pages[0];page.ConfigureSampler(TextureWrapMode.Repeat,TextureWrapMode.Repeat,TextureMinFilter.Linear,TextureMagFilter.Linear,0);}
        bound=texture?.Pages[0];
    }
    public void Dispose()
    {
        if(pending!=null){try{pending.GetAwaiter().GetResult();}catch(Exception){}pending=null;}
        if(texture!=null){TextureCache.Release(texture,evict:true);texture=null;}
    }
}
