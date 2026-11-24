using AnimalTown;

internal static class AttentionGeometryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var vertices=VillagerAttentionRenderer.BuildVertices();
        for(int target=0;target<Town.NpcActorCount;target++)for(int actor=0;actor<=BuildingWorld.Actors;actor++){
            check(VillagerAttentionRenderer.Selected(actor,false,target)==(actor==target+1),"an attention command selects only its NPC, never the player");
            check(VillagerAttentionRenderer.Selected(actor,true,target)==(actor>0&&actor<BuildingWorld.Actors),"the preview includes every NPC");
        }
        check(!VillagerAttentionRenderer.Selected(1,false,-1),"cleared attention submits no extra icon");
        check(vertices.Length==132,"all villagers reuse a bounded 44-triangle attention icon");
        bool translucent=false,opaque=false;
        for(int i=0;i<vertices.Length;i+=3){
            var a=vertices[i].Position;var b=vertices[i+1].Position;var c=vertices[i+2].Position;
            float area=(b.X-a.X)*(c.Y-a.Y)-(b.Y-a.Y)*(c.X-a.X);
            check(area<0,"attention bubble, tail and exclamation all face the camera under global clockwise backface culling");
        }
        foreach(var v in vertices){
            check(float.IsFinite(v.Position.X)&&float.IsFinite(v.Position.Y)&&Math.Abs(v.Position.X)<.5f&&v.Position.Y>-.4f&&v.Position.Y<.3f,"complete icon fits its conservative viewport bound");
            check(Math.Abs(v.TextureCoordinate.X-v.Position.X*1.5f)<.00001f&&Math.Abs(v.TextureCoordinate.Y-v.Position.Y*1.5f)<.00001f,"attention paper/tail/glyph share continuous noise coordinates");
            check(v.Position.Z>=0&&v.Position.Z<=.02f,"icon layers stay in front-to-back order for depth-tested drawing");
            translucent|=v.Color.A<255;opaque|=v.Color.A==255;
        }
        check(translucent&&opaque,"soft shadow preserves alpha and paper/glyph remain opaque");
    }
}
