using AnimalTown;
using Dolphin.Graphics;
using Hypercube.GameCube;

// Seven retained receivers, one shared 2KiB mask. GPU UV translation moves a
// circle over cached ground triangles instead of reskinning a shadow outline.
internal sealed class VillagerShadows
{
    private sealed class Entry
    {
        internal bool Active,Ready,UvReady;
        internal int Revision;
        internal long Upload;
        internal VillagerShadowPatch Wanted,Built;
        internal readonly Material Material;
        internal DynamicMeshBuffer? Mesh;
        internal int Count;
        internal Entry(Texture2D mask)
        {Material=new Material{Texture=mask,Color=new Color32(35,49,66,82),Blend=BlendMode.Alpha,DepthWrite=false,AlphaCutoff=.004f,Lighting=LightingMode.Unlit,Cull=CullMode.Back,Sampler=new SamplerState(TextureAddressMode.Clamp,TextureAddressMode.Clamp,TextureFilter.Linear)};}
    }
    private readonly Entry[] entries=new Entry[BuildingWorld.Actors];
    private readonly GroundShadow staging=new GroundShadow();
    private readonly Transform scroll,flat;
    private readonly Texture2D mask;
    private Town? town;
    private int building=-1,jobRevision,cursor=1;
    private VillagerShadowPatch job;
    internal bool Enabled=true;
    internal VillagerShadows(Transform outdoor,Transform indoor)
    {
        scroll=outdoor;flat=indoor;mask=Texture2D.CreateNative(32,32,NativeTextureFormat.Ia8,true);mask.ReplaceNative(VillagerShadowPatch.Texture());
        for(int i=0;i<entries.Length;i++)entries[i]=new Entry(mask);
    }
    internal void BeginFrame(Town source)
    {
        if(town!=source){town=source;building=-1;for(int i=0;i<entries.Length;i++){entries[i].Ready=false;entries[i].Revision++;}}
        for(int i=0;i<entries.Length;i++)entries[i].Active=false;
    }
    internal void Set(int actor,int space,float x,float z,int size,bool entryMat)
    {
        var e=entries[actor];var p=new VillagerShadowPatch(town!,space,x,z,size,entryMat);
        bool moved=!e.UvReady||e.Wanted.X!=x||e.Wanted.Z!=z;
        if(!e.Wanted.SameRegion(p)||e.Revision==0)e.Revision++;
        e.Wanted=p;e.Active=true;
        if(moved){e.Material.TextureTransform=new UvTransform(new Vector2(p.UOffset,p.VOffset),new Vector2(VillagerShadowPatch.UvScale,VillagerShadowPatch.UvScale));e.UvReady=true;}
    }
    private bool Dirty(int i)=>entries[i].Active&&(!entries[i].Ready||!entries[i].Built.SameRegion(entries[i].Wanted));
    internal bool Advance()
    {
        if(!Enabled||town==null)return false;
        if(building>=0&&(!entries[building].Active||entries[building].Revision!=jobRevision))building=-1;
        if(building<0){
            if(Dirty(0))building=0;
            else for(int n=0;n<entries.Length-1;n++){int i=1+(cursor-1+n)%(entries.Length-1);if(Dirty(i)){building=i;break;}}
            if(building<0)return false;
            var e=entries[building];job=e.Wanted;jobRevision=e.Revision;
            if(job.Space==0)staging.Begin(town,job.Receiver,true);else staging.BeginIndoor(job.Receiver,job.Size,job.EntryMat);
        }
        if(!staging.Advance())return true;
        var target=entries[building];target.Count=staging.Overflow?0:staging.Vertices;
        if(target.Count>0){
            if(target.Mesh==null)target.Mesh=new DynamicMeshBuffer(GroundShadow.MaximumTriangles*3);
            var vertices=target.Mesh.Reserve(target.Count,FrameProfile.Counters);
            for(int i=0;i<target.Count;i++)vertices[i]=new VertexPositionColorTexture(new Vector3(staging.Positions[i*3],staging.Positions[i*3+1],staging.Positions[i*3+2]),Color32.White,new Vector2(staging.Uvs[i*2],staging.Uvs[i*2+1]));
            target.Mesh.Commit(target.Count,++target.Upload,FrameProfile.Counters);
        }
        target.Built=job;target.Ready=true;cursor=1+building%(entries.Length-1);building=-1;return true;
    }
    internal void Draw(Renderer renderer)
    {
        if(!Enabled||town==null)return;
        for(int i=0;i<entries.Length;i++){
            var e=entries[i];if(!e.Active||!e.Ready||e.Count==0||!e.Built.Covers(town,e.Wanted))continue;
            renderer.DrawMesh(e.Mesh!.Mesh!,e.Material,e.Wanted.Space==0?scroll:flat);
        }
    }
}
