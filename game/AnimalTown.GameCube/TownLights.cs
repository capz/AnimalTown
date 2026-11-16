using System;
using AnimalTown;
using Dolphin.Graphics;
using Dolphin.Diagnostics;

// Fixed sources share the shadow-map texels. No light textures or meshes.
internal sealed class TownLights
{
    private readonly float[] sources=new float[24*4];
    internal readonly float[] X=new float[6],Z=new float[6];
    internal int Count {get;private set;}
    private Town? town;
    private int revision=-1,layout=-1,sourceCount;
    private void Add(float x,float z,float radius,float strength)
    {int at=sourceCount++*4;sources[at]=x;sources[at+1]=z;sources[at+2]=radius;sources[at+3]=strength;}
    internal void Prepare(GameState state,int strength)
    {
        int stamp=0;
        for(int i=0;i<BuildingWorld.Count;i++){BuildingWorld.Origin(state,i,out float x,out float z);stamp=unchecked(stamp*31+(int)x*397+(int)z+(BuildingWorld.Exists(state,i)?1:0));}
        if(town!=state.Town||revision!=state.Town.TerrainVersion||layout!=stamp){
            town=state.Town;revision=town.TerrainVersion;layout=stamp;sourceCount=Count=0;
            for(int i=0;i<BuildingWorld.Count;i++)if(BuildingWorld.Exists(state,i)){
                BuildingWorld.Entrance(state,i,out float x,out float z);Add(x,z+.5f,2.8f,210);
                if(WorldLighting.StreetLamp(state,i,out x,out z)){
                    X[Count]=x;Z[Count++]=z;Add(x,z,2.7f,255);
                }
            }
            DebugOverlay.WriteLine("ANIMALTOWN LIGHTS READY sources "+sourceCount+" street lamps "+Count+" map bytes "+(160*1024*2+256*128*2));
        }
        if(NativeShadows.Lights(sources,sourceCount,strength)!=1)throw new InvalidOperationException("Static light map failed");
    }
    internal static Color32 Tint(Color32 color,int rgb)=>new Color32((byte)(color.R*(rgb>>16)/255),(byte)(color.G*(rgb>>8&255)/255),(byte)(color.B*(rgb&255)/255),color.A);
}
