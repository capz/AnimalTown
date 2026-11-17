using AnimalTown;
using Dolphin.Graphics;

internal static class TreeRenderingChecks
{
    private sealed class TestMesh(int token) : Mesh(token) { }

    internal static void Run(Action<bool,string> check)
    {
        int[] triangles={84,68,60};
        check(LowPolyTree.KindCount==triangles.Length,"trees retain three shared mesh kinds");
        for(int kind=0;kind<LowPolyTree.KindCount;kind++){
            var tree=LowPolyTree.Build(kind);
            var reference=LowPolyTree.Build(kind);
            int count=tree.Triangles*3;
            check(tree.Triangles==triangles[kind]&&tree.Ambient.SequenceEqual(reference.Ambient),
                "AO keeps the existing triangle counts and repeats exactly on rebuild");
            check(tree.Ambient[0]==223&&tree.Ambient.Take(count).Min()>=221&&tree.Ambient.Take(count).Max()==255,
                "trunk contact is softly shaded and exposed vertices retain full brightness");
            if(kind==2)check(tree.Ambient[25*3]==221,"the center of a pine cone underside receives the deepest occlusion");
            Array.Fill(reference.Ambient,(byte)255);

            var vertices=new VertexPositionColorTexture[count];
            var unshaded=new VertexPositionColorTexture[count];
            uint[] spring=new uint[count];
            for(int season=0;season<4;season++){
                LowPolyTrees.WriteVertices(tree,kind,season,1.25f,vertices);
                LowPolyTrees.WriteVertices(reference,kind,season,1.25f,unshaded);
                bool changedPalette=false,shaded=false;
                for(int i=0;i<count;i++){
                    var vertex=vertices[i];var original=unshaded[i];
                    check(vertex.Color.A==255&&vertex.Color.R<=original.Color.R&&vertex.Color.G<=original.Color.G&&vertex.Color.B<=original.Color.B,
                        "baked AO only darkens the original RGB palette and keeps opaque alpha");
                    check(vertex.Position.Equals(original.Position)&&vertex.TextureCoordinate.Equals(original.TextureCoordinate),
                        "vertex layout, part order and mipmapped detail coordinates survive AO");
                    shaded|=vertex.Color.Packed!=original.Color.Packed;
                    if(season==0)spring[i]=vertex.Color.Packed;
                    changedPalette|=spring[i]!=vertex.Color.Packed;
                }
                check(shaded&&changedPalette==(season==2&&kind==0||season==3&&kind!=1),
                    "shading preserves the expected seasonal changes and evergreen palettes");
            }
        }

        int[] kinds={2,0,1,2,1,0,0};
        var poses=kinds.Select(_=>new Transform()).ToArray();
        var meshes=new Mesh[kinds.Length];var transforms=new Transform[kinds.Length];
        int total=0;
        for(int kind=0;kind<LowPolyTree.KindCount;kind++){
            var shared=new TestMesh(kind+1);
            int[] expected=Enumerable.Range(0,kinds.Length-1).Where(i=>kinds[i]==kind).ToArray();
            int used=LowPolyTrees.WriteBatch(shared,kind,kinds,poses,kinds.Length-1,meshes,transforms);
            check(used==expected.Length,"the original per-kind batches include only selected instances");
            for(int i=0;i<used;i++)check(ReferenceEquals(meshes[i],shared)&&ReferenceEquals(transforms[i],poses[expected[i]]),
                "every tree of one kind uses the same mesh with its own original transform");
            total+=used;
        }
        check(total==kinds.Length-1,"every selected tree is submitted exactly once");
    }
}
