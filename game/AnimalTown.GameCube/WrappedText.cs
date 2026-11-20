using System;
using System.Text;

// Wrapping replaces spaces one-for-one, so typewriter character indices stay
// aligned with the original message. Cache one current value per UI surface.
internal sealed class WrappedText
{
    private readonly int columns;
    private string source="",wrapped="";
    internal WrappedText(int columns){if(columns<1)throw new ArgumentOutOfRangeException("columns");this.columns=columns;}
    internal string Get(string text)
    {
        if(source==text)return wrapped;
        var chars=new char[text.Length];int col=0;
        for(int i=0;i<text.Length;i++){
            char c=text[i];
            if(col>=columns&&c==' '){chars[i]='\n';col=0;}
            else{chars[i]=c;col++;}
        }
        source=text;wrapped=new string(chars);return wrapped;
    }
}

// Mail is user-authored: explicit newlines and words wider than the panel must
// both be bounded. Unlike typewriter dialogue, this may insert line breaks.
internal sealed class TextPages
{
    private readonly int columns;
    private string source="",wrapped="";
    internal int Lines { get; private set; }=1;
    internal TextPages(int columns){if(columns<1)throw new ArgumentOutOfRangeException("columns");this.columns=columns;}
    internal void Set(string text)
    {
        if(source==text)return;
        var result=new StringBuilder(text.Length+text.Length/columns);int col=0,space=-1;Lines=1;
        for(int i=0;i<text.Length;i++){
            char c=text[i];if(c=='\r')continue;
            if(c=='\n'){result.Append(c);col=0;space=-1;Lines++;continue;}
            // IPL has no supplementary-plane glyphs. Display one replacement
            // per scalar while retaining the complete original saved body.
            if(c>='\ud800'&&c<='\udbff'&&i+1<text.Length&&text[i+1]>='\udc00'&&text[i+1]<='\udfff'){c='?';i++;}
            int width=c>=0x1100?2:1;
            if(col+width>columns){
                if(space>=0){result[space]='\n';col=0;for(int at=space+1;at<result.Length;at++)col+=result[at]>=0x1100?2:1;}
                else{result.Append('\n');col=0;}
                space=-1;Lines++;
            }
            if(c==' ')space=result.Length;
            result.Append(c);col+=width;
        }
        source=text;wrapped=result.ToString();
    }
    internal string Window(int first,int count)
    {
        int start=0,end=0;
        for(int i=0;i<first&&start<wrapped.Length;i++){int at=wrapped.IndexOf('\n',start);start=at<0?wrapped.Length:at+1;}
        end=start;for(int i=0;i<count&&end<wrapped.Length;i++){int at=wrapped.IndexOf('\n',end);if(at<0){end=wrapped.Length;break;}if(i==count-1){end=at;break;}end=at+1;}
        return wrapped.Substring(start,end-start);
    }
}
