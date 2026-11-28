using System.Reflection;
using AnimalTown;
using AnimalTown.Online;

// Exercise the real transport without a database, wall-clock simulation sleeps
// or exposing its connection/session implementation as a production API.
static class TransportMovementChecks
{
    static readonly Type Transport=Assembly.Load("AnimalTown.Server").GetType("LiveTownTransport",true)!;
    static readonly Type Actor=Transport.GetNestedType("Actor",BindingFlags.NonPublic)!;
    static readonly Type Town=Transport.GetNestedType("TownSession",BindingFlags.NonPublic)!;
    const BindingFlags Constructors=BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Instance;
    static T Read<T>(object owner,string field)=>(T)owner.GetType().GetField(field)!.GetValue(owner)!;
    static void Set(object owner,string field,object value)=>owner.GetType().GetField(field)!.SetValue(owner,value);
    static void Check(bool value,string message){if(!value)throw new Exception(message);}
    static (object Town,object Actor) Fixture()
    {
        var actor=Activator.CreateInstance(Actor,Constructors,null,new object?[]{null,(byte)0,36.5f,11.5f},null)!;
        var town=Activator.CreateInstance(Town,Constructors,null,new object[]{new GameState(1838),Guid.NewGuid(),1u},null)!;
        Read<Array>(town,"Actors").SetValue(actor,0);return(town,actor);
    }
    static void Tick(object town,long now)=>Transport.GetMethod("StepTown",BindingFlags.NonPublic|BindingFlags.Static)!.Invoke(null,new object[]{town,now});

    internal static async Task Run()
    {
        var (town,actor)=Fixture();
        var frame=new byte[15];OnlineWire.TryWriteInput(frame,0,frame.Length,1,7,new(0,127,true));
        using(var input=new DelayedPayload(frame))
        {
            var transport=Activator.CreateInstance(Transport,Constructors,null,new object?[]{null,null,null},null)!;
            var receiving=(Task)Transport.GetMethod("Receive",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(transport,new object[]{input,new byte[4108],town,actor,CancellationToken.None})!;
            await receiving;
            Check(Read<uint>(actor,"LastSeen")==7,"Fragmented movement did not reach the real transport");
            Check(Read<long>(actor,"LastInputAt")>=input.DeliveredAt,"Movement aged while its payload was still arriving");
        }
        Set(actor,"Applied",0u);Set(actor,"LastInputAt",1000L);
        Tick(town,1300);
        Check(Read<uint>(actor,"Applied")==7,"An expired accepted sample was never acknowledged");
        Check(Read<float>(actor,"X")==36.5f&&Read<float>(actor,"Z")==11.5f,"Expired movement changed the authoritative position");
        Check(Read<OnlineMovementInput>(actor,"Input").Z==0,"Expired movement remained held");
        Set(actor,"LastSeen",8u);Set(actor,"LastInputAt",1350L);Set(actor,"Input",default(OnlineMovementInput));
        Tick(town,1400);Check(Read<uint>(actor,"Applied")==8,"Fresh input stopped being acknowledged");
        Set(town,"Reviewing",true);Set(actor,"LastSeen",9u);Set(actor,"LastInputAt",1450L);Set(actor,"Input",new OnlineMovementInput(0,127,true));
        Tick(town,1500);Check(Read<uint>(actor,"Applied")==8,"Review advanced paused movement");
        Set(town,"Reviewing",false);Tick(town,1800);
        Check(Read<uint>(actor,"Applied")==9,"Review resume stranded an accepted sample");
        Check(Read<float>(actor,"X")==36.5f&&Read<float>(actor,"Z")==11.5f,"Review resume replayed expired movement");
        Console.WriteLine("Real transport movement: fragmented payload timing, expired acknowledgments, stopped input and review resume passed.");
    }

    sealed class DelayedPayload(byte[] frame):Stream
    {
        int at;internal long DeliveredAt;
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer,CancellationToken cancellationToken=default)
        {
            if(at==12){await Task.Delay(350,cancellationToken);DeliveredAt=Environment.TickCount64;}
            int count=Math.Min(buffer.Length,frame.Length-at);frame.AsMemory(at,count).CopyTo(buffer);at+=count;return count;
        }
        public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
        public override long Length=>throw new NotSupportedException();
        public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
        public override int Read(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
        public override void Flush()=>throw new NotSupportedException();
        public override long Seek(long offset,SeekOrigin origin)=>throw new NotSupportedException();
        public override void SetLength(long length)=>throw new NotSupportedException();
        public override void Write(byte[] buffer,int offset,int count)=>throw new NotSupportedException();
    }
}
