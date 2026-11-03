internal static class WrappedTextChecks
{
    internal static void Run(Action<bool,string> check)
    {
        string[] messages={"","One word","A quiet moment. Try the store, town hall, or your neighbours.","Stick: walk / menus. D-pad: menus. L/R: tabs. B: run / back. A: interact / select. X: backpack. Y: map. Z: tool. START: save / settings.","Garden table","A long word: "+new string('W',90)+" after", " leading  and   repeated    spaces ","Explicit\nline\r\nbreaks\tand café"};
        foreach(int columns in new[]{1,14,41,48}){
            var cache=new WrappedText(columns);
            foreach(string message in messages){
                string wrapped=cache.Get(message);
                check(wrapped==Legacy(message,columns)&&wrapped.Length==message.Length,"linear wrapping preserves existing line breaks and character count");
                for(int end=0;end<=message.Length;end++)check(wrapped.Substring(0,end)==Legacy(message.Substring(0,end),columns),"revealing a cached prefix matches wrapping the visible substring at every character");
                check(ReferenceEquals(wrapped,cache.Get(message)),"unchanged text reuses its wrapped string");
                check(ReferenceEquals(wrapped,cache.Get(new string(message.ToCharArray()))),"equal text from a different source reuses its wrapped string");
            }
        }
        var warm=new WrappedText(41);string original=warm.Get(messages[2]);long before=GC.GetAllocatedBytesForCurrentThread();
        bool same=true;for(int i=0;i<10000;i++)same&=ReferenceEquals(original,warm.Get(messages[2]));
        long bytes=GC.GetAllocatedBytesForCurrentThread()-before;
        check(same&&bytes==0,"ten thousand warm UI wrap requests allocate no managed bytes");
        check(warm.Get(messages[3])==Legacy(messages[3],41)&&warm.Get(messages[2])==original,"new messages invalidate the single cached value correctly");
        bool invalid=false;try{new WrappedText(0);}catch(ArgumentOutOfRangeException){invalid=true;}check(invalid,"invalid wrap width is rejected");
    }
    private static string Legacy(string text,int columns){string result="";int col=0;foreach(char c in text){if(col>=columns&&c==' '){result+="\n";col=0;}else{result+=new string(new[]{c});col++;}}return result;}
}
