using Hypercube;
internal static class GraphicsMemory
{
    internal static int Bytes,Allocations,Failures,FailAfter=-1;
    internal static void Allocate(int bytes)
    {
        if(FailAfter==0){FailAfter=-1;Failures++;throw new OutOfMemoryException();}
        if(FailAfter>0)FailAfter--;
        Bytes+=bytes;Allocations++;
    }
    internal static void Free(int bytes){Bytes-=bytes;if(Bytes<0)throw new Exception("Native owner double release");}
}
internal static class File
{
    internal static byte[] ReadAllBytes(string path)
    {
        if(!path.Contains("dropped-"))throw new IOException("Unexpected fixture asset");
        var bytes=new byte[11008];int[] header={0x58544e44,2,5,64,64,10976,7,0};
        for(int i=0;i<header.Length;i++)for(int b=0;b<4;b++)bytes[i*4+b]=(byte)(header[i]>>(b*8));return bytes;
    }
}
namespace Dolphin.Timing { internal static class Clock { internal static long Timestamp; } }
namespace Dolphin.Diagnostics { internal static class DebugOverlay { internal static void WriteLine(string message){} } }
namespace Dolphin.Graphics
{
    internal readonly struct Vector2 { public readonly float X,Y;public Vector2(float x,float y){X=x;Y=y;} }
    internal readonly struct Vector3 { public readonly float X,Y,Z;public static Vector3 Zero=>default;public Vector3(float x,float y,float z){X=x;Y=y;Z=z;} }
    internal readonly struct Quaternion { public Quaternion(float x,float y,float z,float w){} }
    internal readonly struct Color32
    {
        public readonly byte R,G,B,A;public static Color32 White=>new Color32(255,255,255);
        public Color32(byte r,byte g,byte b,byte a=255){R=r;G=g;B=b;A=a;}
    }
    internal readonly struct VertexPositionColorTexture
    {
        public readonly Vector3 Position;public readonly Color32 Color;public readonly Vector2 TextureCoordinate;
        public VertexPositionColorTexture(Vector3 position,Color32 color,Vector2 uv){Position=position;Color=color;TextureCoordinate=uv;}
    }
    internal sealed class Transform { public Transform? Parent;public Vector3 LocalPosition,LocalScale;public Quaternion LocalRotation; }
    internal enum CullMode { Back }internal enum LightingMode { Unlit }internal enum BlendMode { Alpha }
    internal enum TextureWrapMode { Clamp,Repeat }internal enum TextureMinFilter { LinearMipmapLinear }internal enum TextureMagFilter { Linear }
    internal sealed class Material
    {
        public CullMode Cull;public LightingMode Lighting;public BlendMode Blend;public Color32 Color;
        public bool DepthTest,DepthWrite;public float AlphaCutoff;public Texture2D? Texture;
    }
    internal class Mesh : IDisposable
    {
        internal readonly int NativeBytes;internal bool Disposed;
        protected Mesh(int bytes){GraphicsMemory.Allocate(bytes);NativeBytes=bytes;}
        public void Dispose(){if(Disposed)throw new Exception("Mesh disposed twice");Disposed=true;GraphicsMemory.Free(NativeBytes);}
    }
    internal sealed class DynamicMesh : Mesh
    {
        private DynamicMesh(int bytes):base(bytes){}
        internal static DynamicMesh Create(VertexPositionColorTexture[] vertices)=>new DynamicMesh(vertices.Length*36);
        internal void SetVertices(VertexPositionColorTexture[] vertices){if(Disposed)throw new Exception("Disposed mesh upload");}
    }
    internal sealed class Texture2D : IDisposable
    {
        internal readonly int NativeBytes;private bool disposed;
        private Texture2D(int bytes){GraphicsMemory.Allocate(bytes);NativeBytes=bytes;}
        internal static Texture2D FromPackage(byte[] package,bool linear)=>new Texture2D(package.Length-32);
        internal void ConfigureSampler(TextureWrapMode x,TextureWrapMode y,TextureMinFilter min,TextureMagFilter mag){}
        public void Dispose(){if(disposed)throw new Exception("Texture disposed twice");disposed=true;GraphicsMemory.Free(NativeBytes);}
    }
    internal sealed class Renderer
    {
        internal int Draws;
        internal void DrawMesh(Mesh mesh,Material material,Transform transform){if(mesh.Disposed)throw new Exception("Disposed mesh draw");Draws++;}
        internal void DrawMeshes(Mesh[] meshes,Transform[] transforms,Material material,int first,int count){for(int i=first;i<first+count;i++)DrawMesh(meshes[i],material,transforms[i]);}
    }
}
namespace Hypercube.GameCube
{
    internal sealed class DynamicMeshBuffer : IDisposable
    {
        private Dolphin.Graphics.VertexPositionColorTexture[] vertices=Array.Empty<Dolphin.Graphics.VertexPositionColorTexture>();
        private Dolphin.Graphics.DynamicMesh? mesh;private int uploaded;
        internal int Capacity=>vertices.Length;internal int Count;
        internal Dolphin.Graphics.Mesh? Mesh=>Count==0?null:mesh;
        private readonly int maximum;
        internal DynamicMeshBuffer(int maximum){this.maximum=maximum;}
        internal Dolphin.Graphics.VertexPositionColorTexture[] Reserve(int required)
        {int next=BufferGrowth.Next(Capacity,required,maximum);if(next!=Capacity)vertices=new Dolphin.Graphics.VertexPositionColorTexture[next];return vertices;}
        internal void Commit(int count,long revision)
        {
            if(uploaded!=Capacity){var replacement=Dolphin.Graphics.DynamicMesh.Create(vertices);mesh?.Dispose();mesh=replacement;uploaded=Capacity;}
            else mesh!.SetVertices(vertices);
            Count=count;
        }
        public void Dispose(){mesh?.Dispose();mesh=null;Count=0;}
    }
}
