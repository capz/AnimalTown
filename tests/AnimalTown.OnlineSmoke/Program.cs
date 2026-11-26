using AnimalTown.Online;
using Dolphin.Timing;

static class Program
{
    static int Main()
    {
        if (OnlineWebSocketUpgrade.AcceptKey("dGhlIHNhbXBsZSBub25jZQ==") != "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=") return 1;
        var command = new OnlineCommand { Kind = OnlineCommandKind.SendMail, Id = new(0x102030405060708, 9), Text = "Hello from a native client.", B = -1 };
        var bytes = new byte[4096]; int count = OnlineGameplayCodec.WriteCommand(bytes, command);
        var decoded = OnlineGameplayCodec.ReadCommand(bytes, 0, count);
        if (!decoded.Id.Same(command.Id) || decoded.Text != command.Text || decoded.B != -1) return 2;
        var wire = new Wire(); using var socket = new OnlineWebSocketStream(wire,new Masks());
        byte[] payload = { 11,22,33 }; if (socket.TryWrite(payload,0,3)!=3||wire.Count!=9||wire.Bytes[0]!=130||wire.Bytes[1]!=131) return 3;
        for(int i=0;i<3;i++)if((byte)(wire.Bytes[6+i]^wire.Bytes[2+i])!=payload[i])return 4;
#if ONLINE_ROOT_CONNECTION
        // Root the actual SDK connection path for AOT validation without opening
        // a network connection in a pure host framing fixture.
        if (Clock.Timestamp == long.MinValue)
            OnlineGameplayConnection.ConnectAsync("https://town.example", new byte[32], new OnlineVisitState(), default).GetAwaiter().GetResult().Dispose();
#endif
        Console.WriteLine("Native online protocol checks passed."); return 0;
    }
    sealed class Masks:IOnlineMaskSource {public uint NextMask()=>0x10203040;}
    sealed class Wire:IOnlinePoseStream
    {
        internal readonly byte[] Bytes=new byte[256];internal int Count;
        public int TryRead(byte[] bytes,int offset,int count)=>-1;
        public int TryWrite(byte[] bytes,int offset,int count){Array.Copy(bytes,offset,Bytes,Count,count);Count+=count;return count;}
    }
}
