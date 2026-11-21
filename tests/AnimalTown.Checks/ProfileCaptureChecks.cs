using AnimalTown;
internal static class ProfileCaptureChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var capture=new ProfileCapture(42);var packet=new int[32];var memory=new int[32];packet[16]=333;packet[28]=125;memory[7]=100;
        capture.Record(65535,33333,packet,memory);capture.Record(65536,33334,packet,memory);
        check(capture.Words[0]==65535&&capture.Words[16]==65536&&capture.Session==42,"capture preserves 32-bit identity across old video sequence wrap");
        long before=GC.GetAllocatedBytesForCurrentThread();
        for(uint i=2;i<1000;i++){memory[7]=(int)i;capture.Record(i+65535,33333,packet,memory);}
        check(GC.GetAllocatedBytesForCurrentThread()==before,"recording and overwrite accounting allocate no per-frame objects");
        check(capture.Count==256&&capture.Total==1000&&capture.Overwritten==744&&capture.MemoryPeaks[7]==999,"bounded ring retains latest frames and reports overwrite and memory peaks");
        check(capture.IntervalHistogram.Sum(x=>(long)x)==1000&&capture.PhasePeaks[12]==333,"histogram includes overwritten frames without silently dropping hitches");
        for(int i=0;i<70;i++)capture.Event(123,1,99,100);
        check(capture.EventCount==64&&capture.DroppedEvents==6,"bounded checkpoint events disclose overflow");
        int bytes=(capture.Words.Length+capture.IntervalHistogram.Length+capture.PhasePeaks.Length+capture.MemoryPeaks.Length+capture.Events.Length)*4;
        check(bytes+512<ProfileCapture.ReservedBytes&&ProfileCapture.ReservedBytes<=24*1024,"capture storage including object allowance stays below the 24 KiB ceiling");
        int[] words={0x3231,0x3433,0x3635,0x3837};
        check(ProfileCapture.Crc(65535,words,4)==0xa12b,"wire CRC matches CCITT little-endian bytes 12345678");
    }
}
