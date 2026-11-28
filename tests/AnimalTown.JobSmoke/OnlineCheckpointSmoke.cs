using System;
using AnimalTown;
using AnimalTown.Jobs;
using AnimalTown.Online;

internal static partial class Program
{
    private static void PrepareOnlineCheckpoint()
    {
        BuildFullCard(0,true);
        var source=new Card(0);
        var target=new TownCardRecords(new CardBlocks(1,true),true);
        foreach(string name in source.Names())target.CopyFrom(source,name);
        var repository=new SaveRepository(new Card(1));var returned=repository.Load()!;
        returned.Wallet+=321;repository.Save(returned);
    }
    private static string OnlineCheckpointFingerprint()
    {
        var repository=new SaveRepository(new Card(1));var state=repository.Load()!;
        using var export=repository.OpenCheckpointExport(state);
        using var hash=new OnlineCheckpointHash();var page=new byte[16384];int count;
        while((count=export.Read(page,0,page.Length))!=0)hash.Append(page,0,count);
        return hash.Complete();
    }
    private static OnlineCheckpointDownload DownloadOnlineCheckpoint(GameState home,string fingerprint)
    {
        var repository=new SaveRepository(new Card(1));var state=repository.Load(home)!;
        using var export=repository.OpenCheckpointExport(state);
        var download=new OnlineCheckpointDownload(export.Length,fingerprint);var page=new byte[16384];int count;
        while((count=export.Read(page,0,page.Length))!=0)download.Append(page,0,count);
        download.Verify();return download;
    }
    private static void OnlineCheckpointBudget()
    {
        Console.WriteLine("Native maximum online card return");
        PrepareOnlineCheckpoint();string fingerprint=OnlineCheckpointFingerprint();
        var repository=new SaveRepository(new Card(0));var home=repository.Load()!;int wallet=home.Wallet,sequence=home.Sequence;
        GameState returned;
        using(var download=DownloadOnlineCheckpoint(home,fingerprint))returned=download.Publish(repository,home);
        var readback=repository.Load(returned)!;
        Require(readback.Sequence==sequence+1&&readback.Wallet==wallet+321&&readback.MailCount==128&&
            readback.ResidentJobs.ReadLetter(127)==home.ResidentJobs.ReadLetter(127)&&
            readback.Town.Jobs.Reward(127).State!=JobRewardState.Empty&&readback.Town.Jobs.Job(79).Occupying&&
            readback.Town.Jobs.Offers[47].Id.IsValid&&readback.Town.Traffic.Winter,
            "streamed online publication and card readback retain maximum Unicode mail, jobs, rewards and winter state");
        Console.WriteLine("Native maximum online return: verified streaming, publication and durable readback passed");
    }
}
