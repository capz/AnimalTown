namespace AnimalTown;

// Reuse a small draw buffer until every visible tree has been submitted. Its
// capacity limits one batch, never which trees are allowed to stay on screen.
public static class TreeDrawBatch
{
    public const int Capacity=32;
    public static bool IsTree(GroundItem item)=>item.Kind==3||item.Kind==2&&item.Item!=2;
    public static bool Visible(Town town,GroundItem item,float focusX,float focusZ,float eyeHeight)
    {
        float scale=item.Kind==2?.75f:3;
        var center=CylinderWorld.Surface(item.Z,focusZ,TerrainElevation.At(town,item.X,item.Z)+scale*LowPolyTree.VisibilityCenterY);
        float radius=scale*LowPolyTree.VisibilityRadius+.025f;
        return RenderView.Sphere(item.X,center.Y,center.Z,radius,focusX,focusZ,eyeHeight)&&
            RenderView.AboveHorizon(center.Y,center.Z,radius,focusZ,eyeHeight);
    }
    public static int Fill(Town town,float focusX,float focusZ,float eyeHeight,int[] selected,ref int cursor)
    {
        if(selected.Length==0)throw new ArgumentException("A tree batch needs at least one slot");
        int count=0;
        while(cursor<town.GroundCount&&count<selected.Length){
            int index=cursor++;var item=town.Ground[index];
            if(IsTree(item)&&Visible(town,item,focusX,focusZ,eyeHeight))selected[count++]=index;
        }
        return count;
    }
    public static int Fill(Town town,WorldObjectCandidates candidates,float focusX,float focusZ,float eyeHeight,int[] selected,ref int cursor)
    {
        if(selected.Length==0)throw new ArgumentException("A tree batch needs at least one slot");
        int count=0;
        while(cursor<candidates.GroundCount&&count<selected.Length){
            int index=candidates.GroundIndices[cursor++];var item=town.Ground[index];
            if(IsTree(item)&&Visible(town,item,focusX,focusZ,eyeHeight))selected[count++]=index;
        }
        return count;
    }
}
