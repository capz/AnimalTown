#if ANIMALTOWN_ONLINE_PLAY
using System;
using System.Collections.Generic;
using Dolphin.Serialization.Json;

// Small control replies only. Worlds and private saves use bounded binary
// formats. This tree cannot consume an unbounded friends list or nested reply.
internal sealed class OnlineJson
{
    internal readonly JsonTokenType Kind;
    internal readonly string Value;
    internal readonly List<OnlineJson> Children=new List<OnlineJson>();
    readonly List<string> names=new List<string>();
    OnlineJson(JsonTokenType kind,string value){Kind=kind;Value=value;}
    internal static OnlineJson Read(string json)
    {
        if(json.Length>16384)throw new FormatException("Online reply exceeds its bound.");
        var reader=new JsonReader(json);int budget=256;if(!reader.Read())throw new FormatException("Empty online reply.");
        var result=Node(reader,0,ref budget);if(reader.Read())throw new FormatException("Trailing online reply.");return result;
    }
    static OnlineJson Node(JsonReader reader,int depth,ref int budget)
    {
        if(depth>5||--budget<0)throw new FormatException("Online reply exceeds its bound.");
        var node=new OnlineJson(reader.TokenType,reader.Value);
        if(node.Kind==JsonTokenType.StartObject||node.Kind==JsonTokenType.StartArray)
        {
            var end=node.Kind==JsonTokenType.StartObject?JsonTokenType.EndObject:JsonTokenType.EndArray;
            while(reader.Read()&&reader.TokenType!=end)
            {
                if(node.Kind==JsonTokenType.StartObject)
                {
                    if(reader.TokenType!=JsonTokenType.PropertyName||node.names.Contains(reader.Value))throw new FormatException("Invalid online reply field.");
                    node.names.Add(reader.Value);if(!reader.Read())throw new FormatException("Incomplete online reply.");
                }
                node.Children.Add(Node(reader,depth+1,ref budget));
            }
            if(reader.TokenType!=end)throw new FormatException("Incomplete online reply.");
        }
        return node;
    }
    internal OnlineJson Field(string name)
    {int index=names.IndexOf(name);if(index<0)throw new FormatException("Missing online reply field.");return Children[index];}
    internal bool Null=>Kind==JsonTokenType.Null;
    internal bool Flag=>Kind==JsonTokenType.True?true:Kind==JsonTokenType.False?false:throw new FormatException("Invalid online flag.");
    internal string Text=>Kind==JsonTokenType.String?Value:throw new FormatException("Invalid online text.");
    internal long Number
    {
        get
        {
            if(Kind!=JsonTokenType.Number||Value.Length==0||Value.Length>19)throw new FormatException("Invalid online number.");
            long value=0;for(int i=0;i<Value.Length;i++){int digit=Value[i]-'0';if(digit<0||digit>9||value>(long.MaxValue-digit)/10)throw new FormatException("Invalid online number.");value=value*10+digit;}return value;
        }
    }
    internal int Int32
    {get{long value=Number;if(value>int.MaxValue)throw new FormatException("Online number exceeds its range.");return (int)value;}}
}
#endif
