namespace AnimalTown;
public sealed partial class GameState
{
    public int Space;
    public float Facing;
    public readonly byte[] BuildingTypes=new byte[BuildingWorld.Count];
    // Persist only committed actor locations. Portal animation owns no saved
    // position, so saving halfway through a doorway cannot create a half-room.
    public void NormalizeBuildingLocations()
    {
        for(int actor=0;actor<BuildingWorld.Actors;actor++){
            var p=BuildingWorld.Location(this,actor);if(p.Space.IsTown)continue;
            if(!BuildingWorld.ValidSpace(p.Space.Value))throw new InvalidOperationException("Unknown saved space");
            if(BuildingWorld.CanStand(this,p.Space.Value,p.X,p.Z))continue;
            int building=BuildingWorld.Building(p.Space.Value);bool found=false;
            if(BuildingWorld.Available(this,p.Space.Value)){
                int cells=BuildingWorld.Size(this,p.Space.Value)*2;
                for(int z=0;z<cells&&!found;z++)for(int x=0;x<cells&&!found;x++)if(BuildingWorld.CanStand(this,p.Space.Value,x*.5f+.25f,z*.5f+.25f)){
                    BuildingWorld.SetLocation(this,actor,new ActorLocation(p.Space.Value,x*.5f+.25f,z*.5f+.25f,p.Yaw));found=true;
                }
            }
            if(!found){BuildingWorld.Entrance(this,building,out float x,out float z);
                if(!CanWalk(x,z,0))for(int cell=0;cell<Town.Cells;cell++)if(CanWalk(cell%Town.Width+.5f,cell/Town.Width+.5f,0)){x=cell%Town.Width+.5f;z=cell/Town.Width+.5f;break;}
                BuildingWorld.SetLocation(this,actor,new ActorLocation(0,x,z,0));
            }
        }
    }
}
