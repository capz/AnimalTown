#if ANIMALTOWN_ONLINE_PAIRING
using System;
using System.IO;
using AnimalTown;
using Dolphin.Storage;

// A card setting makes the same candidate configurable on physical disc and
// SD. It never carries credentials; changing it still requires explicit linking
// to the new exact origin. A damaged setting fails closed.
internal static class OnlineServiceOrigin
{
    const string Name="AnimalTown-Service";
    internal static string? Load()
    {
        using var card=SaveCards.Mount();bool found=false;
        foreach(var entry in card.GetEntries())if(entry.Name==Name){if(entry.Length!=256)throw new IOException("Invalid service setting.");found=true;break;}
        if(!found)return null;
        var bytes=card.ReadAllBytes(Name);uint expected=0;for(int i=0;i<4;i++)expected|=(uint)bytes[252+i]<<(8*i);
        if(bytes[0]!=1||SaveCodec.Crc(bytes,0,252)!=expected||bytes[1]<9||bytes[1]>71)throw new IOException("Invalid service setting.");
        var text=new char[bytes[1]];for(int i=0;i<text.Length;i++)text[i]=(char)bytes[i+2];var origin=new string(text);
        if(!OnlineCardCredential.ValidOrigin(origin))throw new IOException("Invalid service setting.");return origin;
    }
    internal static void Save(string origin)
    {
        if(!OnlineCardCredential.ValidOrigin(origin))throw new FormatException("Expected an HTTPS service host.");
        var bytes=new byte[256];bytes[0]=1;bytes[1]=(byte)origin.Length;for(int i=0;i<origin.Length;i++)bytes[i+2]=(byte)origin[i];
        uint checksum=SaveCodec.Crc(bytes,0,252);for(int i=0;i<4;i++)bytes[252+i]=(byte)(checksum>>(i*8));
        using var card=SaveCards.Mount();card.WriteSave(Name,bytes,new MemoryCardSaveOptions{Title="AnimalTown",Comment="Online service address"});
        var actual=card.ReadAllBytes(Name);if(actual.Length!=bytes.Length)throw new IOException("Could not verify the service setting.");
        for(int i=0;i<bytes.Length;i++)if(bytes[i]!=actual[i])throw new IOException("Could not verify the service setting.");
    }
}
#endif
