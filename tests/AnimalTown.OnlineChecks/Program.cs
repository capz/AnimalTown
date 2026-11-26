using System.Text;
using System.Security.Cryptography;
using AnimalTown.Online;

int checks = 0;
void Check(bool condition, string reason) { checks++; if (!condition) throw new Exception(reason); }
Check(OnlineWebSocketUpgrade.AcceptKey("dGhlIHNhbXBsZSBub25jZQ==") == "s3pPLMBiTxaQ9kYGzzhZRbK+xOo=", "RFC 6455 published handshake vector");
var random = new Mask();
var handshake = new OnlineWebSocketUpgrade("https://town.example", random);
string request = Encoding.ASCII.GetString(handshake.Request);
string key = request.Split("Sec-WebSocket-Key: ")[1].Split("\r\n")[0];
Check(Convert.FromBase64String(key).Length == 16, "Fresh upgrade nonce is 16 bytes");
string response = "HTTP/1.1 101 Switching Protocols\r\nUpgrade: websocket\r\nConnection: keep-alive, Upgrade\r\nSec-WebSocket-Accept: " + OnlineWebSocketUpgrade.AcceptKey(key) + "\r\nSec-WebSocket-Protocol: animaltown.v1\r\n\r\n";
byte[] accepted = Encoding.ASCII.GetBytes(response + "extra");
Check(handshake.ReadResponse(accepted, accepted.Length, out int consumed) == OnlineFrameRead.Complete && consumed == accepted.Length - 5, "Upgrade preserves coalesced first gameplay bytes");
for (int i = 0; i < consumed; i++) Check(handshake.ReadResponse(accepted, i, out _) == OnlineFrameRead.Incomplete, "Every fragmented upgrade prefix stays incomplete");
foreach (string invalid in new[]{response.Replace("101 Switching Protocols", "200 OK"), response.Replace("websocket\r\n", "websocket, other\r\n"), response.Replace("animaltown.v1", "other"), response.Replace("keep-alive, Upgrade", "keep-alive"), response.Replace(OnlineWebSocketUpgrade.AcceptKey(key), "wrong"), response.Replace("\r\n\r\n", "\r\nSec-WebSocket-Accept: duplicate\r\n\r\n"), response.Replace("\r\n\r\n", "\r\nSec-WebSocket-Extensions: permessage-deflate\r\n\r\n"), response.Replace("\r\n\r\n", "\r\nTransfer-Encoding: chunked\r\n\r\n")})
    Check(handshake.ReadResponse(Encoding.ASCII.GetBytes(invalid), invalid.Length, out _) == OnlineFrameRead.Invalid, "Reject invalid upgrade/extension/protocol/framing");
Check(handshake.ReadResponse(new byte[8192],8192,out _) == OnlineFrameRead.Invalid,"Upgrade headers are bounded");

foreach(int quantum in new[]{1,3,127,8192})
{
    var wire = new Wire(quantum); var masks = new Mask(); var socket = new OnlineWebSocketStream(wire,masks);
    byte[] body = Enumerable.Range(0,300).Select(i=>(byte)i).ToArray();
    // Interleaved control frames must not alter a fragmented binary message.
    wire.Feed(Frame(2, body[..100], false).Concat(Frame(9,new byte[]{7,8,9})).Concat(Frame(0,body[100..])).ToArray());
    var read = new byte[300];int count=0;
    for(int i=0;i<10000&&count<read.Length;i++) { int n=socket.TryRead(read,count,read.Length-count);if(n>0)count+=n;else Check(n==-1,"Fragmented stream does not produce premature EOF"); }
    Check(count==body.Length&&read.SequenceEqual(body),"Fragmentation/control frames preserve the binary byte stream");
    for(int i=0;i<1000;i++)socket.TryRead(read,0,1);
    byte[] pong=wire.Take();Check(pong.Length==9&&pong[0]==138&&(pong[1]&128)!=0,"Pong is final, masked and has exact length");
    for(int i=0;i<3;i++)Check((byte)(pong[6+i]^pong[2+(i&3)])==7+i,"Pong echoes the ping payload");
    wire.BlockWrites=true;
    Check(socket.TryWrite(body,0,body.Length)==body.Length,"Writes can be accepted into bounded owned staging");
    int calls=masks.Calls;Check(socket.TryWrite(body,0,1)==-1&&masks.Calls==calls,"Backpressure preserves the pending frame and mask");
    wire.BlockWrites=false;
    for(int i=0;i<1000;i++)socket.TryRead(read,0,1);
    byte[] sent=wire.Take();Check(sent.Length==308&&sent[0]==130&&sent[1]==254&&sent[2]==1&&sent[3]==44,"Client extended-length binary framing follows RFC 6455");
    for(int i=0;i<body.Length;i++)Check((byte)(sent[8+i]^sent[4+(i&3)])==body[i],"Masking preserves every payload byte across short writes");
    byte[] close=Frame(8,new byte[]{3,232});wire.Feed(close);
    for(int i=0;i<1000&&!socket.Closed;i++)socket.TryRead(read,0,1);
    Check(socket.Closed&&!socket.ProtocolError,"Valid close finishes without a protocol error");
    byte[] echoed=wire.Take();Check(echoed.Length==8&&echoed[0]==136,"Client sends a close reply before closing");
}
foreach(byte[] invalid in new[]{new byte[]{0x81,0},new byte[]{0xc2,0},new byte[]{0x82,0x80},new byte[]{0x82,127},new byte[]{0x82,126,0,1},new byte[]{0x00,0},new byte[]{0x09,0},new byte[]{0x8b,0},Frame(8,new byte[]{1}),Frame(8,new byte[]{3,237}),Frame(8,new byte[]{3,232,0xc0,0x80}),Frame(2,new byte[3000],false).Concat(Frame(0,new byte[2000])).ToArray()})
{
    var wire=new Wire(8192);wire.Feed(invalid);var socket=new OnlineWebSocketStream(wire,new Mask());var bytes=new byte[8192];
    for(int i=0;i<50&&!socket.Closed;i++)socket.TryRead(bytes,0,bytes.Length);
    Check(socket.Closed&&socket.ProtocolError,"Invalid opcodes, masking, lengths, fragmentation and close reasons fail closed");
}
var idleWire=new Wire(8192);var idleSocket=new OnlineWebSocketStream(idleWire,new Mask());var idleBytes=new byte[32];
for(int i=0;i<100;i++)idleSocket.TryRead(idleBytes,0,32);
long baseline=GC.GetAllocatedBytesForCurrentThread();
for(int i=0;i<10000;i++)idleSocket.TryRead(idleBytes,0,32);
Check(GC.GetAllocatedBytesForCurrentThread()==baseline,"Steady-state nonblocking WebSocket polling allocates nothing");
CodecChecks.Run(Check);
CardAndProjectionChecks.Run(Check);
if(args.Length>0) await VisitChecks.Run(args[0],Check);
Console.WriteLine($"{checks} online checks passed.");

static byte[] Frame(int opcode,byte[] payload,bool final=true)
{
    int header=payload.Length<126?2:4;var bytes=new byte[header+payload.Length];bytes[0]=(byte)(opcode|(final?128:0));
    if(header==2)bytes[1]=(byte)payload.Length;else {bytes[1]=126;bytes[2]=(byte)(payload.Length>>8);bytes[3]=(byte)payload.Length;}
    payload.CopyTo(bytes,header);return bytes;
}
sealed class Mask:IOnlineMaskSource {public int Calls;public uint NextMask()=>unchecked((uint)(0x10203040+Calls++));}
sealed class Wire(int quantum):IOnlinePoseStream
{
    readonly byte[] input=new byte[16384],output=new byte[16384];int position,length,written;
    public bool BlockWrites;
    public void Feed(byte[] bytes){bytes.CopyTo(input,0);position=0;length=bytes.Length;}
    public byte[] Take(){var result=output[..written];written=0;return result;}
    public int TryRead(byte[] buffer,int offset,int count){if(position==length)return -1;int take=Math.Min(Math.Min(count,quantum),length-position);Array.Copy(input,position,buffer,offset,take);position+=take;return take;}
    public int TryWrite(byte[] buffer,int offset,int count){if(BlockWrites)return -1;int take=Math.Min(count,quantum);Array.Copy(buffer,offset,output,written,take);written+=take;return take;}
}
