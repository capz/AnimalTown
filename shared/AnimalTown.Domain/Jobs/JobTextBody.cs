namespace AnimalTown.Jobs;

// ASCII and ordinary letters stay packed UTF-8. Supplementary-heavy messages
// use three bytes per scalar when smaller, bounding a 1,000-scalar body at 3 KB.
// The negative leading word is an explicit encoding tag, not a truncated string.
internal static class JobTextBody
{
    internal static int[] Pack(string text,int scalars)
    {
        var sizing=new JobWireWriter();sizing.Text(text);
        if((sizing.Count-1)*4<=scalars*3){var utf8=new int[sizing.Count];new JobWireWriter(utf8).Text(text);return utf8;}
        var data=new int[1+(scalars*3+3)/4];data[0]=-1-scalars;int at=0;
        void Byte(int value){data[1+at/4]|=value<<(at%4*8);at++;}
        for(int i=0;i<text.Length;i++){int cp=text[i];if(cp>=0xd800&&cp<=0xdbff)cp=0x10000+((cp-0xd800)<<10)+text[++i]-0xdc00;Byte(cp&255);Byte(cp>>8&255);Byte(cp>>16);}
        return data;
    }
    internal static void Validate(int[] body,int offset=0,int words=-1)
    {
        if(words<0)words=body.Length;
        if(words<1)throw new InvalidOperationException("Empty packed letter");
        if(body[offset]>=0){if(body[offset]>4000||words!=1+(body[offset]+3)/4)throw new InvalidOperationException("Invalid UTF-8 letter length");var r=new JobWireReader(body,offset,words);r.ValidateText(1000);r.End();return;}
        int count=-1-body[offset];if(count<0||count>1000||words!=1+(count*3+3)/4)throw new InvalidOperationException("Invalid scalar-packed letter");
        for(int i=0;i<count;i++){int cp=Scalar(body,offset,i);if(cp>0x10ffff||cp>=0xd800&&cp<=0xdfff)throw new InvalidOperationException("Invalid packed Unicode scalar");}
    }
    private static int Scalar(int[] body,int offset,int scalar)
    {int at=scalar*3,cp=0;for(int i=0;i<3;i++){int index=at+i;cp|=(int)((uint)body[offset+1+index/4]>>(index%4*8)&255)<<(i*8);}return cp;}
    internal static string Unpack(int[] body,int offset=0,int words=-1)
    {
        if(words<0)words=body.Length;
        if(body[offset]>=0){var r=new JobWireReader(body,offset,words);string text=r.Text(1000);r.End();return text;}
        int count=-1-body[offset];var chars=new char[count*2];int at=0;
        for(int i=0;i<count;i++){int cp=Scalar(body,offset,i);if(cp<65536)chars[at++]=(char)cp;else{cp-=0x10000;chars[at++]=(char)(0xd800+(cp>>10));chars[at++]=(char)(0xdc00+(cp&1023));}}
        return new string(chars,0,at);
    }
}
