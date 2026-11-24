using System;
using System.Collections.Generic;
using AnimalTown;
using Dolphin.Graphics;

// Opaque vertex colors on shared static geometry. Poses are the only things
// that change once the pad and helicopter have been admitted to the scene.
internal sealed partial class HelipadRenderer : IDisposable
{
    private static readonly Color32 cream=new Color32(248,225,170),mint=new Color32(99,172,147),
        blue=new Color32(107,181,209),gold=new Color32(229,170,63),steel=new Color32(88,103,115),
        ivory=new Color32(246,225,177);
    private readonly Material material=new Material{Cull=CullMode.None,Lighting=LightingMode.Unlit};
    private readonly Transform padPose=new Transform(),craftPose=new Transform();
    private readonly Transform doorPose,rotorPose,tailRotorPose;
    private Mesh? pad,body,door,rotor,tailRotor,cloud;

    internal HelipadRenderer()
    {
        doorPose=new Transform{Parent=craftPose};
        rotorPose=new Transform{Parent=craftPose,LocalPosition=new Vector3(-.08f,1.53f,0)};
        tailRotorPose=new Transform{Parent=craftPose,LocalPosition=new Vector3(-1.65f,.83f,.14f)};
    }

    internal void PreparePad()
    {
        if(pad!=null)return;
        var g=new Geometry();
        AddDeck(g);
        // The windsock is beside the painted pad.
        g.Box(-2.62f,0,-1.34f,.07f,1.56f,.07f,new Color32(124,88,55));
        var red=new Color32(205,98,72);
        g.Box(-2.55f,1.34f,-1.43f,.18f,.2f,.24f,red);
        g.Box(-2.37f,1.34f,-1.42f,.19f,.17f,.22f,ivory);
        g.Box(-2.18f,1.33f,-1.4f,.24f,.14f,.18f,red);
        pad=g.Mesh();
    }

    internal void PrepareCraft()
    {
        if(body!=null)return;
        var g=new Geometry();
        g.Oval(0,.81f,0,.88f,.63f,.56f,cream,10,8,true);
        g.Oval(-1.01f,.79f,0,.78f,.152f,.17f,mint,8,4);
        g.Box(-1.59f,.79f,.125f,.68f,.07f,.02f,gold);
        g.Quad(new Vector3(-1.74f,.82f,0),new Vector3(-1.69f,1.12f,0),
            new Vector3(-1.5f,1.09f,0),new Vector3(-1.34f,.82f,0),cream);
        g.Oval(-.08f,1.45f,0,.155f,.11f,.155f,cream,8,3);
        for(int side=-1;side<=1;side+=2){
            float z=side*.405f;
            g.Oval(.01f,.065f,z,.91f,.07f,.072f,steel,8,4);
            g.Box(-.48f,.093f,z-.036f,.077f,.26f,.073f,steel);
            g.Box(.43f,.093f,z-.036f,.077f,.26f,.073f,steel);
        }
        g.Box(-.46f,.277f,.42f,.50f,.061f,.25f,steel);
        g.Box(-.40f,.337f,.487f,.38f,.014f,.14f,ivory);
        // The sliding door reveals the dark entrance against the mint sill.
        g.Box(-.43f,.46f,.532f,.49f,.52f,.024f,new Color32(61,79,80));
        body=g.Mesh();
        g=new Geometry();
        g.Box(-.43f,.46f,.54f,.49f,.53f,.038f,cream);
        g.Box(-.375f,.70f,.575f,.38f,.232f,.018f,blue);
        g.Box(-.173f,.59f,.576f,.103f,.035f,.023f,steel);
        door=g.Mesh();
        g=new Geometry();
        var rotorColor=new Color32(80,84,97);
        g.Box(-1.55f,-.021f,-.072f,3.1f,.042f,.144f,rotorColor);
        g.Box(-.072f,-.019f,-1.55f,.144f,.04f,3.1f,rotorColor);
        g.Oval(0,0,0,.144f,.058f,.144f,cream,8,3);
        rotor=g.Mesh();
        g=new Geometry();
        g.Box(-.226f,-.026f,0,.452f,.052f,.033f,rotorColor);
        g.Box(-.026f,-.226f,0,.052f,.452f,.033f,rotorColor);
        tailRotor=g.Mesh();
    }

    internal void PrepareCloud()
    {
        if(cloud!=null)return;
        var g=new Geometry();var white=new Color32(235,246,242);
        g.Oval(-.77f,0,0,.90f,.38f,.44f,white,8,4);
        g.Oval(-.05f,.14f,0,1.08f,.65f,.52f,white,8,4);
        g.Oval(.87f,-.025f,0,.85f,.38f,.46f,white,8,4);
        cloud=g.Mesh();
    }

    internal void DrawPad(Renderer renderer,Vector3 position,Quaternion rotation,int tint)
    {
        material.Color=TownLights.Tint(Color32.White,tint);
        padPose.LocalPosition=position;padPose.LocalRotation=rotation;
        renderer.DrawMesh(pad!,material,padPose);
    }

    internal void DrawCraft(Renderer renderer,Vector3 position,Quaternion rotation,float doorOpen,float angle,int tint)
    {
        material.Color=TownLights.Tint(Color32.White,tint);
        craftPose.LocalPosition=position;craftPose.LocalRotation=rotation;
        doorPose.LocalPosition=new Vector3(-.47f*doorOpen,0,.024f*doorOpen);
        rotorPose.LocalRotation=new Quaternion(0,(float)Math.Sin(angle*.5f),0,(float)Math.Cos(angle*.5f));
        tailRotorPose.LocalRotation=new Quaternion(0,0,(float)Math.Sin(angle),(float)Math.Cos(angle));
        renderer.DrawMesh(body!,material,craftPose);
        renderer.DrawMesh(door!,material,doorPose);
        renderer.DrawMesh(rotor!,material,rotorPose);
        renderer.DrawMesh(tailRotor!,material,tailRotorPose);
    }

    internal void DrawCloud(Renderer renderer,float x,float y,float z)
    {
        material.Color=Color32.White;padPose.LocalPosition=new Vector3(x,y,z);
        renderer.DrawMesh(cloud!,material,padPose);
    }

    public void Dispose()
    {
        pad?.Dispose();body?.Dispose();door?.Dispose();rotor?.Dispose();tailRotor?.Dispose();cloud?.Dispose();
    }

    private sealed class Geometry
    {
        private readonly List<VertexPositionColor> vertices=new List<VertexPositionColor>();
        private readonly List<ushort> indices=new List<ushort>();
        internal Mesh Mesh()=>Dolphin.Graphics.Mesh.Create(vertices.ToArray(),indices.ToArray());
        internal void Quad(Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color32 color)
        {
            int n=vertices.Count;
            vertices.Add(new VertexPositionColor(a,color));vertices.Add(new VertexPositionColor(b,color));
            vertices.Add(new VertexPositionColor(c,color));vertices.Add(new VertexPositionColor(d,color));
            indices.Add((ushort)n);indices.Add((ushort)(n+1));indices.Add((ushort)(n+2));
            indices.Add((ushort)n);indices.Add((ushort)(n+2));indices.Add((ushort)(n+3));
        }
        internal void DeckQuad(int left,int top,int right,int bottom,Color32 color)
        {
            const float height=HelipadSite.DeckHeight;
            const float halfWidth=HelipadSite.Width*.5f,halfDepth=HelipadSite.Depth*.5f;
            float x=left/100f-halfWidth,z=top/100f-halfDepth;
            float endX=right/100f-halfWidth,endZ=bottom/100f-halfDepth;
            Quad(new Vector3(x,height,z),new Vector3(endX,height,z),
                new Vector3(endX,height,endZ),new Vector3(x,height,endZ),color);
        }
        internal void Box(float x,float y,float z,float width,float height,float depth,Color32 color)
        {
            float r=x+width,t=y+height,b=z+depth;
            var a=new Vector3(x,y,z);var c=new Vector3(r,y,z);var d=new Vector3(r,y,b);var e=new Vector3(x,y,b);
            var f=new Vector3(x,t,z);var g=new Vector3(r,t,z);var h=new Vector3(r,t,b);var i=new Vector3(x,t,b);
            Quad(f,g,h,i,color);Quad(a,e,d,c,Shade(color,159));
            Quad(a,c,g,f,Shade(color,204));Quad(c,d,h,g,Shade(color,223));
            Quad(d,e,i,h,Shade(color,236));Quad(e,a,f,i,Shade(color,196));
        }
        internal void Oval(float x,float y,float z,float rx,float ry,float rz,Color32 color,int sides,int bands,bool cabin=false)
        {
            for(int band=0;band<bands;band++){
                float a0=-1.5707963f+band*3.1415927f/bands,a1=-1.5707963f+(band+1)*3.1415927f/bands;
                float r0=(float)Math.Cos(a0),r1=(float)Math.Cos(a1),h0=y+(float)Math.Sin(a0)*ry,h1=y+(float)Math.Sin(a1)*ry;
                for(int side=0;side<sides;side++){
                    float b0=side*6.2831853f/sides,b1=(side+1)*6.2831853f/sides;
                    float cx0=(float)Math.Cos(b0)*rx,cx1=(float)Math.Cos(b1)*rx,sz0=(float)Math.Sin(b0)*rz,sz1=(float)Math.Sin(b1)*rz;
                    var paint=color;
                    if(cabin){
                        if(band<=2)paint=mint;
                        else if(band>=4&&band<=6&&cx0+cx1>.44f)paint=blue;
                    }
                    var shaded=Shade(paint,Math.Min(255,183+band*48/bands+(cx0+cx1>0?20:0)+(sz0+sz1>0?13:0)));
                    Quad(new Vector3(x+cx0*r0,h0,z+sz0*r0),new Vector3(x+cx1*r0,h0,z+sz1*r0),
                        new Vector3(x+cx1*r1,h1,z+sz1*r1),new Vector3(x+cx0*r1,h1,z+sz0*r1),shaded);
                }
            }
        }
        private static Color32 Shade(Color32 color,int amount)
            =>new Color32((byte)(color.R*amount/255),(byte)(color.G*amount/255),(byte)(color.B*amount/255));
    }
}
