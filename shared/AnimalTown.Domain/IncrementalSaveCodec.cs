namespace AnimalTown;

// v5 is a reference manifest over independently alternating v4 data records.
// The normal seven-record snapshot remains unchanged for server/export callers.
internal static class IncrementalSaveCodec
{
    internal const int Count=8,Residents=8,RuntimeFields=67;
    internal sealed class Manifest
    {
        internal int Sequence,Seed,Bank;
        internal readonly int[] Banks=new int[Count],Sequences=new int[Count],Lengths=new int[Count];
        internal readonly uint[] Checksums=new uint[Count];
    }
    internal static Manifest Read(byte[] bytes,int bank)
    {
        var r=new SplitSaveCodec.Reader(bytes,SplitSaveCodec.Commit);
        if(r.Version!=5)throw new InvalidOperationException("Not an incremental save manifest");
        var m=new Manifest{Sequence=r.Sequence,Seed=r.Seed,Bank=bank};
        for(int i=0;i<Count;i++){
            if(r.Int()!=SplitSaveCodec.RecordKind(i))throw new InvalidOperationException("Invalid save reference kind/order");
            m.Banks[i]=r.Range(0,1);m.Sequences[i]=r.Range(0,m.Sequence);m.Lengths[i]=r.Range(32,SaveCodec.MaximumBytes);m.Checksums[i]=(uint)r.Int();
        }
        r.End();return m;
    }
    internal static byte[] Commit(GameState s,Manifest m)
    {
        var w=new SplitSaveCodec.Writer(s,SplitSaveCodec.Commit,256,5);
        for(int i=0;i<Count;i++){w.Int(SplitSaveCodec.RecordKind(i));w.Int(m.Banks[i]);w.Int(m.Sequences[i]);w.Int(m.Lengths[i]);w.Int((int)m.Checksums[i]);}
        return w.Finish();
    }
    internal static void Reference(Manifest m,int part,int bank,byte[] bytes)
    {
        var r=new SplitSaveCodec.Reader(bytes,SplitSaveCodec.RecordKind(part));
        if(r.Version!=4||r.Seed!=m.Seed||r.Sequence>m.Sequence)throw new InvalidOperationException("Invalid reusable save record");
        m.Banks[part]=bank;m.Sequences[part]=r.Sequence;m.Lengths[part]=bytes.Length;m.Checksums[part]=SaveCodec.Crc(bytes,0,bytes.Length-4);
    }
    internal static void Verify(Manifest m,int part,byte[] bytes)
    {
        var r=new SplitSaveCodec.Reader(bytes,SplitSaveCodec.RecordKind(part));
        if(r.Version!=4||r.Seed!=m.Seed||r.Sequence!=m.Sequences[part]||bytes.Length!=m.Lengths[part]||SaveCodec.Crc(bytes,0,bytes.Length-4)!=m.Checksums[part])throw new InvalidOperationException("Save reference mismatch");
    }
    internal static bool SamePayload(byte[] a,byte[] b)
    {
        if(a.Length!=b.Length)return false;
        // Sequence and its derived CRC change on publication, not content.
        for(int i=0;i<a.Length-4;i++)if((i<16||i>=20)&&a[i]!=b[i])return false;
        return true;
    }
    internal static byte[] Encode(GameState s,int part)
    {
        if(part==Count-1){
            var w=new SplitSaveCodec.Writer(s,Residents,320);
            for(int i=0;i<6;i++){var v=s.Town.Residents[i];w.Int(v.X);w.Int(v.Z);w.Int((int)(v.PosX*1024));w.Int((int)(v.PosZ*1024));}
            w.Int(s.ActiveProfile);
            for(int i=0;i<6;i++){var v=s.Town.Residents[i];w.Int(v.Space);w.Int(v.JourneyBuilding);w.Int(v.JourneyPhase);w.Int(v.Dwell);w.Int((int)(v.Yaw*1024));w.Int(v.JourneyNextX);w.Int(v.JourneyNextZ);}
            return w.Finish();
        }
        var bytes=SplitSaveCodec.EncodePart(s,part);
        if(part==0){var offsets=RuntimeOffsets(bytes);for(int i=0;i<offsets.Length;i++)Put(bytes,offsets[i],0);Checksum(bytes);}
        return bytes;
    }
    // Locate only the fixed runtime fields in the bounded v4 town layout.
    // Static terrain, identity, placements and building types remain byte-exact.
    private static int[] RuntimeOffsets(byte[] bytes)
    {
        var r=new SplitSaveCodec.Reader(bytes,SplitSaveCodec.TownData);
        if(r.Version!=4)throw new InvalidOperationException("Invalid incremental town projection");
        var positions=new int[RuntimeFields];int at=0;
        r.Range(Town.Width,Town.Width);r.Range(Town.Height,Town.Height);r.Range(1,TownGenerator.Version);r.Int();r.Int();r.SkipTerrain();r.SkipTerrain();
        for(int i=0;i<6;i++){
            positions[at++]=r.Offset;r.Int();positions[at++]=r.Offset;r.Int();r.SkipText(24);
            positions[at++]=r.Offset;r.Int();positions[at++]=r.Offset;r.Int();
        }
        int n=r.Range(0,512);for(int i=0;i<n*6;i++)r.Int();
        n=r.Range(0,256);for(int i=0;i<n*6;i++)r.Int();
        positions[at++]=r.Offset;r.Int();for(int i=0;i<BuildingWorld.Count;i++)r.Int();
        for(int i=0;i<42;i++){positions[at++]=r.Offset;r.Int();}
        r.End();return positions;
    }
    internal static byte[][] Restore(Manifest m,byte[][] wire)
    {
        if(wire.Length!=Count)throw new InvalidOperationException("Incomplete referenced save");
        var parts=new byte[SplitSaveCodec.PartCount][];
        for(int i=0;i<Count;i++)Verify(m,i,wire[i]);
        for(int i=0;i<parts.Length;i++)parts[i]=(byte[])wire[i].Clone();
        var offsets=RuntimeOffsets(parts[0]);var runtime=new SplitSaveCodec.Reader(wire[Count-1],Residents);
        for(int i=0;i<offsets.Length;i++){
            int p=offsets[i];if(parts[0][p]!=0||parts[0][p+1]!=0||parts[0][p+2]!=0||parts[0][p+3]!=0)throw new InvalidOperationException("Noncanonical town projection");
            Put(parts[0],p,runtime.Int());
        }
        runtime.End();
        for(int i=0;i<parts.Length;i++){Put(parts[i],16,m.Sequence);Checksum(parts[i]);}
        return parts;
    }
    private static void Put(byte[] bytes,int at,int value){for(int i=0;i<4;i++)bytes[at+i]=(byte)((uint)value>>(8*i));}
    private static void Checksum(byte[] bytes)=>Put(bytes,bytes.Length-4,(int)SaveCodec.Crc(bytes,0,bytes.Length-4));
}
