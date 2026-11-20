using System.Text;

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
        var pages=new TextPages(48);string full=string.Join("\n",Enumerable.Range(0,500).Select(i=>i.ToString()));pages.Set(full);
        check(pages.Lines==500&&pages.Window(492,8)==string.Join("\n",Enumerable.Range(492,8)),"all newline-heavy mail remains reachable in an eight-line panel");
        foreach(string body in new[]{new string('W',1000),string.Concat(Enumerable.Repeat("\U0001f642",1000)),"\U0001f642 hello "+string.Concat(Enumerable.Repeat("\U0001f642",100)),"one\r\ntwo\nthree"}){
            pages.Set(body);string[] lines=pages.Window(0,pages.Lines).Split('\n');
            check(lines.Length==pages.Lines&&lines.All(line=>line.EnumerateRunes().Count()<=48),"arbitrary Unicode words and explicit newlines fit the panel width");
            for(int row=0;row<pages.Lines;row++)check(pages.Window(row,4).Split('\n').Length<=4,"every message and draft window fits its allocated line count");
        }
        pages.Set(full);before=GC.GetAllocatedBytesForCurrentThread();for(int i=0;i<10000;i++)pages.Set(full);check(GC.GetAllocatedBytesForCurrentThread()==before,"unchanged paged text allocates nothing while drawing");
        pages.Set(string.Concat(Enumerable.Repeat("\U0001f642",1000)));check(pages.Window(0,pages.Lines).Count(c=>c=='?')==1000,"IPL displays one fallback per supplementary scalar without doubling the message width");
        pages.Set(new string('\u3042',1000));check(pages.Window(0,pages.Lines).Split('\n').All(line=>line.Length<=24),"wide IPL glyphs remain within the same body panel");
    }
    private static string Legacy(string text,int columns){string result="";int col=0;foreach(char c in text){if(col>=columns&&c==' '){result+="\n";col=0;}else{result+=new string(new[]{c});col++;}}return result;}
}
