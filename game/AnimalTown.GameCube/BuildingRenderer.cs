using System;
using AnimalTown;
using Dolphin.Graphics;

// All instances share eight tiny static parts per definition. No room mesh,
// texture or pose is allocated on entry or per frame. Never load invisible NPC
// rooms. The initial kit remains resident within an explicit combined ceiling.
internal sealed class BuildingRenderer : IDisposable
{
    private readonly Model?[] models=new Model?[BuildingCatalog.MaximumDefinitions*8];
    private readonly ModelInstance?[] parts=new ModelInstance?[BuildingCatalog.MaximumDefinitions*8];
    private readonly Transform[] roots=new Transform[BuildingWorld.Count],poses=new Transform[32];
    private readonly bool[] ready=new bool[BuildingCatalog.MaximumDefinitions];
    private int used;
    internal int ResidentBytes {get;private set;}
    internal BuildingRenderer(Transform world)
    {
        for(int i=0;i<roots.Length;i++)roots[i]=new Transform{Parent=world};for(int i=0;i<poses.Length;i++)poses[i]=new Transform{Parent=world};
    }
    private System.Threading.Tasks.Task<Model>? pending;
    private int loadType,loadPart,typeStartBytes;
    internal int LoadedParts=>loadType*8+loadPart;
    internal string LoadingLabel {get;private set;}="Loading buildings";
    public void Dispose()
    {
        // A completed async load owns a native model even before AdvanceLoading
        // adopts it. Drain it outside a frame so disc work cannot outlive a draft.
        Model? unadopted=null;
        if(pending!=null){try{unadopted=pending.GetAwaiter().GetResult();}catch(Exception){if(!pending.IsCompleted)throw;}pending=null;}
        for(int i=0;i<parts.Length;i++){parts[i]?.Dispose();parts[i]=null;}
        unadopted?.Dispose();
        for(int i=0;i<models.Length;i++){models[i]?.Dispose();models[i]=null;}
        Array.Clear(ready,0,ready.Length);ResidentBytes=0;loadType=BuildingCatalog.Count;loadPart=used=0;
    }
    internal bool AdvanceLoading()
    {
        if(loadType>=BuildingCatalog.Count)return true;
        var d=BuildingCatalog.Get(loadType);
        try{
            if(pending==null){
                string name=loadPart==0?d.Exterior:loadPart==1?d.Door:loadPart==2?d.Floor:loadPart==3?d.North:loadPart==4?d.East:loadPart==5?d.South:loadPart==6?d.West:d.Exterior+"-closed";
                LoadingLabel="Loading "+name;pending=Model.LoadAsync(TexturePolicy.ModelPath(name,TextureResolution.Size128),default,256*1024);return false;
            }
            if(!pending.IsCompleted)return false;
            int slot=loadType*8+loadPart;models[slot]=pending.GetAwaiter().GetResult();
            if(NativeBuildings.Retain(models[slot]!)!=1)throw new InvalidOperationException("Static building geometry retention failed");
            parts[slot]=models[slot]!.CreateInstance(16*1024);
            ResidentBytes+=models[slot]!.NativeBytes+parts[slot]!.NativeBytes;
            if(ResidentBytes>768*1024)throw new InvalidOperationException("Building resident memory budget");
            loadPart++;if(loadPart==8)ready[loadType]=true;
        }catch(Exception error){
            for(int i=0;i<8;i++){int slot=loadType*8+i;parts[slot]?.Dispose();parts[slot]=null;models[slot]?.Dispose();models[slot]=null;}
            ResidentBytes=typeStartBytes;loadPart=8;Dolphin.Diagnostics.DebugOverlay.WriteLine("BUILDING ASSET ERROR "+d.Id+": "+error.Message);
        }
        pending=null;if(loadPart==8){loadType++;loadPart=0;typeStartBytes=ResidentBytes;}
        return loadType>=BuildingCatalog.Count;
    }
    internal void Begin(PortalSystem doors){used=0;for(int i=0;i<ready.Length;i++)doors.Ready[i]=ready[i];}
    internal bool Ready(int type)=>type>=0&&type<ready.Length&&ready[type];
    private void Part(Renderer renderer,int type,int part,Transform parent,Vector3 position,Vector3 scale,float yaw=0)
    {
        if(!Ready(type))return;if(used==poses.Length)throw new InvalidOperationException("Building draw budget");var pose=poses[used++];pose.Parent=parent;pose.LocalPosition=position;pose.LocalScale=scale;pose.LocalRotation=new Quaternion(0,(float)Math.Sin(yaw*.5f),0,(float)Math.Cos(yaw*.5f));renderer.DrawModel(parts[type*8+part]!,pose);
    }
    internal void Exterior(Renderer renderer,GameState state,int building,PortalSystem doors)
    {
        if(!BuildingWorld.Exists(state,building))return;int type=state.BuildingTypes[building];if(!Ready(type))return;
        BuildingWorld.Origin(state,building,out float x,out float z);float h=TerrainElevation.At(state.Town,x+1.5f,z+1.5f);var p=CylinderWorld.Surface(z,state.Z,h);
        if(!RenderView.Sphere(x+1.5f,p.Y+1.6f,p.Z+1.5f,3.5f,state.X,state.Z,TerrainElevation.At(state.Town,state.X,state.Z)))return;
        var root=roots[building];root.LocalPosition=new Vector3(x,p.Y,p.Z);float a=(z-state.Z)/CylinderWorld.Radius*.5f;root.LocalRotation=new Quaternion((float)Math.Sin(a),0,0,(float)Math.Cos(a));root.LocalScale=new Vector3(1,1,1);
        if(doors.Openness(building)==0){Part(renderer,type,7,root,Vector3.Zero,new Vector3(1,1,1));return;}
        Part(renderer,type,0,root,Vector3.Zero,new Vector3(1,1,1));Part(renderer,type,1,root,new Vector3(BuildingWorld.Definition(state,building).HingeX,0,BuildingWorld.Definition(state,building).HingeZ),new Vector3(1,1,1),-doors.Openness(building)*1.4f);
    }
    // Uses the already resident closed cottage. The ghost never places a
    // building, changes collision or allocates a second model/texture set.
    internal void PreviewHome(Renderer renderer,GameState state,int plotX,int plotZ)
    {
        int building=BuildingWorld.PlayerBuilding(state.ActiveProfile),type=state.BuildingTypes[building];
        if(!Ready(type))return;float x=plotX+2,z=plotZ+1;
        var p=CylinderWorld.Surface(z,state.Z,TerrainElevation.At(state.Town,x+1.5f,z+1.5f));
        var root=roots[building];root.LocalPosition=new Vector3(x,p.Y,p.Z);root.LocalScale=new Vector3(1,1,1);
        float a=(z-state.Z)/CylinderWorld.Radius*.5f;root.LocalRotation=new Quaternion((float)Math.Sin(a),0,0,(float)Math.Cos(a));
        Part(renderer,type,7,root,Vector3.Zero,new Vector3(1,1,1));
    }
    internal void Interior(Renderer renderer,GameState state,Transform world,InteriorCamera camera,PortalSystem doors)
    {
        int building=BuildingWorld.Building(state.Space),type=state.BuildingTypes[building];float size=BuildingWorld.Size(state,state.Space),scale=size/4;
        NativeShadows.Scope(2);
        try{Part(renderer,type,2,world,Vector3.Zero,new Vector3(scale,1,scale));}finally{NativeShadows.Scope(0);}
        for(int wall=0;wall<4;wall++)if(camera.WallVisible(wall))Part(renderer,type,3+wall,world,Vector3.Zero,new Vector3(scale,1,scale));
        // Keep the doorway legible even when the front wall is cut away.
        if(camera.WallVisible(2)||doors.Openness(building)>0)Part(renderer,type,1,world,new Vector3(size*.5f-.55f*scale,0,size+.01f),new Vector3(scale,1,1),doors.Openness(building)*1.4f);
    }
}
