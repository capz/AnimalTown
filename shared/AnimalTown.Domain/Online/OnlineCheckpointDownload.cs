namespace AnimalTown.Online;

// Accept arbitrary HTTP page boundaries without retaining a duplicate envelope.
// Record lengths are checked before allocating; a partial or mismatched stream
// never exposes a publication to the card writer.
public sealed class OnlineCheckpointDownload : IDisposable
{
    readonly int expectedLength;readonly string fingerprint;readonly OnlineCheckpointHash hash=new();
    readonly byte[][] fields=new byte[SplitSaveCodec.PartCount+1][];
    readonly byte[] prefix=new byte[4];int prefixAt,field,fieldAt,position;bool complete,disposed;
    public int Position=>position;
    public OnlineCheckpointDownload(int length,string fingerprint)
    {
        if(length<256||length>SaveCodec.MaximumBytes||!OnlineCardCredential.Hex(fingerprint,0,64))throw new FormatException("Invalid return checkpoint.");
        expectedLength=length;this.fingerprint=fingerprint;
    }
    public void Append(byte[] bytes,int offset,int count)
    {
        if(disposed||complete||offset<0||count<0||offset>bytes.Length-count||count>expectedLength-position)
            throw new InvalidOperationException("Invalid return checkpoint page.");
        hash.Append(bytes,offset,count);
        while(count>0)
        {
            if(field>=fields.Length)throw new InvalidOperationException("Trailing return checkpoint data.");
            if(prefixAt<4)
            {
                prefix[prefixAt++]=bytes[offset++];count--;position++;
                if(prefixAt<4)continue;
                uint length=(uint)prefix[0]|(uint)prefix[1]<<8|(uint)prefix[2]<<16|(uint)prefix[3]<<24;
                int remainingFields=fields.Length-field-1;
                if(length<32||length>expectedLength-position-remainingFields*36||field==0&&length>128)
                    throw new InvalidOperationException("Invalid return checkpoint record length.");
                fields[field]=new byte[(int)length];fieldAt=0;
            }
            int take=Math.Min(count,fields[field].Length-fieldAt);
            Array.Copy(bytes,offset,fields[field],fieldAt,take);fieldAt+=take;offset+=take;count-=take;position+=take;
            if(fieldAt==fields[field].Length){field++;prefixAt=0;}
        }
    }
    public void Verify()
    {
        if(disposed||complete||position!=expectedLength||field!=fields.Length||prefixAt!=0)throw new InvalidOperationException("The return checkpoint is incomplete.");
        if(hash.Complete()!=fingerprint)throw new InvalidOperationException("The return checkpoint fingerprint differs.");
        SplitSaveCodec.VerifyCommitted(fields[0],Parts());complete=true;
    }
    byte[][] Parts(){var records=new byte[SplitSaveCodec.PartCount][];for(int i=0;i<records.Length;i++)records[i]=fields[i+1];return records;}
    public GameState Publish(SaveRepository repository,GameState expected)
    {if(!complete||disposed)throw new InvalidOperationException("Verify the return checkpoint before saving.");return repository.PublishOnlineCheckpoint(expected,fields[0],Parts());}
    public void Dispose()
    {
        if(disposed)return;disposed=true;hash.Dispose();Array.Clear(prefix,0,prefix.Length);
        for(int i=0;i<fields.Length;i++){if(fields[i]!=null)Array.Clear(fields[i],0,fields[i].Length);fields[i]=null!;}
    }
}
