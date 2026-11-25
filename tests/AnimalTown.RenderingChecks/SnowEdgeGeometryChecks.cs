using AnimalTown;
internal static class SnowEdgeGeometryChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var town=new Town(3);Array.Fill(town.Tiles,(byte)Terrain.Grass);
        foreach(byte level in new byte[]{0,1}){
            Array.Fill(town.Elevation,level);
            foreach(int z in new[]{1,24,60,118}){
                var foot=SnowEdgeRenderer.Vertex(town,20,z,21,z,0).Position;
                var top=SnowEdgeRenderer.Vertex(town,20,z,20.75f,z,SnowVisibility.Height).Position;
                float ground=level==0?0:2;
                double footRadius=Math.Sqrt((foot.Y+CylinderWorld.Radius)*(foot.Y+CylinderWorld.Radius)+foot.Z*foot.Z);
                double topRadius=Math.Sqrt((top.Y+CylinderWorld.Radius)*(top.Y+CylinderWorld.Radius)+top.Z*top.Z);
                check(Math.Abs(footRadius-(CylinderWorld.Radius+ground+.001f))<.0001,"snow ramp toes meet the supporting ground on flat land and terraces");
                check(Math.Abs(topRadius-(CylinderWorld.Radius+ground)*(1+SnowVisibility.Height/CylinderWorld.Radius))<.0001,"snow ramp tops meet the existing raised radial snow layer across the town");
                check(foot.X==21&&top.X==20.75f,"the snow ramp stays within the covered quarter tile");
            }
        }
    }
}
