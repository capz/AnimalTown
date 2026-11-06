namespace AnimalTown;
public static class BuildingSave
{
    public const int FieldCount=2+BuildingWorld.Count+6*7;
    public static int[] Encode(GameState s)
    {
        var fields=new int[FieldCount];int at=0;fields[at++]=s.Space;fields[at++]=(int)(s.Facing*1024);
        for(int i=0;i<s.BuildingTypes.Length;i++)fields[at++]=s.BuildingTypes[i];
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];fields[at++]=v.Space;fields[at++]=v.JourneyBuilding;fields[at++]=v.JourneyPhase;fields[at++]=v.Dwell;fields[at++]=(int)(v.Yaw*1024);fields[at++]=v.JourneyNextX;fields[at++]=v.JourneyNextZ;}
        return fields;
    }
    public static void Decode(GameState s,int[] fields)
    {
        if(fields.Length!=FieldCount)throw new InvalidOperationException("Invalid building save length");int at=0;
        int Read(int lo,int hi){int value=fields[at++];if(value<lo||value>hi)throw new InvalidOperationException("Invalid saved building state");return value;}
        s.Space=Read(0,25);if(!BuildingWorld.ValidSpace(s.Space))throw new InvalidOperationException("Invalid player space");s.Facing=Read(-6434,6434)/1024f;
        for(int i=0;i<s.BuildingTypes.Length;i++)s.BuildingTypes[i]=(byte)Read(0,255);
        for(int i=0;i<6;i++){var v=s.Town.Residents[i];v.Space=Read(0,25);if(!BuildingWorld.ValidSpace(v.Space))throw new InvalidOperationException("Invalid villager space");v.JourneyBuilding=Read(-1,8);v.JourneyPhase=Read(0,5);v.Dwell=Read(0,36000);v.Yaw=Read(-6434,6434)/1024f;v.JourneyNextX=Read(-1,Town.Width-1);v.JourneyNextZ=Read(-1,Town.Height-1);if((v.JourneyNextX<0)!=(v.JourneyNextZ<0))throw new InvalidOperationException("Partial journey waypoint");if(v.JourneyPhase==1&&v.JourneyBuilding<0)throw new InvalidOperationException("Missing journey destination");}
    }
}
