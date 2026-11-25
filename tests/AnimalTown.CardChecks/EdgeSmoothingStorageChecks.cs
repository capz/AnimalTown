using AnimalTown;
using Dolphin.Storage;

internal static class EdgeSmoothingStorageChecks
{
    internal static void Run(Action<bool,string> check)
    {
        MemoryCard.Slots.Clear();var card=new MemoryCard();SaveCards.Initialize();
        int writes=card.Writes;
        check(UserOptionsStorage.Load().EdgeSmoothing&&card.Writes==writes,"missing settings enable edge smoothing without writing a card");
        foreach(int layout in new[]{0,1,2})foreach(int version in new[]{4,5,6})foreach(bool enabled in new[]{false,true}){
            card=Fixture(layout,LegacyOptions(2),LegacyDebug(version,enabled));writes=card.Writes;
            var loaded=UserOptionsStorage.Load();
            check(loaded.EdgeSmoothing==enabled&&loaded.Resolution==2&&loaded.Widescreen&&loaded.ShowDebugMenus,"old AA imports from dedicated, loose and archived settings without changing other video preferences");
            check(card.Writes==writes,"legacy preference loading stays read-only");
            var debugBefore=ReadDebug(card);
            check(UserOptionsStorage.Save(loaded)&&UserOptions.Decode(card.ReadAllBytes(CardSettingsStorage.VideoFile)).EdgeSmoothing==enabled,"explicit video save publishes migrated AA in the normal config block");
            check(ReadDebug(card).SequenceEqual(debugBefore),"normal video save leaves debug data unchanged");
            var debug=DevConfigStorage.Load();debug.Values[DevConfig.GroundMipBandsIndex]=1;
            check(DevConfigStorage.Save(debug)&&ReadDebug(card)[3]==7&&ReadDebug(card)[34]==0,"the next debug save retires the AA byte after video migration");
            check(UserOptionsStorage.Load().EdgeSmoothing==enabled&&DevConfigStorage.Load().Values[DevConfig.GroundMipBandsIndex]==1,"restarting retains AA in video settings and independent diagnostics");
        }
        foreach(bool enabled in new[]{false,true}){
            card=Fixture(0,null,LegacyDebug(6,enabled));writes=card.Writes;
            check(UserOptionsStorage.Load().EdgeSmoothing==enabled&&card.Writes==writes,"an old debug-only card keeps its explicit AA choice");
            check(DevConfigStorage.Save(DevConfigStorage.Load()),"saving diagnostics first migrates an old debug-only AA choice");
            var video=UserOptions.Decode(card.ReadAllBytes(CardSettingsStorage.VideoFile));
            check(video.EdgeSmoothing==enabled&&video.HasStoredEdgeSmoothing&&ReadDebug(card)[34]==0,"AA is durable in video settings before its debug source is retired");

            card=Fixture(0,new UserOptions{EdgeSmoothing=!enabled,Widescreen=true}.Encode(),LegacyDebug(6,enabled));
            byte[] authoritative=card.ReadAllBytes(CardSettingsStorage.VideoFile);
            check(UserOptionsStorage.Load().EdgeSmoothing==!enabled,"current video settings take precedence over conflicting legacy debug bytes");
            check(DevConfigStorage.Save(DevConfigStorage.Load())&&card.ReadAllBytes(CardSettingsStorage.VideoFile).SequenceEqual(authoritative),"retiring old debug data never overwrites an authoritative video choice");
        }
        foreach(int layout in new[]{0,1,2}){
            card=Fixture(layout,LegacyOptions(2),LegacyDebug(6,false));
            check(DevConfigStorage.Save(DevConfigStorage.Load()),"diagnostics-first migration supports every historical storage layout");
            var video=UserOptionsStorage.Load();
            check(!video.EdgeSmoothing&&video.Resolution==2&&video.Widescreen&&video.ShowDebugMenus&&ReadDebug(card)[34]==0,"diagnostics-first migration retains AA and all unrelated preferences");
        }
        card=Fixture(0,LegacyOptions(2),LegacyDebug(6,true));var a=card;byte[] slotA=a.ReadAllBytes(CardSettingsStorage.DebugFile);writes=a.Writes;
        var b=new MemoryCard(5);b.WriteSave(CardSettingsStorage.DebugFile,LegacyDebug(6,false),new MemoryCardSaveOptions());
        SaveCards.Select(SaveCards.Inspect(5));
        check(!UserOptionsStorage.Load().EdgeSmoothing&&DevConfigStorage.Save(DevConfigStorage.Load())&&!UserOptionsStorage.Load().EdgeSmoothing,"legacy AA migration follows the selected Slot B card");
        check(a.Writes==writes&&a.ReadAllBytes(CardSettingsStorage.DebugFile).SequenceEqual(slotA),"Slot B migration leaves Slot A's settings byte-identical");
        foreach(int version in new[]{1,2}){
            card=Fixture(0,LegacyOptions(version),null);
            check(UserOptionsStorage.Load().EdgeSmoothing,"old video-only cards inherit the enabled default");
        }
        card=Fixture(0,LegacyOptions(2),LegacyDebug(3,false));
        check(UserOptionsStorage.Load().EdgeSmoothing,"debug versions predating AA cannot disable the new default");
        card=Fixture(0,LegacyOptions(2),new byte[]{1,2,3});
        var intact=UserOptionsStorage.Load();
        check(intact.EdgeSmoothing&&intact.Resolution==2&&intact.Widescreen,"damaged optional debug data cannot discard valid video preferences");

        // Inject removal on either write: the first published video copy wins,
        // while a failed first write retains the sole trustworthy legacy copy.
        foreach(int fail in new[]{0,1}){
            card=Fixture(0,LegacyOptions(2),LegacyDebug(6,false));
            byte[] oldDebug=ReadDebug(card);card.FailSaveWrite=fail;
            check(!DevConfigStorage.Save(DevConfigStorage.Load()),"interrupted AA migration reports the failed settings save");
            check(ReadDebug(card).SequenceEqual(oldDebug)&&!UserOptionsStorage.Load().EdgeSmoothing,"interruption preserves the disabled choice and its legacy fallback");
            card.FailSaveWrite=-1;
            check(DevConfigStorage.Save(DevConfigStorage.Load())&&!UserOptionsStorage.Load().EdgeSmoothing&&ReadDebug(card)[3]==7,"retry completes AA migration without reverting the player's choice");
        }
        card=Fixture(0,null,LegacyDebug(6,false));byte[] before=ReadDebug(card);card.Capacity=card.Used;
        check(!DevConfigStorage.Save(DevConfigStorage.Load())&&ReadDebug(card).SequenceEqual(before)&&!UserOptionsStorage.Load().EdgeSmoothing,"a full card cannot retire AA before its replacement is durable");
        card=Fixture(0,LegacyOptions(2),LegacyDebug(6,false));SaveCards.WithoutSaving();writes=card.Writes;
        check(UserOptionsStorage.Load().EdgeSmoothing&&!UserOptionsStorage.Save(new UserOptions{EdgeSmoothing=false})&&!DevConfigStorage.Save(new DevConfig())&&card.Writes==writes,"temporary play uses defaults and never migrates card preferences");
        Console.WriteLine("Edge smoothing: defaults, all legacy layouts, version precedence and interrupted migration passed.");
    }
    private static MemoryCard Fixture(int layout,byte[]? video,byte[]? debug)
    {
        MemoryCard.Slots.Clear();var card=new MemoryCard();
        if(layout==2){
            var archive=new CardArchive(card,new byte[6144],new byte[2048],4);
            if(video!=null)archive.Write(UserOptionsStorage.FileName,video);
            if(debug!=null)archive.Write(DevConfigStorage.FileName,debug);
        }else{
            if(video!=null)card.WriteSave(layout==0?CardSettingsStorage.VideoFile:UserOptionsStorage.FileName,video,new MemoryCardSaveOptions());
            if(debug!=null)card.WriteSave(layout==0?CardSettingsStorage.DebugFile:DevConfigStorage.FileName,debug,new MemoryCardSaveOptions());
        }
        SaveCards.Initialize();return card;
    }
    private static byte[] ReadDebug(MemoryCard card)=>CardSettingsStorage.Read(card,card.Device,CardSettingsStorage.DebugFile,DevConfigStorage.FileName)!;
    private static byte[] LegacyOptions(int version)
    {
        byte[] bytes=new UserOptions{Resolution=2,Widescreen=true,ShowDebugMenus=true}.Encode();bytes[3]=(byte)version;bytes[7]=0;if(version==1)bytes[6]=0;Rehash(bytes,12);return bytes;
    }
    private static byte[] LegacyDebug(int version,bool enabled)
    {
        byte[] bytes=new DevConfig().Encode();if(version<6)Array.Resize(ref bytes,40);bytes[3]=(byte)version;bytes[34]=(byte)(version>=4&&enabled?1:0);Rehash(bytes,version<6?36:40);return bytes;
    }
    private static void Rehash(byte[] bytes,int length)
    {uint hash=2166136261;for(int i=0;i<length;i++)hash=unchecked((hash^bytes[i])*16777619);for(int i=0;i<4;i++)bytes[length+i]=(byte)(hash>>(i*8));}
}
