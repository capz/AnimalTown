namespace AnimalTown;

public interface ITownDataFiles
{
    string[] Names();
    byte[] Read(string name);
    void Write(string name,byte[] bytes);
    void Delete(string name);
}

// A durable intent prevents fallback banks/legacy saves from resurrecting a
// partly deleted town after removal or power loss. Only confirmation calls Erase.
public static class TownDataDeletion
{
    public const string Marker="AT-DeleteTown";
    public static bool IsCredentialFile(string name)=>name==OnlineCredentialStore.Bank0||name==OnlineCredentialStore.Bank1;
    private static bool IsTownData(string name)=>SplitSaveCodec.IsFile(name)||IsCredentialFile(name);
    public static bool Pending(string[] names)
    {foreach(string name in names)if(name==Marker)return true;return false;}
    public static void Erase(ITownDataFiles files)
    {
        string[] names=files.Names();bool any=Pending(names);
        foreach(string name in names)if(IsTownData(name))any=true;
        if(!any)return;
        byte[] intent={65,84,68,69,76,69,84,69,1};
        files.Write(Marker,intent);var readback=files.Read(Marker);
        if(readback.Length!=intent.Length)throw new IOException("Town deletion intent was not saved");
        for(int i=0;i<intent.Length;i++)if(readback[i]!=intent[i])throw new IOException("Town deletion intent was not saved");
        foreach(string name in names)if(IsTownData(name))files.Delete(name);
        foreach(string name in files.Names())if(IsTownData(name))throw new IOException("Town deletion is incomplete");
        files.Delete(Marker);
        if(Pending(files.Names()))throw new IOException("Town deletion is incomplete");
    }
}
