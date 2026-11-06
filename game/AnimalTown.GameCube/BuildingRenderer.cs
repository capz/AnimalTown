using System;
using AnimalTown;
using Dolphin.Graphics;

// All instances share seven tiny static parts per definition. No room mesh,
// texture or pose is allocated on entry or per frame. Never load invisible NPC
// rooms. The initial kit remains resident within an explicit combined ceiling.
internal sealed class BuildingRenderer
{
    private readonly Model?[] models=new Model?[BuildingCatalog.MaximumDefinitions*7];
    private readonly ModelInstance?[] parts=new ModelInstance?[BuildingCatalog.MaximumDefinitions*7];
    private readonly Transform[] roots=new Transform[BuildingWorld.Count],poses=new Transform[32];
    private readonly bool[] ready=new bool[BuildingCatalog.MaximumDefinitions];
    private int used;
    internal int ResidentBytes {get;private set;}
    internal BuildingRenderer(Transform world)
    {
        for(int i=0;i<roots.Length;i++)roots[i]=new Transform{Parent=world};for(int i=0;i<poses.Length;i++)poses[i]=new Transform{Parent=world};
        for(int type=0;type<BuildingCatalog.Count;type++){
            var d=BuildingCatalog.Get(type);string[] names={d.Exterior,d.Door,d.Floor,d.North,d.East,d.South,d.West};int before=ResidentBytes;
            try{for(int i=0;i<7;i++){int slot=type*7+i;models[slot]=Model.LoadAsync(TexturePolicy.ModelPath(names[i],TextureResolution.Size128),default,256*1024).GetAwaiter().GetResult();parts[slot]=models[slot]!.CreateInstance(16*1024);ResidentBytes+=models[slot]!.NativeBytes+parts[slot]!.NativeBytes;if(ResidentBytes>768*1024)throw new InvalidOperationException("Building resident memory budget");}ready[type]=true;}
            catch(Exception error){for(int i=0;i<7;i++){int slot=type*7+i;parts[slot]?.Dispose();parts[slot]=null;models[slot]?.Dispose();models[slot]=null;}ResidentBytes=before;Dolphin.Diagnostics.DebugOverlay.WriteLine("BUILDING ASSET ERROR "+d.Id+": "+error.Message);}
        }
    }
    internal void Begin(PortalSystem doors){used=0;for(int i=0;i<ready.Length;i++)doors.Ready[i]=ready[i];}
    internal bool Ready(int type)=>type>=0&&type<ready.Length&&ready[type];
    private void Part(Renderer renderer,int type,int part,Transform parent,Vector3 position,Vector3 scale,float yaw=0)
    {
        if(!Ready(type))return;if(used==poses.Length)throw new InvalidOperationException("Building draw budget");var pose=poses[used++];pose.Parent=parent;pose.LocalPosition=position;pose.LocalScale=scale;pose.LocalRotation=new Quaternion(0,(float)Math.Sin(yaw*.5f),0,(float)Math.Cos(yaw*.5f));renderer.DrawModel(parts[type*7+part]!,pose);
    }
    internal void Exterior(Renderer renderer,GameState state,int building,PortalSystem doors)
    {
        if(!BuildingWorld.Exists(state,building))return;int type=state.BuildingTypes[building];if(!Ready(type))return;
        BuildingWorld.Origin(state,building,out float x,out float z);float h=TerrainElevation.At(state.Town,x+1.5f,z+1.5f);var p=CylinderWorld.Surface(z,state.Z,h);
        if(!RenderView.Sphere(x+1.5f,p.Y+1.6f,p.Z+1.5f,3.5f,state.X,state.Z,TerrainElevation.At(state.Town,state.X,state.Z)))return;
        var root=roots[building];root.LocalPosition=new Vector3(x,p.Y,p.Z);float a=(z-state.Z)/CylinderWorld.Radius*.5f;root.LocalRotation=new Quaternion((float)Math.Sin(a),0,0,(float)Math.Cos(a));root.LocalScale=new Vector3(1,1,1);
        Part(renderer,type,0,root,Vector3.Zero,new Vector3(1,1,1));Part(renderer,type,1,root,new Vector3(BuildingWorld.Definition(state,building).HingeX,0,BuildingWorld.Definition(state,building).HingeZ),new Vector3(1,1,1),-doors.Openness(building)*1.4f);
    }
    internal void Interior(Renderer renderer,GameState state,Transform world,InteriorCamera camera,PortalSystem doors)
    {
        int building=BuildingWorld.Building(state.Space),type=state.BuildingTypes[building];float size=BuildingWorld.Size(state,state.Space),scale=size/4;
        Part(renderer,type,2,world,Vector3.Zero,new Vector3(scale,1,scale));
        for(int wall=0;wall<4;wall++)if(camera.WallVisible(wall))Part(renderer,type,3+wall,world,Vector3.Zero,new Vector3(scale,1,scale));
        // Keep the doorway legible even when the front wall is cut away.
        if(camera.WallVisible(2)||doors.Openness(building)>0)Part(renderer,type,1,world,new Vector3(size*.5f-.55f*scale,0,size+.01f),new Vector3(scale,1,1),doors.Openness(building)*1.4f);
    }
}
