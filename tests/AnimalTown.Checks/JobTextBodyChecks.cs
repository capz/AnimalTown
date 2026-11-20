using AnimalTown.Jobs;
using System.Text;

internal static class JobTextBodyChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var slots=new JobMailSlots(576);_ =slots[0].State;long beforeRead=GC.GetAllocatedBytesForCurrentThread();int empty=0;
        for(int pass=0;pass<100;pass++)for(int i=0;i<slots.Length;i++)if(slots[i].State==JobMailState.Empty)empty++;
        check(empty==57600&&GC.GetAllocatedBytesForCurrentThread()==beforeRead,"reading absent mailbox pages allocates no memory");
        slots.Set(31,new JobMailRecord{State=JobMailState.Inbox});slots.Set(32,new JobMailRecord{State=JobMailState.Queued});slots.Set(31,default);
        check(slots[31].State==JobMailState.Empty&&slots[32].State==JobMailState.Queued,"clearing one page preserves the neighbouring page and its envelopes");
        // Exercise every Unicode scalar, including every 21-bit word crossing.
        // Compare with the platform's independent UTF-8 implementation.
        var text=new StringBuilder();int count=0;
        void RoundTrip(){
            string original=text.ToString();var packed=JobTextBody.Pack(original,count);JobTextBody.Validate(packed);
            string restored=JobTextBody.Unpack(packed);
            check(original==restored&&Encoding.UTF8.GetBytes(original).SequenceEqual(Encoding.UTF8.GetBytes(restored)),"packed text preserves every Unicode scalar and word boundary");
            check(packed.Length*4<=2632,"every 1,000-scalar body stays within the retained body bound");text.Clear();count=0;
        }
        for(int cp=0;cp<=0x10ffff;cp++){
            if(cp>=0xd800&&cp<=0xdfff)continue;text.Append(char.ConvertFromUtf32(cp));
            if(++count==1000)RoundTrip();
        }
        if(count>0)RoundTrip();
        string body=string.Concat(Enumerable.Range(0x10000,1000).Select(char.ConvertFromUtf32));var compact=JobTextBody.Pack(body,1000);
        check(compact.Length==658,"1,000 distinct supplementary characters require 2,632 bytes without repeated-text deduplication");
        var legacy=new int[751];legacy[0]=-1001;int at=0;
        foreach(var rune in body.EnumerateRunes())for(int b=0;b<3;b++){legacy[1+at/4]|=(rune.Value>>(b*8)&255)<<(at%4*8);at++;}
        JobTextBody.Validate(legacy);check(JobTextBody.Unpack(legacy)==body&&JobTextBody.Compact(legacy).SequenceEqual(compact),"legacy three-byte scalar bodies compact losslessly");
        var sizing=new JobWireWriter();sizing.Text(body);var utf8=new int[sizing.Count];new JobWireWriter(utf8).Text(body);JobTextBody.Validate(utf8);
        check(JobTextBody.Compact(utf8).SequenceEqual(compact),"legacy four-byte UTF-8 compacts without materializing a UTF-16 string");
        check(JobTextBody.Pack("Thank you!",10)[0]==10,"ordinary ASCII letters retain the smaller UTF-8 representation");
        foreach(var bad in new[]{new[]{int.MinValue},new[]{-2003},new[]{-1003,0xd800},new[]{-1003,0x110000},new[]{-1003,0x1fffff}}){
            bool refused=false;try{JobTextBody.Validate(bad);}catch(InvalidOperationException){refused=true;}check(refused,"corrupt scalar tags and invalid Unicode values are rejected");
        }
        Console.WriteLine("Packed mail: all Unicode scalars, distinct maximum bodies, legacy UTF-8/24-bit migration and corrupt encodings passed; 2,632 retained bytes per maximum body.");
    }
}
