namespace AnimalTown.Jobs;

// Keep UTF-8 when smaller; otherwise use the 21 bits needed by a Unicode
// scalar. The disjoint negative tag preserves old three-byte scalar bodies.
internal static class JobTextBody
{
    private const int BitsTag=-1002;
    private static int PackedWords(int count)=>1+(count*21+31)/32;
    private static int Count(int tag)=>tag<=BitsTag?BitsTag-tag:-1-tag;
    private static void PutScalar(int[] data,int index,int cp)
    {
        int bit=index*21,word=1+bit/32;bit%=32;
        data[word]|=unchecked((int)((uint)cp<<bit));
        if(bit>11)data[word+1]|=(int)((uint)cp>>(32-bit));
    }
    internal static int[] Pack(string text,int scalars)
    {
        if(scalars<0||scalars>1000)throw new InvalidOperationException("Packed letter exceeds its scalar bound");
        var sizing=new JobWireWriter();sizing.Text(text);
        if(sizing.Count<=PackedWords(scalars)){var utf8=new int[sizing.Count];new JobWireWriter(utf8).Text(text);return utf8;}
        var data=new int[PackedWords(scalars)];data[0]=BitsTag-scalars;int at=0;
        for(int i=0;i<text.Length;i++){int cp=text[i];if(cp>=0xd800&&cp<=0xdbff)cp=0x10000+((cp-0xd800)<<10)+text[++i]-0xdc00;PutScalar(data,at++,cp);}
        return data;
    }
    internal static void Validate(int[] body,int offset=0,int words=-1)
    {
        if(words<0)words=body.Length;
        if(words<1)throw new InvalidOperationException("Empty packed letter");
        int tag=body[offset];
        if(tag>=0){if(tag>4000||words!=1+(tag+3)/4)throw new InvalidOperationException("Invalid UTF-8 letter length");var r=new JobWireReader(body,offset,words);r.ValidateText(1000);r.End();return;}
        if(tag<BitsTag-1000)throw new InvalidOperationException("Invalid scalar-packed letter tag");
        int count=Count(tag),expected=tag<=BitsTag?PackedWords(count):1+(count*3+3)/4;
        if(count<0||count>1000||words!=expected)throw new InvalidOperationException("Invalid scalar-packed letter");
        for(int i=0;i<count;i++){int cp=Scalar(body,offset,i);if(cp>0x10ffff||cp>=0xd800&&cp<=0xdfff)throw new InvalidOperationException("Invalid packed Unicode scalar");}
    }
    private static int Byte(int[] body,int offset,int index)=>(int)((uint)body[offset+1+index/4]>>(index%4*8)&255);
    private static int Scalar(int[] body,int offset,int scalar)
    {
        if(body[offset]<=BitsTag){
            int bit=scalar*21,word=offset+1+bit/32;bit%=32;uint cp=(uint)body[word]>>bit;
            if(bit>11)cp|=(uint)body[word+1]<<(32-bit);return (int)(cp&0x1fffff);
        }
        int at=scalar*3;return Byte(body,offset,at)|Byte(body,offset,at+1)<<8|Byte(body,offset,at+2)<<16;
    }
    // Decode old saves directly into the smaller retained form without a
    // temporary UTF-16 string. No repeated-body deduplication is assumed.
    internal static int[] Compact(int[] body)
    {
        if(body[0]<=BitsTag)return body;
        int count=body[0]<0?Count(body[0]):0;
        if(body[0]>=0)for(int i=0;i<body[0];i++)if((Byte(body,0,i)&0xc0)!=0x80)count++;
        if(PackedWords(count)>=body.Length)return body;
        var packed=new int[PackedWords(count)];packed[0]=BitsTag-count;
        if(body[0]<0){for(int i=0;i<count;i++)PutScalar(packed,i,Scalar(body,0,i));}
        else for(int at=0,index=0;at<body[0];index++){
            int lead=Byte(body,0,at++),cp=lead<128?lead:lead<224?lead&31:lead<240?lead&15:lead&7;
            int following=lead<128?0:lead<224?1:lead<240?2:3;
            for(int j=0;j<following;j++)cp=(cp<<6)|(Byte(body,0,at++)&63);PutScalar(packed,index,cp);
        }
        return packed;
    }
    internal static string Unpack(int[] body,int offset=0,int words=-1)
    {
        if(words<0)words=body.Length;
        if(body[offset]>=0){var r=new JobWireReader(body,offset,words);string text=r.Text(1000);r.End();return text;}
        int count=Count(body[offset]);var chars=new char[count*2];int at=0;
        for(int i=0;i<count;i++){int cp=Scalar(body,offset,i);if(cp<65536)chars[at++]=(char)cp;else{cp-=0x10000;chars[at++]=(char)(0xd800+(cp>>10));chars[at++]=(char)(0xdc00+(cp&1023));}}
        return new string(chars,0,at);
    }
}
