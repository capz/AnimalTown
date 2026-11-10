internal static class AttentionGeometryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var vertices=VillagerAttentionRenderer.BuildVertices();
        check(vertices.Length==132,"all six villagers reuse a bounded 44-triangle attention icon");
        bool translucent=false,opaque=false;
        for(int i=0;i<vertices.Length;i+=3){
            var a=vertices[i].Position;var b=vertices[i+1].Position;var c=vertices[i+2].Position;
            float area=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
            check(area<0,"attention bubble, tail and exclamation all face the camera under global clockwise backface culling");
        }
        foreach(var v in vertices){
            check(float.IsFinite(v.Position.X)&&float.IsFinite(v.Position.Y)&&Math.Abs(v.Position.X)<.5f&&v.Position.Y>-.4f&&v.Position.Y<.3f,"complete icon fits its conservative viewport bound");
            check(v.Position.Z>=0&&v.Position.Z<=.02f,"icon layers stay in front-to-back order for depth-tested drawing");
            translucent|=v.Color.A<255;opaque|=v.Color.A==255;
        }
        check(translucent&&opaque,"soft shadow preserves alpha and paper/glyph remain opaque");
    }
}
