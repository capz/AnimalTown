using AnimalTown;
using System.Buffers.Binary;
using System.Text;

// Disposable synthetic card input for Dolphin; never reads or alters a real save.
internal static class AttentionFixture
{
    internal static void Write(string destination,int day,bool deadline)
    {
        var s=new GameState(841){Name="Alex",Tutorial=6,MetMask=3,Sequence=1,Wallet=1234};
        s.Rollover(day,2026*12+10);
        if(!s.PlaceHouse(15,35,false))throw new InvalidOperationException("Fixture house plot");
        s.X=18.5f;s.Z=43;s.Facing=3.1415927f;
        for(int i=s.Town.GroundCount-1;i>=0;i--){var g=s.Town.Ground[i];if(g.X>=8&&g.X<=30&&g.Z>=34&&g.Z<=52)s.Town.RemoveGround(i);}
        for(int i=0;i<6;i++){
            var v=s.Town.Residents[i];v.Met=1;v.LastSeenDay=day;v.Dwell=36000;v.Space=i<2?0:16+i;v.JourneyPhase=i<2?0:3;
            v.PosX=i==0?15.5f:i==1?21.5f:2;v.PosZ=i<2?44.5f:2;v.X=i<2?(int)v.PosX:v.HomeX;v.Z=i<2?(int)v.PosZ:v.HomeZ+4;
        }
        var friendly=s.Town.Residents[0];friendly.Name="Milo";friendly.LastSeenDay=day-3;
        var urgent=s.Town.Residents[1];urgent.Name="Pip";urgent.Request=14;urgent.RequestDay=day;
        if(!deadline&&!s.Receive(14))throw new InvalidOperationException("Fixture request item");
        byte[] payload=SaveCodec.Encode(s);SaveCodec.Decode(payload);
        int blocks=(payload.Length+32+8191)/8192;var gci=new byte[64+blocks*8192];Array.Fill(gci,(byte)255);
        Encoding.ASCII.GetBytes("AMTE01").CopyTo(gci,0);gci[6]=255;gci[7]=0;
        Array.Clear(gci,8,32);Encoding.ASCII.GetBytes("AnimalTown1").CopyTo(gci,8);
        BinaryPrimitives.WriteUInt32BigEndian(gci.AsSpan(40,4),0);
        BinaryPrimitives.WriteUInt32BigEndian(gci.AsSpan(44,4),uint.MaxValue);
        gci[48]=gci[49]=gci[50]=gci[51]=0;gci[52]=4;gci[53]=0;gci[54]=gci[55]=0;
        BinaryPrimitives.WriteUInt16BigEndian(gci.AsSpan(56,2),(ushort)blocks);
        BinaryPrimitives.WriteUInt32BigEndian(gci.AsSpan(60,4),uint.MaxValue);
        Array.Clear(gci,64,32);Encoding.ASCII.GetBytes("DNDSAVE1").CopyTo(gci,64);
        BinaryPrimitives.WriteUInt32BigEndian(gci.AsSpan(72,4),(uint)payload.Length);
        BinaryPrimitives.WriteUInt32BigEndian(gci.AsSpan(76,4),32);
        payload.CopyTo(gci,96);Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(destination))!);File.WriteAllBytes(destination,gci);
        Console.WriteLine("Synthetic attention card fixture: "+destination+"; day="+day+"; deadline="+deadline);
    }
}
