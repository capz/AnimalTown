using System;
using System.IO;
using AnimalTown;
using Dolphin.Storage;

// A browser entry holds both logical banks for one playable villager. The
// SDK's verified replacement journal protects the other bank during a write.
internal sealed class VillagerCardFiles
{
    private readonly MemoryCard card;
    internal const int Blocks=2,JournalBlocks=3;
    private readonly MemoryCardSaveOptions artwork;
    private readonly bool[] present=new bool[GameState.ProfileCount];
    private const int MaximumBankBytes=8192;
    private const int Magic=0x31565441; // ATV1

    internal VillagerCardFiles(MemoryCard card,byte[] banner,byte[] icon,MemoryCardEntry[] entries)
    {
        this.card=card;
        artwork=new MemoryCardSaveOptions{Title="AnimalTown",Banner=banner,Icon=icon};
        for(int slot=0;slot<present.Length;slot++)
            foreach(var entry in entries)if(entry.Name==FileName(slot)){present[slot]=true;break;}
    }

    internal static string FileName(int slot)=>"AnimalTown-Villager"+(slot+1);
    internal static bool IsFile(string name)
    {
        for(int slot=0;slot<GameState.ProfileCount;slot++)if(name==FileName(slot))return true;
        return false;
    }

    internal static string BankName(int slot,int bank)=>SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(3+slot),bank);
    internal static int SlotForBank(string name)
    {
        for(int slot=0;slot<GameState.ProfileCount;slot++)
            if(name==BankName(slot,0)||name==BankName(slot,1))return slot;
        return -1;
    }

    internal bool Exists(int slot)=>present[slot];

    internal byte[][] Read(int slot)
    {
        var bytes=card.ReadAllBytes(FileName(slot));
        if(bytes==null||bytes.Length<12||Get(bytes,0)!=Magic)throw new IOException("Invalid villager card file");
        int first=Get(bytes,4),second=Get(bytes,8);
        if(first<0||second<0||first>MaximumBankBytes||second>MaximumBankBytes||first+second==0||bytes.Length!=12+first+second)
            throw new IOException("Invalid villager card banks");
        var banks=new[]{new byte[first],new byte[second]};
        Array.Copy(bytes,12,banks[0],0,first);
        Array.Copy(bytes,12+first,banks[1],0,second);
        return banks;
    }

    internal void Write(int slot,byte[][] banks)
    {
        var bytes=new byte[12+banks[0].Length+banks[1].Length];
        Put(bytes,0,Magic);
        Put(bytes,4,banks[0].Length);
        Put(bytes,8,banks[1].Length);
        Array.Copy(banks[0],0,bytes,12,banks[0].Length);
        Array.Copy(banks[1],0,bytes,12+banks[0].Length,banks[1].Length);
        if(artwork.Banner==null||artwork.Banner.Length==0)artwork.Banner=File.ReadAllBytes(ContentPaths.Asset("savebanner.rgb5a3"));
        if(artwork.Icon==null||artwork.Icon.Length==0)artwork.Icon=File.ReadAllBytes(ContentPaths.Asset("saveicon.rgb5a3"));
        artwork.Comment="Villager "+(slot+1);
        card.WriteSave(FileName(slot),bytes,artwork);
        present[slot]=true;
        var actual=Read(slot);
        for(int bank=0;bank<2;bank++)CardArchive.Match(banks[bank],actual[bank]);
    }

    internal void Delete(int slot)
    {
        if(!Exists(slot))return;
        card.Delete(FileName(slot));
        present[slot]=false;
    }

    private static int Get(byte[] bytes,int at)=>bytes[at]|bytes[at+1]<<8|bytes[at+2]<<16|bytes[at+3]<<24;
    private static void Put(byte[] bytes,int at,int value)
    {for(int i=0;i<4;i++)bytes[at+i]=(byte)((uint)value>>(8*i));}
}
