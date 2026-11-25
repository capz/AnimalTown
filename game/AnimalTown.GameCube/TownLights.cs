using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Diagnostics;

// Ground pools remain baked. Model points change only with their layout or
// the cylinder focus; unlit views need no point projection or native upload.
internal sealed class TownLights
{
    private readonly float[] sources=new float[24*4];
    private readonly float[] heights=new float[24],points=new float[24*5];
    internal readonly float[] X=new float[6],Z=new float[6];
    internal int Count {get;private set;}
    internal int ProjectedSources {get;private set;}
    private Town? town;
    private int revision=-1,groundRevision=-1,objectRevision=-1,sourceCount;
    private readonly float[] buildingX=new float[BuildingWorld.Count],buildingZ=new float[BuildingWorld.Count];
    private readonly bool[] buildingExists=new bool[BuildingWorld.Count];
    private readonly BuildingDefinition?[] definitions=new BuildingDefinition?[BuildingWorld.Count];
    private bool pointsReady;
    private float projectedZ;
    private void Add(float x,float z,float radius,float strength,float height)
    {heights[sourceCount]=height;int at=sourceCount++*4;sources[at]=x;sources[at+1]=z;sources[at+2]=radius;sources[at+3]=strength;}
    internal void Prepare(GameState state,int strength)
    {
        bool layoutChanged=town!=state.Town||revision!=state.Town.TerrainVersion||groundRevision!=state.Town.ShadowVersion||objectRevision!=state.Town.ObjectVersion||!LayoutMatches(state,false);
        if(layoutChanged){
            sourceCount=Count=0;
            for(int i=0;i<BuildingWorld.Count;i++)if(BuildingWorld.Exists(state,i)){
                BuildingWorld.Entrance(state,i,out float x,out float z);Add(x,z+.5f,2.8f,210,1.3f);
                if(WorldLighting.StreetLamp(state,i,out x,out z)){
                    X[Count]=x;Z[Count++]=z;Add(x,z,2.7f,255,2.3f);
                }
            }
        }
        if(NativeShadows.Lights(layoutChanged?sources:null,layoutChanged?sourceCount:0,strength)!=1)throw new InvalidOperationException("Static light map failed");
        if(layoutChanged){
            town=state.Town;revision=town.TerrainVersion;groundRevision=town.ShadowVersion;objectRevision=town.ObjectVersion;
            LayoutMatches(state,true);pointsReady=false;
            DebugOverlay.WriteLine("ANIMALTOWN LIGHTS READY sources "+sourceCount+" street lamps "+Count+" map bytes "+(160*1024*2+256*128*2));
        }
        // Native model lighting also gates on this gain. Retain the static
        // source list, and republish points before the next lit frame.
        if(strength==0){pointsReady=false;return;}
        if(pointsReady&&projectedZ==state.Z)return;
        for(int i=0;i<sourceCount;i++){
            int s=i*4,p=i*5;float x=sources[s],z=sources[s+1];
            var pose=CylinderWorld.Surface(z,state.Z,TerrainElevation.At(state.Town,x,z)+heights[i]);
            points[p]=x;points[p+1]=pose.Y;points[p+2]=pose.Z;
            points[p+3]=sources[s+2]*1.65f;points[p+4]=sources[s+3];
        }
        ProjectedSources+=sourceCount;
        if(NativeShadows.Points(points,sourceCount)!=1)throw new InvalidOperationException("Model light sources failed");
        projectedZ=state.Z;pointsReady=true;
    }
    private bool LayoutMatches(GameState state,bool capture)
    {
        bool same=true;
        for(int i=0;i<BuildingWorld.Count;i++){
            bool exists=BuildingWorld.Exists(state,i);float x=0,z=0;
            if(exists)BuildingWorld.Origin(state,i,out x,out z);
            var definition=exists?BuildingWorld.Definition(state,i):null;
            if(buildingExists[i]!=exists||buildingX[i]!=x||buildingZ[i]!=z||definitions[i]!=definition)same=false;
            if(capture){buildingExists[i]=exists;buildingX[i]=x;buildingZ[i]=z;definitions[i]=definition;}
        }
        return same;
    }
    internal static Color32 Tint(Color32 color,int rgb)=>new Color32((byte)(color.R*(rgb>>16)/255),(byte)(color.G*(rgb>>8&255)/255),(byte)(color.B*(rgb&255)/255),color.A);
}
