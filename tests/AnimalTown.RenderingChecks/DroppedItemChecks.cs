using AnimalTown;
using Dolphin.Graphics;

internal static class DroppedItemChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var town=new Town(7319);Array.Fill(town.Tiles,(byte)Terrain.Grass);Array.Clear(town.Elevation);town.GroundCount=6;
        for(int i=0;i<6;i++){var g=town.Ground[i];g.X=29+i%3;g.Z=32+i;g.Kind=i<2?0:i-1;}
        var plan=new DroppedItemPlan();check(plan.Prepare(town,30,38,0)&&plan.Count==2,"only ordinary dropped items become sprites");
        // Any giftable item is still generic until explicitly wrapped.
        check(DroppedItemAppearance.For(1)==DroppedItemArt.Generic&&
              DroppedItemAppearance.For(10)==DroppedItemArt.Generic&&
              DroppedItemAppearance.For(17)==DroppedItemArt.Generic,
              "giftable and ordinary items retain the generic marker");
        check(DroppedItemAppearance.For(22)==DroppedItemArt.Money&&
              DroppedItemAppearance.For(23)==DroppedItemArt.Money&&
              DroppedItemAppearance.For(24)==DroppedItemArt.Money,
              "all currency denominations use the acorn bag");
        check(DroppedItemAppearance.For(5)==DroppedItemArt.Tool&&
              DroppedItemAppearance.For(6)==DroppedItemArt.Tool&&
              DroppedItemAppearance.For(7)==DroppedItemArt.Tool&&
              DroppedItemAppearance.For(8)==DroppedItemArt.Tool&&
              DroppedItemAppearance.For(9)==DroppedItemArt.Tool,
              "all equipment uses the shovel sprite");
        check(DroppedItemAppearance.For(4)==DroppedItemArt.Clothing,
              "wearable items use the shirt");
        check(DroppedItemAppearance.For(19)==DroppedItemArt.Quest&&
              DroppedItemAppearance.For(20)==DroppedItemArt.Quest,
              "quest letters and lost objects use the scroll");
        check(DroppedItemAppearance.For(18)==DroppedItemArt.Present&&
              DroppedItemAppearance.For(5,true)==DroppedItemArt.Present&&
              DroppedItemAppearance.For(4,true)==DroppedItemArt.Present&&
              DroppedItemAppearance.For(22,true)==DroppedItemArt.Present,
              "present overrides all underlying item types only when wrapped");

        var wrappedRecord=new GroundItem { Kind=0,Wrapped=true };
        var restored=new GroundItem();restored.RestoreKind(wrappedRecord.PackedKind);
        check(restored.Kind==0&&restored.Wrapped&&wrappedRecord.PackedKind==8,
              "wrapped appearance round-trips through the existing ground kind integer");
        restored.RestoreKind(4);
        check(restored.Kind==4&&!restored.Wrapped,"legacy ground kind has no wrapped appearance");

        town.Ground[0].Item=22;
        check(plan.Prepare(town,30,38,0)&&plan.Art[0]==(byte)DroppedItemArt.Money,
              "changing a dropped item without moving it selects the new texture");
        town.Ground[0].Wrapped=true;
        check(plan.Prepare(town,30,38,0)&&plan.Art[0]==(byte)DroppedItemArt.Present,
              "wrapping a stationary item invalidates its retained sprite");
        town.Ground[0].Wrapped=false;town.Ground[0].Item=5;
        check(plan.Prepare(town,30,38,0)&&plan.Art[0]==(byte)DroppedItemArt.Tool,
              "unwrapped tool returns to its underlying type");
        check(DroppedItemRenderer.WriteVertices(plan,new VertexPositionColorTexture[12],DroppedItemArt.Tool)==1,
              "texture batch writes only its selected category");
        town.Ground[1].Item=4;
        check(plan.Prepare(town,30,38,0)&&plan.Art[1]==(byte)DroppedItemArt.Clothing,
              "second drop can independently use clothing art");
        town.Ground[1].Item=19;
        check(plan.Prepare(town,30,38,0)&&plan.Art[1]==(byte)DroppedItemArt.Quest,
              "second drop can independently use quest art");
        town.Ground[1].Item=18;
        check(plan.Prepare(town,30,38,0)&&plan.Art[1]==(byte)DroppedItemArt.Present,
              "an unwrapped parcel is still depicted as a present");
        town.Ground[0].Item=1;town.Ground[1].Item=1;
        plan.Prepare(town,30,38,0);
        long revision=plan.Revision;check(!plan.Prepare(town,30,38,0)&&revision==plan.Revision,"unchanged stationary drops reuse their GPU geometry");
        town.Ground[0].X++;check(plan.Prepare(town,30,38,0),"moving a drop invalidates its retained geometry");
        town.Ground[0].Kind=1;check(plan.Prepare(town,30,38,0)&&plan.Count==1,"burying a drop removes its billboard");
        town.Ground[0].Kind=0;town.Ground[0].Z=120;check(plan.Prepare(town,30,38,0)&&plan.Count==1,"offscreen and cylinder-hidden drops are culled");
        town.Ground[0].Z=32;town.Elevation[32*Town.Width+town.Ground[0].X]=2;town.NotifyTerrainChanged();plan.Prepare(town,30,38,0);
        var ground=CylinderWorld.Surface(32.5f,38,TerrainElevation.At(town,town.Ground[0].X+.5f,32.5f));
        check(Math.Abs(plan.Y[0]-ground.Y-DroppedItemPlan.GroundOffset)<.00001f,"dropped sprite follows local raised terrain and cylinder height");
        check(plan.Prepare(town,30,38,.5f),"eye height updates culling and sorting");
        town.GroundCount=0;check(plan.Prepare(town,30,38,0)&&plan.Count==0,"picking up the last drop clears its geometry");
        town.GroundCount=town.Ground.Length;check(town.GroundCount==DroppedItemPlan.Capacity,"sprite capacity covers every persisted ground slot");
        for(int i=0;i<town.GroundCount;i++){var g=town.Ground[i];g.Kind=0;g.X=29+i%3;g.Z=30+i%7;}
        Array.Clear(town.Elevation);town.NotifyTerrainChanged();plan.Prepare(town,30,38,0);
        check(plan.Count==512,"all 512 visible drops fit without arbitrary truncation");
        for(int i=1;i<plan.Count;i++)check(plan.Depth[plan.Order[i-1]]>=plan.Depth[plan.Order[i]],"alpha sprites are sorted back to front");
        var vertices=new VertexPositionColorTexture[plan.Count*6];DroppedItemRenderer.WriteVertices(plan,vertices);
        CheckFacing(plan,vertices,check);
        // Move the eye across the town, up ramps/cliffs, and past drops at the
        // left/right/frustum edges. The old fixed basis fails the lateral rays.
        town.GroundCount=9;
        foreach(int focusX in new[]{8,64,Town.Width-8})foreach(int focusZ in new[]{24,64,Town.Height-8})foreach(float height in new[]{0f,.5f,2f}){
            for(int i=0;i<9;i++){var g=town.Ground[i];g.X=focusX+(i%3-1)*6;g.Z=focusZ-(i/3)*8;g.Kind=0;town.Elevation[g.Z*Town.Width+g.X]=(byte)(i%3);}
            town.NotifyTerrainChanged();plan.Prepare(town,focusX,focusZ,height);
            check(plan.Count>0,"eye-facing sweep retains visible drops across the town and raised terrain");
            DroppedItemRenderer.WriteVertices(plan,vertices);CheckFacing(plan,vertices,check);
        }
        foreach(var facing in new[]{new DroppedItemFacing(0,1,0),new DroppedItemFacing(0,-1,0),new DroppedItemFacing(0,0,0)})
            check(float.IsFinite(facing.RightX+facing.RightZ+facing.UpX+facing.UpY+facing.UpZ)&&Math.Abs(facing.RightX*facing.RightX+facing.RightZ*facing.RightZ-1)<.00001f&&Math.Abs(facing.UpX*facing.UpX+facing.UpY*facing.UpY+facing.UpZ*facing.UpZ-1)<.00001f,"overhead and coincident-eye fallbacks remain finite orthonormal billboards");
        town.GroundCount=town.Ground.Length;
        for(int i=0;i<town.GroundCount;i++){var g=town.Ground[i];g.Kind=0;g.X=29+i%3;g.Z=30+i%7;}
        for(int i=0;i<10;i++)plan.Prepare(town,30,38+i*.01f,0);
        long before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<200;i++)plan.Prepare(town,30,38+i*.001f,0);
        check(GC.GetAllocatedBytesForCurrentThread()==before,"moving and sorting the maximum sprite count allocates no frame garbage");
    }
    private static void CheckFacing(DroppedItemPlan plan,VertexPositionColorTexture[] vertices,Action<bool,string> check)
    {
        for(int n=0;n<plan.Count;n++){
            int at=n*6,index=plan.Order[n];float dx=plan.EyeX-plan.X[index],dy=plan.EyeY-plan.Y[index],dz=plan.EyeZ-plan.Z[index];
            var a=vertices[at].Position;var b=vertices[at+1].Position;var c=vertices[at+2].Position;var d=vertices[at+5].Position;
            float ux=b.X-a.X,uy=b.Y-a.Y,uz=b.Z-a.Z,rx=c.X-b.X,ry=c.Y-b.Y,rz=c.Z-b.Z;
            float nx=uy*rz-uz*ry,ny=uz*rx-ux*rz,nz=ux*ry-uy*rx;
            float normalLength=(float)Math.Sqrt(nx*nx+ny*ny+nz*nz),rayLength=(float)Math.Sqrt(dx*dx+dy*dy+dz*dz);
            check((nx*dx+ny*dy+nz*dz)/(normalLength*rayLength)<-.99999f,"each pinecone faces its own camera ray with clockwise GX winding");
            check(Math.Abs(ux*dx+uy*dy+uz*dz)<.0001f*rayLength&&Math.Abs(rx*dx+ry*dy+rz*dz)<.0001f*rayLength,"both sprite axes stay perpendicular to the eye ray rather than a shared fixed pitch");
            check(Math.Abs(ux*ux+uy*uy+uz*uz-DroppedItemPlan.Size*DroppedItemPlan.Size)<.00001f&&Math.Abs(rx*rx+ry*ry+rz*rz-DroppedItemPlan.Size*DroppedItemPlan.Size)<.00001f,"eye-facing rotations preserve the square sprite dimensions");
            check(Math.Abs(a.Y-plan.Y[index])<.00001f&&Math.Abs(d.Y-a.Y)<.00001f,"billboard bottom edge remains level and anchored above terrain");
            float cy=(a.Y+c.Y)*.5f,cz=(a.Z+c.Z)*.5f;
            check(Math.Abs(plan.Depth[index]-((cy-plan.EyeY)*RenderView.ForwardY+(cz-plan.EyeZ)*RenderView.ForwardZ))<.0001f,"alpha sorting uses the rotated quad centre");
        }
    }

}
