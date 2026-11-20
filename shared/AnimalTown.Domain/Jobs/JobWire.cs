namespace AnimalTown.Jobs;

// The economic save extension uses a sizing pass, then one exact allocation.
// Words match the existing explicit LE codecs; text is packed UTF-8, not four
// bytes per UTF-16 code unit. Every count is bounded before allocation/read.
internal sealed class JobWireWriter
{
    internal int Count;
    internal readonly int[]? Data;
    private readonly byte[]? bytes;private readonly int byteOffset;
    internal JobWireWriter(int[]? data=null){Data=data;}
    internal JobWireWriter(byte[] bytes,int offset){this.bytes=bytes;byteOffset=offset;}
    internal void Int(int n){if(Data!=null)Data[Count]=n;if(bytes!=null){int start=byteOffset+Count*4;for(int i=0;i<4;i++)bytes[start+i]=(byte)((uint)n>>(i*8));}Count++;}
    internal void Long(long n){ULong(unchecked((ulong)n));}
    internal void ULong(ulong n){Int(unchecked((int)n));Int(unchecked((int)(n>>32)));}
    internal void Key(JobIdentity key){ULong(key.Town);ULong(key.Serial);}
    internal void Block(int[]? block,int offset=0,int words=-1){int count=block==null?0:words<0?block.Length:words;Int(count);if(block!=null)for(int i=0;i<count;i++)Int(block[offset+i]);}
    internal void Text(string text)
    {
        int length=0;for(int i=0;i<text.Length;i++){int c=text[i];if(c<128)length++;else if(c<2048)length+=2;else if(c>=0xd800&&c<=0xdbff&&i+1<text.Length&&text[i+1]>=0xdc00&&text[i+1]<=0xdfff){length+=4;i++;}else length+=3;}
        Int(length);int word=0,shift=0;
        void Byte(int b){word|=b<<shift;shift+=8;if(shift==32){Int(word);word=0;shift=0;}}
        for(int i=0;i<text.Length;i++){
            int c=text[i];if(c<128)Byte(c);
            else if(c<2048){Byte(0xc0|(c>>6));Byte(0x80|(c&63));}
            else if(c>=0xd800&&c<=0xdbff&&i+1<text.Length&&text[i+1]>=0xdc00&&text[i+1]<=0xdfff){c=0x10000+((c-0xd800)<<10)+text[++i]-0xdc00;Byte(0xf0|(c>>18));Byte(0x80|((c>>12)&63));Byte(0x80|((c>>6)&63));Byte(0x80|(c&63));}
            else{Byte(0xe0|(c>>12));Byte(0x80|((c>>6)&63));Byte(0x80|(c&63));}
        }
        if(shift!=0)Int(word);
    }
}
internal sealed class JobWireReader
{
    private readonly int[]? data;
    private readonly byte[]? bytes;private readonly int offset,length;
    private int at;
    internal JobWireReader(int[] data){this.data=data;length=data.Length;}
    internal JobWireReader(int[] data,int offset,int words){if(offset<0||words<0||words>data.Length-offset)throw new InvalidOperationException("Invalid text slice");this.data=data;this.offset=offset;length=words;}
    internal int Position{get=>at;set{if(value<0||value>length)throw new InvalidOperationException("Invalid ledger position");at=value;}}
    internal void Skip(int words){if(words<0||words>length-at)throw new InvalidOperationException("Truncated ledger block");at+=words;}
    internal JobWireReader(byte[] bytes,int offset,int words){if(offset<0||words<0||words>(bytes.Length-offset)/4)throw new InvalidOperationException("Invalid job ledger bounds");this.bytes=bytes;this.offset=offset;length=words;}
    private int Word(int index){if(data!=null)return data[offset+index];int n=0,start=offset+index*4;for(int i=0;i<4;i++)n|=bytes![start+i]<<(i*8);return n;}
    internal int Int(){if(at>=length)throw new InvalidOperationException("Truncated job ledger");return Word(at++);}
    internal int Range(int minimum,int maximum){int n=Int();if(n<minimum||n>maximum)throw new InvalidOperationException("Invalid job ledger value");return n;}
    internal ulong ULong(){uint lo=unchecked((uint)Int()),hi=unchecked((uint)Int());return lo|((ulong)hi<<32);}
    internal long Long()=>unchecked((long)ULong());
    internal JobIdentity Key(bool empty=true){var key=new JobIdentity(ULong(),ULong());if(!key.IsValid&&!(empty&&key.IsEmpty))throw new InvalidOperationException("Invalid saved job identity");return key;}
    internal int[]? Block(int maximumWords,int[]? reuse,int reuseOffset,int reuseWords,out int bodyOffset,out int bodyWords)
    {int count=Range(0,maximumWords);bodyOffset=0;bodyWords=count;if(count==0)return null;if(count>length-at)throw new InvalidOperationException("Truncated packed job data");if(reuse!=null&&reuseWords==count){bool same=true;for(int i=0;i<count;i++)if(Word(at+i)!=reuse[reuseOffset+i]){same=false;break;}if(same){at+=count;bodyOffset=reuseOffset;return reuse;}}var block=new int[count];for(int i=0;i<count;i++)block[i]=Int();return block;}

    internal string Text(int maximumScalars)=>ReadText(maximumScalars,true);
    internal void ValidateText(int maximumScalars){ReadText(maximumScalars,false);}
    private string ReadText(int maximumScalars,bool materialize)
    {
        int length=Range(0,maximumScalars*4);if((length+3)/4>this.length-at)throw new InvalidOperationException("Truncated job text");
        var chars=materialize?new char[length]:null;int count=0,scalarCount=0;
        int Byte(int index)=>(int)((uint)Word(at+index/4)>>(8*(index%4)))&255;
        for(int i=0;i<length;){
            int lead=Byte(i++),c,following,min;
            if(lead<128){c=lead;following=0;min=0;}
            else if(lead>=0xc2&&lead<=0xdf){c=lead&31;following=1;min=128;}
            else if(lead>=0xe0&&lead<=0xef){c=lead&15;following=2;min=2048;}
            else if(lead>=0xf0&&lead<=0xf4){c=lead&7;following=3;min=65536;}
            else throw new InvalidOperationException("Invalid UTF-8 job text");
            if(i+following>length)throw new InvalidOperationException("Truncated UTF-8 job text");
            for(int j=0;j<following;j++){int b=Byte(i++);if((b&0xc0)!=0x80)throw new InvalidOperationException("Invalid UTF-8 continuation");c=(c<<6)|(b&63);}
            if(c<min||c>0x10ffff||c>=0xd800&&c<=0xdfff||++scalarCount>maximumScalars)throw new InvalidOperationException("Invalid Unicode scalar in job text");
            if(chars!=null){if(c<65536)chars[count++]=(char)c;else{c-=65536;chars[count++]=(char)(0xd800+(c>>10));chars[count++]=(char)(0xdc00+(c&1023));}}
        }
        at+=(length+3)/4;return chars==null?"":new string(chars,0,count);
    }
    internal void End(){if(at!=length)throw new InvalidOperationException("Trailing job ledger data");}
}
