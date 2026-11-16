using System;
using System.IO;
using Dolphin.Storage;
using Dolphin.Timing;

// Pinned SDK-owned record store; the bridge allows using its new block API
// without requiring an unpublished replacement for the game's renderer SDK.
internal sealed class NativeCardBlocks : ICardBlockStorage
{
    private const string FileName="AnimalTown";
    private readonly int device,identity;
    private static readonly byte[] Empty=new byte[0];
    public int Blocks {get;}
    internal NativeCardBlocks(int device,bool create,byte[] banner,byte[] icon)
    {
        this.device=device;int count=Check(CardBlockBridge.Io(device,FileName,0,0,0,Empty));
        if(count==0){
            if(!create)throw new FileNotFoundException("AnimalTown container not found");
            if(banner.Length!=6144||icon.Length!=2048)throw new IOException("Save artwork is unavailable");
            var header=new byte[16384];Text(header,32,"AnimalTown");Text(header,64,"Town, villagers and options");
            Array.Copy(banner,0,header,96,banner.Length);Array.Copy(icon,0,header,6240,icon.Length);
            int id=(int)(Clock.Timestamp&0x7fffffff);if(id==0)id=1;
            Check(CardBlockBridge.Io(device,FileName,3,16,id,header));count=16;
        }
        Blocks=count;identity=Check(CardBlockBridge.Io(device,FileName,4,0,0,Empty));
    }
    private static void Text(byte[] b,int at,string text){for(int i=0;i<text.Length;i++)b[at+i]=(byte)text[i];}
    private static int Check(int code){if(code<0)throw new IOException("Card container I/O failed / error "+(-code));return code;}
    public void Read(int block,byte[] bytes){LoadingScreen.Pulse();Check(CardBlockBridge.Io(device,FileName,1,block,identity,bytes));}
    public void Write(int block,byte[] bytes){LoadingScreen.Pulse();Check(CardBlockBridge.Io(device,FileName,2,block,identity,bytes));}
}
