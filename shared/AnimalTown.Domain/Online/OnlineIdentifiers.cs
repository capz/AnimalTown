namespace AnimalTown.Online;

public static class OnlineIdentifiers
{
    static readonly int[] positions={6,4,2,0,11,9,16,14,19,21,24,26,28,30,32,34};
    static int Hex(char c)=>c>='0'&&c<='9'?c-'0':c>='a'&&c<='f'?c-'a'+10:c>='A'&&c<='F'?c-'A'+10:-1;
    public static OnlineId Parse(string text)
    {
        if(text.Length!=36||text[8]!='-'||text[13]!='-'||text[18]!='-'||text[23]!='-')throw new FormatException("Invalid online identity.");
        ulong first=0,second=0;
        for(int i=0;i<16;i++)
        {
            int p=positions[i],high=Hex(text[p]),low=Hex(text[p+1]);if(high<0||low<0)throw new FormatException("Invalid online identity.");
            ulong value=(ulong)(high*16+low);if(i<8)first|=value<<(i*8);else second|=value<<((i-8)*8);
        }
        return new(first,second);
    }
    public static string Format(OnlineId id)
    {
        const string digits="0123456789abcdef";var text=new char[36];text[8]=text[13]=text[18]=text[23]='-';
        for(int i=0;i<16;i++){int value=(int)((i<8?id.First>>(i*8):id.Second>>((i-8)*8))&255),p=positions[i];text[p]=digits[value>>4];text[p+1]=digits[value&15];}
        return new string(text);
    }
}
