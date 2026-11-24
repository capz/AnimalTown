namespace AnimalTown.Jobs;

public readonly struct JobLetterGrade
{
    public readonly string NormalizedBody;
    public readonly int ScalarCount,FilteredLength,Rank,Friendship;
    public readonly bool Quality;
    internal JobLetterGrade(string body,int scalars,int length,bool quality,bool present)
    {NormalizedBody=body;ScalarCount=scalars;FilteredLength=length;Quality=quality;Rank=(length<17?0:length<49?1:2)+(quality?3:0)+(present?6:0);Friendship=3-(quality?0:5)+(present?3:0);}
}

// Canonical normalization and scalar classification use compact pinned Unicode
// data. No ICU, culture-sensitive casing, reflection or desktop-only BCL calls.
public static class JobLetterGrader
{
    public static string UnicodeVersion=>JobUnicodeTables.Version;
    public static int UnicodeTableBytes=>JobUnicodeTables.DataBytes;
    public static bool TryGrade(string body,bool present,out JobLetterGrade grade)
    {
        grade=default;if(!TryNormalize(body,out string normalized,out int scalars))return false;
        int length=0,previous=-1,run=0;bool hasLetter=false,repeated=false;
        for(int i=0;i<normalized.Length;i++){
            int cp=normalized[i];if(cp>=0xd800&&cp<=0xdbff)cp=0x10000+((cp-0xd800)<<10)+normalized[++i]-0xdc00;
            if(InRanges(JobUnicodeTables.Ignored,cp))continue;
            length++;
            if(InRanges(JobUnicodeTables.LettersAndDigits,cp)){hasLetter=true;run=previous==cp?run+1:1;previous=cp;if(run>=4)repeated=true;}
            else{previous=-1;run=0;}
        }
        grade=new JobLetterGrade(normalized,scalars,length,length>=5&&hasLetter&&!repeated,present);return true;
    }
    public static bool TryNormalize(string input,out string normalized,out int scalarCount)
    {
        normalized="";scalarCount=0;
        if(input.Length>JobRulesV1.LetterMaximumScalars*JobUnicodeTables.MaximumCanonicalExpansion*2)return false;
        var values=new int[JobRulesV1.LetterMaximumScalars*JobUnicodeTables.MaximumCanonicalExpansion];int count=0;
        for(int i=0;i<input.Length;i++){
            int cp=input[i];
            if(cp>=0xd800&&cp<=0xdbff){if(i+1>=input.Length||input[i+1]<0xdc00||input[i+1]>0xdfff)return false;cp=0x10000+((cp-0xd800)<<10)+input[++i]-0xdc00;}
            else if(cp>=0xdc00&&cp<=0xdfff)return false;
            if(!Decompose(cp,values,ref count))return false;
        }
        Order(values,count);
        int output=0,starter=-1,lastClass=0;
        for(int i=0;i<count;i++){
            int cp=values[i],cc=CombiningClass(cp),composed=starter>=0&&(lastClass<cc||lastClass==0)?Compose(values[starter],cp):0;
            if(composed!=0)values[starter]=composed;
            else{if(cc==0)starter=output;values[output++]=cp;lastClass=cc;}
        }
        if(output>JobRulesV1.LetterMaximumScalars)return false;
        var chars=new char[output*2];int at=0;
        for(int i=0;i<output;i++){int cp=values[i];if(cp<65536)chars[at++]=(char)cp;else{cp-=0x10000;chars[at++]=(char)(0xd800+(cp>>10));chars[at++]=(char)(0xdc00+(cp&1023));}}
        normalized=new string(chars,0,at);scalarCount=output;return true;
    }
    private static bool Append(int cp,int[] values,ref int count)
    {if(count==values.Length)return false;values[count++]=cp;return true;}
    private static bool Decompose(int cp,int[] values,ref int count)
    {
        int hangul=cp-0xac00;
        if(hangul>=0&&hangul<11172){if(!Append(0x1100+hangul/588,values,ref count)||!Append(0x1161+hangul%588/28,values,ref count))return false;int t=hangul%28;return t==0||Append(0x11a7+t,values,ref count);}
        var table=JobUnicodeTables.Decompositions;int lo=0,hi=table.Length/3-1;
        while(lo<=hi){int mid=(lo+hi)/2,at=mid*3,c=table[at];if(c<cp)lo=mid+1;else if(c>cp)hi=mid-1;else return Decompose(table[at+1],values,ref count)&&(table[at+2]==0||Decompose(table[at+2],values,ref count));}
        return Append(cp,values,ref count);
    }
    private static int CombiningClass(int cp)
    {
        var table=JobUnicodeTables.CombiningClasses;int lo=0,hi=table.Length/3-1;
        while(lo<=hi){int mid=(lo+hi)/2,at=mid*3;if(cp<table[at])hi=mid-1;else if(cp>table[at+1])lo=mid+1;else return table[at+2];}return 0;
    }
    private static void Order(int[] values,int count)
    {
        int start=0;int[]? temporary=null;int[]? buckets=null;
        while(start<count){
            if(CombiningClass(values[start])==0)start++;
            int end=start,last=0;bool ordered=true;
            while(end<count){int cc=CombiningClass(values[end]);if(cc==0)break;if(cc<last)ordered=false;last=cc;end++;}
            if(!ordered){
                if(end-start<=32){for(int i=start+1;i<end;i++){int cp=values[i],cc=CombiningClass(cp),j=i;while(j>start&&CombiningClass(values[j-1])>cc){values[j]=values[j-1];j--;}values[j]=cp;}}
                else{
                    if(temporary==null){temporary=new int[count];buckets=new int[256];}
                    for(int i=0;i<256;i++)buckets![i]=0;
                    for(int i=start;i<end;i++)buckets![CombiningClass(values[i])]++;
                    int total=0;for(int i=0;i<256;i++){int n=buckets![i];buckets[i]=total;total+=n;}
                    for(int i=start;i<end;i++){int cp=values[i];temporary[start+buckets![CombiningClass(cp)]++]=cp;}
                    for(int i=start;i<end;i++)values[i]=temporary[i];
                }
            }
            start=end;
        }
    }
    private static int Compose(int first,int second)
    {
        int l=first-0x1100,v=second-0x1161;
        if(l>=0&&l<19&&v>=0&&v<21)return 0xac00+(l*21+v)*28;
        int s=first-0xac00,t=second-0x11a7;
        if(s>=0&&s<11172&&s%28==0&&t>0&&t<28)return first+t;
        var table=JobUnicodeTables.Compositions;int lo=0,hi=table.Length/3-1;
        while(lo<=hi){int mid=(lo+hi)/2,at=mid*3,a=table[at],b=table[at+1];if(a<first||a==first&&b<second)lo=mid+1;else if(a>first||a==first&&b>second)hi=mid-1;else return table[at+2];}return 0;
    }
    private static bool InRanges(int[] table,int cp)
    {int lo=0,hi=table.Length/2-1;while(lo<=hi){int mid=(lo+hi)/2,at=mid*2;if(cp<table[at])hi=mid-1;else if(cp>table[at+1])lo=mid+1;else return true;}return false;}
}
