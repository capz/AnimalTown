using System;

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
