namespace AnimalTown;

// Explicitly admitted, bounded rolling capture. 64-byte records; no objects or
// formatted strings are created by Record. Counters remain useful without it.
public sealed class ProfileCapture
{
    public const int Capacity=256,WordsPerFrame=16,ReservedBytes=20480;
    public readonly uint[] Words=new uint[Capacity*WordsPerFrame];
    public readonly uint[] IntervalHistogram=new uint[32];
    public readonly uint[] PhasePeaks=new uint[16];
    public readonly int[] MemoryPeaks=new int[32];
    public readonly uint[] Events=new uint[64*4];
    public int EventCount {get;private set;}
    public int DroppedEvents {get;private set;}
    public uint Session {get;}
    public uint Total {get;private set;}
    public uint Overwritten=>Total>Capacity?Total-Capacity:0;
    public int Count=>(int)Math.Min((uint)Capacity,Total);
    public ProfileCapture(uint session){Session=session;}
    public void Event(uint sequence,uint stage,uint clock,uint used)
    {
        if(EventCount==64){DroppedEvents++;return;}
        int at=EventCount++*4;Events[at]=sequence;Events[at+1]=stage;Events[at+2]=clock;Events[at+3]=used;
    }
    public void Record(uint sequence,uint interval,int[] packet,int[] memory)
    {
        int at=(int)(Total%Capacity)*WordsPerFrame;
        Words[at]=sequence;Words[at+1]=interval;
        for(int i=0;i<8;i++)Words[at+2+i]=(uint)(packet[4+i*2]&65535)|((uint)(packet[5+i*2]&65535)<<16);
        Words[at+10]=(uint)packet[20]|((uint)packet[21]<<16);
        Words[at+11]=(uint)(packet[26]&65535)|((uint)(packet[28]&65535)<<16);
        Words[at+12]=(uint)(packet[27]&65535)|((uint)(packet[29]&65535)<<16);
        Words[at+13]=(uint)(packet[23]&65535)|((uint)(packet[30]&65535)<<16);
        Words[at+14]=(uint)(packet[3]&65535)|((uint)(packet[24]&65535)<<16);
        Words[at+15]=(uint)(packet[22]&65535)|((uint)(packet[25]&65535)<<16);
        uint value=interval;int bucket=0;while(value>1&&bucket<31){value>>=1;bucket++;}IntervalHistogram[bucket]++;
        for(int i=0;i<16;i++)PhasePeaks[i]=Math.Max(PhasePeaks[i],(uint)packet[4+i]);
        for(int i=0;i<MemoryPeaks.Length;i++)MemoryPeaks[i]=Math.Max(MemoryPeaks[i],memory[i]);
        Total++;
    }
    // CRC-16/CCITT over little-endian 16-bit wire words. Identity CRC covers
    // every band of the same presentation, preventing cross-frame attachment.
    public static int Crc(int crc,int[] words,int count)
    {
        for(int i=0;i<count;i++)for(int octet=0;octet<2;octet++){
            crc^=((words[i]>>(octet*8))&255)<<8;
            for(int bit=0;bit<8;bit++)crc=(crc&32768)!=0?((crc<<1)^0x1021)&65535:(crc<<1)&65535;
        }
        return crc;
    }
}
