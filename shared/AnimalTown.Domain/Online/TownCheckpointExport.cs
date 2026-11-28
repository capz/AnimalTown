namespace AnimalTown;

internal interface ICheckpointRecordVerifier {void Verify(int part,byte[] bytes);}

// Canonical server upload envelope. A caller reads into its own page buffer,
// sends that page, and repeats. No cursor read advances through two records.
public sealed class TownCheckpointExport : IDisposable
{
    private GameState? snapshot;
    private byte[]? record;
    private readonly int[] lengths=new int[SplitSaveCodec.PartCount];
    private readonly uint[] checksums=new uint[SplitSaveCodec.PartCount];
    private readonly int sequence;
    private int part=-1,recordOffset;

    public int Length {get;}
    public int Position {get;private set;}
    internal byte[] CommitBytes=>record!;

    internal TownCheckpointExport(GameState snapshot,ICheckpointRecordVerifier? verify=null)
    {
        this.snapshot=snapshot;
        sequence=snapshot.Sequence;
        var commit=new SplitSaveCodec.Writer(snapshot,SplitSaveCodec.Commit,128);
        int total=0;
        for(int index=0;index<SplitSaveCodec.PartCount;index++)
        {
            Describe(snapshot,index,verify,out int length,out uint checksum);
            lengths[index]=length;checksums[index]=checksum;
            commit.Int(length);commit.Int(unchecked((int)checksum));
            total=AddRecord(total,length);
        }
        record=commit.Finish();
        Length=AddRecord(total,record.Length);
    }

    private static int AddRecord(int total,int length)
    {
        if(length>SaveCodec.MaximumBytes-total-4)
            throw new InvalidOperationException("The town checkpoint exceeds the upload limit");
        return total+4+length;
    }

    // Encoding and verification finishes in a separate frame, so a previous
    // encoded component cannot remain a native collector root on the next one.
    private static void Describe(GameState state,int part,ICheckpointRecordVerifier? verify,out int length,out uint checksum)
    {
        byte[] bytes=SplitSaveCodec.EncodePart(state,part);
        verify?.Verify(part,bytes);
        length=bytes.Length;checksum=FooterChecksum(bytes);
    }

    private static uint FooterChecksum(byte[] bytes)
    {
        int at=bytes.Length-4;
        return (uint)bytes[at]|(uint)bytes[at+1]<<8|(uint)bytes[at+2]<<16|(uint)bytes[at+3]<<24;
    }

    private byte[] NextRecord(GameState state)
    {
        // Guard against reentrant gameplay edits between asynchronous sends.
        // Neither a changed record nor its prefix leaves the cursor.
        if(state.Sequence!=sequence)throw new InvalidOperationException("The checkpoint changed during upload");
        state.CaptureProfile();
        byte[] next=SplitSaveCodec.EncodePart(state,part);
        if(next.Length!=lengths[part]||FooterChecksum(next)!=checksums[part])
            throw new InvalidOperationException("The checkpoint changed during upload");
        return next;
    }

    // Returns short reads at record boundaries, and zero at EOF (or for a
    // zero-length request). Offset and count refer to the caller's buffer.
    public int Read(byte[] buffer,int offset,int count)
    {
        var state=snapshot??throw new ObjectDisposedException(nameof(TownCheckpointExport));
        if(buffer==null)throw new ArgumentNullException(nameof(buffer));
        if(offset<0||offset>buffer.Length||count<0||count>buffer.Length-offset)
            throw new ArgumentOutOfRangeException(nameof(count));
        if(count==0||part==SplitSaveCodec.PartCount)return 0;

        byte[] bytes=record??=NextRecord(state);
        int size=4+bytes.Length;
        int taken=Math.Min(count,size-recordOffset),copied=0;
        while(recordOffset<4&&copied<taken)
        {
            buffer[offset+copied]=(byte)(bytes.Length>>(8*recordOffset));
            recordOffset++;copied++;
        }
        int body=taken-copied;
        if(body!=0)
        {
            Array.Copy(bytes,recordOffset-4,buffer,offset+copied,body);
            recordOffset+=body;
        }
        if(recordOffset==size){record=null;recordOffset=0;part++;}
        Position+=taken;
        return taken;
    }

    public void Dispose(){record=null;snapshot=null;}
}
