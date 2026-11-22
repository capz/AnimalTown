using System;
using System.IO;
using Dolphin.Storage;
using Dolphin.Timing;

// Game-specific adapter for the pinned SDK record store/block API. Preserve
// incremental loading pulses and the town's browser artwork.
internal sealed class NativeCardBlocks : ICardBlockStorage
{
    internal const int ReservedBlocks=32;
    private readonly string FileName;
    private readonly int device,identity;
    private static readonly byte[] Empty=new byte[0];
    public int Blocks {get;}
    internal NativeCardBlocks(int device,bool create,byte[] banner,byte[] icon,string fileName="AnimalTown",int reservedBlocks=ReservedBlocks)
    {
        this.device=device;FileName=fileName;int count=Check(CardBlockBridge.Io(device,FileName,0,0,0,Empty),"open");
        if(count==0){
            if(!create)throw new FileNotFoundException("AnimalTown container not found");
            if(banner.Length!=6144||icon.Length!=2048)throw new IOException("Save artwork is unavailable");
            var header=new byte[16384];Text(header,32,"AnimalTown");Text(header,64,"Town, villagers and options");
            Array.Copy(banner,0,header,96,banner.Length);Array.Copy(icon,0,header,6240,icon.Length);
            int id=(int)(Clock.Timestamp&0x7fffffff);if(id==0)id=1;
            Check(CardBlockBridge.Io(device,FileName,3,reservedBlocks,id,header),"create");count=reservedBlocks;
        }
        Blocks=count;identity=Check(CardBlockBridge.Io(device,FileName,4,0,0,Empty),"identify");
    }
    private static void Text(byte[] b,int at,string text){for(int i=0;i<text.Length;i++)b[at+i]=(byte)text[i];}
    private int Check(int code,string operation,int block=-1)
    {
        if(code<0)throw new IOException((device==4?"Slot A":"Slot B")+" "+FileName+" "+operation+
            (block<0?"":" block "+block)+" / error "+(-code));
        return code;
    }
    public void Read(int block,byte[] bytes){LoadingScreen.Pulse();Check(CardBlockBridge.Io(device,FileName,1,block,identity,bytes),"read",block);}
    public void Write(int block,byte[] bytes){LoadingScreen.Pulse();Check(CardBlockBridge.Io(device,FileName,2,block,identity,bytes),"write",block);}
}
