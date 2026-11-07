using Dolphin.Graphics;

internal static class TerrainIndexChecks
{
    private static VertexPositionNormalColorTexture V(float x,float z,float height=0)=>new VertexPositionNormalColorTexture(new Vector3(x,height,z),new Vector3(0,1,0),Color32.White,new Vector2(x*.5f,z*.5f));
    internal static void Run(Action<bool,string> check)
    {
        var geometry=new TerrainIndexBuffer();var expected=new List<VertexPositionNormalColorTexture>();
        for(int z=0;z<4;z++){
            VertexPositionNormalColorTexture a=V(0,z),b=V(8,z),c=V(8,z+1),d=V(0,z+1);
            geometry.Quad(a,b,c,d);expected.AddRange(new[]{a,b,c,a,c,d});
        }
        check(geometry.VertexCount==10&&geometry.IndexCount==24,"four flat strips share their row edges without changing triangles");
        for(int i=0;i<expected.Count;i++){
            var actual=geometry.Vertices[geometry.Indices[i]];var wanted=expected[i];
            check(actual.Position.X==wanted.Position.X&&actual.Position.Y==wanted.Position.Y&&actual.Position.Z==wanted.Position.Z&&actual.TextureCoordinate.X==wanted.TextureCoordinate.X&&actual.TextureCoordinate.Y==wanted.TextureCoordinate.Y&&actual.Color.Packed==wanted.Color.Packed,"indexed expansion preserves triangle winding, positions, UVs and tints");
        }
        check(geometry.VertexCount*36+geometry.IndexCount*2==408&&24*36==864,"exact shared native payload halves the unindexed flat-strip payload");
        var vertices=geometry.Vertices;var indices=geometry.Indices;geometry.Reset();geometry.Quad(V(0,0),V(1,0),V(1,1),V(0,1));
        check(ReferenceEquals(vertices,geometry.Vertices)&&ReferenceEquals(indices,geometry.Indices)&&geometry.VertexCount==4&&geometry.IndexCount==6,"reset reuses staging capacity and forgets old logical content");
        var same=V(1,1);var tint=same;tint.Color=new Color32(205,199,181);var uv=same;uv.TextureCoordinate=new Vector2(9,9);var normal=same;normal.Normal=new Vector3(0,0,1);
        geometry.Quad(same,tint,uv,normal);check(geometry.VertexCount==7,"wet-sand, UV and normal seams never weld merely because positions match");
        geometry.Quad(V(0,0,2),V(1,0,2),V(1,1,2),V(0,1,2));check(geometry.VertexCount==11,"high and low cliff surfaces retain independent vertices");
        geometry.Reset();
        for(int q=0;q<32;q++)geometry.Quad(V(q*3,0),V(q*3+1,0),V(q*3+1,1),V(q*3,1));
        check(geometry.VertexCount==TerrainIndexBuffer.MaximumVertices&&geometry.IndexCount==TerrainIndexBuffer.MaximumIndices,"worst-case section fits its explicit index and vertex budgets");
        for(int i=0;i<geometry.IndexCount;i++)check(geometry.Indices[i]<geometry.VertexCount,"growth preserves every authored index and staged prefix");
        bool rejected=false;try{geometry.Quad(V(0,0),V(1,0),V(1,1),V(0,1));}catch(InvalidOperationException){rejected=true;}
        check(rejected&&geometry.IndexCount==TerrainIndexBuffer.MaximumIndices,"overflow fails before changing the valid triangle count");
    }
}
