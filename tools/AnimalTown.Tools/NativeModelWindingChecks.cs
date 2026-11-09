using System.Buffers.Binary;
internal static class NativeModelWindingChecks
{
    internal static void Run()
    {
        const int start=160,vertices=start+4+44,indices=vertices+3*80;
        var bytes=new byte[indices+6*4];
        void Put(int at,int value)=>BinaryPrimitives.WriteUInt32BigEndian(bytes.AsSpan(at,4),(uint)value);
        uint Hash(){uint h=2166136261;for(int i=start;i<bytes.Length;i++)h=unchecked((h^bytes[i])*16777619);return h;}
        Put(0,0x444e444d);Put(8,bytes.Length);Put(12,6);Put(36,start);Put(40,bytes.Length-start);
        Put(start,1);Put(start+4+12,3);Put(start+4+16,6);
        for(int i=0;i<3;i++){Put(vertices+i*80+16,BitConverter.SingleToInt32Bits(1));Put(vertices+i*80+24,BitConverter.SingleToInt32Bits(i*.25f));}
        Put(vertices+80,BitConverter.SingleToInt32Bits(1));Put(vertices+160+8,BitConverter.SingleToInt32Bits(1));
        int[] ids={0,1,2,0,2,1};for(int i=0;i<ids.Length;i++)Put(indices+i*4,ids[i]);Put(44,unchecked((int)Hash()));
        var original=(byte[])bytes.Clone();
        if(NativeModelWinding.Normalize(bytes)!=1)throw new Exception("Mixed source winding was not normalized");
        for(int at=0;at<bytes.Length;at++)if((at<44||at>=48)&&(at<indices+16||at>=indices+24)&&bytes[at]!=original[at])throw new Exception("Winding conversion altered non-index model data");
        var normalized=(byte[])bytes.Clone();if(NativeModelWinding.Normalize(bytes)!=0||!bytes.SequenceEqual(normalized))throw new Exception("Winding normalization is not idempotent");
        bytes[44]^=1;try{NativeModelWinding.Normalize(bytes);throw new Exception("Bad checksum accepted");}catch(InvalidDataException){}
        Console.WriteLine("Native winding: mixed faces become clockwise; vertices/UVs/normals and other sections stay byte-exact; idempotence and corrupt-section rejection passed");
    }
}
