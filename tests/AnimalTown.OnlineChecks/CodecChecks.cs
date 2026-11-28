using AnimalTown.Online;

static class CodecChecks
{
    public static void Run(Action<bool,string> check)
    {
        var command = new OnlineCommand { Kind=OnlineCommandKind.SendMail,Id=new(0x0807060504030201,0xfffefdfcfbfaf9f8),
            Entity=new(17,23),Revision=long.MaxValue,A=-1,B=int.MinValue,C=int.MaxValue,D=24,E=7,F=3,
            Text=new string('a',998)+"\U0001f330" };
        var bytes = new byte[4096]; int count = OnlineGameplayCodec.WriteCommand(bytes,command);
        var read = OnlineGameplayCodec.ReadCommand(bytes,0,count);
        check(read.Id.Same(command.Id)&&read.Entity.Same(command.Entity)&&read.Kind==command.Kind&&
            read.Revision==command.Revision&&read.A==-1&&read.B==int.MinValue&&read.C==int.MaxValue&&
            read.D==24&&read.E==7&&read.F==3&&read.Text==command.Text,"Binary command preserves IDs, integer bounds and Unicode mail");
        for(int i=0;i<count;i++)
        {
            bool rejected=false;try{OnlineGameplayCodec.ReadCommand(bytes,0,i);}catch(InvalidOperationException){rejected=true;}
            check(rejected,"Every truncated command fails closed");
        }
        bool extra=false;try{OnlineGameplayCodec.ReadCommand(bytes,0,count+1);}catch(InvalidOperationException){extra=true;}
        check(extra,"Trailing command bytes fail closed");
        bytes[1]=255;bool unknown=false;try{OnlineGameplayCodec.ReadCommand(bytes,0,count);}catch(InvalidOperationException){unknown=true;}
        check(unknown,"Excluded and unknown action opcodes fail closed");
        command.Text="\ud800";bool malformed=false;try{OnlineGameplayCodec.WriteCommand(bytes,command);}catch(InvalidOperationException){malformed=true;}
        check(malformed,"Unpaired Unicode surrogates cannot reach mail");

        var stream=new PumpWire();var sink=new Sink();using var pump=new OnlinePosePump(stream,new byte[32],0,sink);
        check(!pump.Queue(OnlineFrameType.Command,new byte[1],0,1,out _),"Commands wait for authenticated epoch");
        stream.Feed(OnlineFrameType.Pong,73,1,[]);pump.Pump(0);stream.ClearSent();
        check(pump.Epoch==73&&pump.LocalSlot==1,"Pump takes its epoch and seat from the authenticated hello");
        byte[] payload=[4,5,6];check(pump.Queue(OnlineFrameType.Command,payload,0,3,out uint sequence),"Pump queues an owned command");
        payload[0]=99;check(!pump.Queue(OnlineFrameType.Command,payload,0,3,out _),"Only one command can wait behind backpressure");
        pump.Pump(1);var queued=stream.TakeSent();
        check(OnlineWire.TryReadHeader(queued,0,queued.Length,out var header)&&header.Sequence==sequence&&queued[12]==4,
            "Command input is copied once and uses the movement sequence space");
        var maximum=new char[2000];for(int i=0;i<1000;i++){maximum[i*2]='\ud83c';maximum[i*2+1]='\udf30';}
        command.Text=new string(maximum);count=OnlineGameplayCodec.WriteCommand(bytes,command);
        check(count>512&&pump.Queue(OnlineGameplayCodec.CommandFrame(count),bytes,0,count,out _),"The production queue accepts a maximum Unicode letter");
        pump.Pump(2);queued=stream.TakeSent();
        check(OnlineWire.ReadFrame(queued,0,queued.Length,out header,out int used)==OnlineFrameRead.Complete&&used==queued.Length&&header.Type==OnlineFrameType.BulkPage&&
            OnlineGameplayCodec.ReadCommand(queued,12,header.PayloadLength).Text==command.Text,"Maximum mail leaves the queue in a bounded complete gameplay frame");
        stream.Feed(OnlineFrameType.ReliableEvent,73,9,[17,18]);pump.Pump(2);
        check(sink.Count==1&&sink.Last==17,"Reliable messages reach the consumer before borrowed input is reused");
        stream.Feed(OnlineFrameType.SessionState,73,10,[1]);pump.Pump(3);
        check(pump.Reviewing&&!pump.Queue(OnlineFrameType.Command,payload,0,3,out _),"Review blocks guest commands");
        pump.Dispose();check(!pump.Queue(OnlineFrameType.Command,payload,0,3,out _)&&!pump.TryGetActor(0,3,out _),"Disposed gameplay state cannot be reused");
    }
    sealed class Sink:IOnlineMessageSink
    {public int Count,Last;public bool Apply(OnlineFrameHeader header,byte[] bytes,int offset,int count){Count++;Last=bytes[offset];return true;}}
    sealed class PumpWire:IOnlinePoseStream
    {
        readonly byte[] input=new byte[8192],output=new byte[8192];int at,left,written;
        public void Feed(OnlineFrameType kind,uint epoch,uint sequence,byte[] payload)
        {OnlineWire.TryWriteHeader(input,0,input.Length,new(kind,payload.Length,epoch,sequence));payload.CopyTo(input,12);at=0;left=12+payload.Length;}
        public void ClearSent()=>written=0;
        public byte[] TakeSent(){var result=output[..written];written=0;return result;}
        public int TryRead(byte[] buffer,int offset,int count){if(left==0)return -1;int take=Math.Min(count,left);Array.Copy(input,at,buffer,offset,take);at+=take;left-=take;return take;}
        public int TryWrite(byte[] buffer,int offset,int count){Array.Copy(buffer,offset,output,written,count);written+=count;return count;}
    }
}
