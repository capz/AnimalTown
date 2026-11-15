using AnimalTown;

internal static class UserOptionsChecks
{
    internal static void Run(Action<bool,string> check)
    {
        var defaults=new UserOptions();check(defaults.Resolution==0&&!defaults.Widescreen&&!defaults.ShowDebugMenus,"new options preserve boot timing, 4:3 and hidden debug menus");
        for(int resolution=0;resolution<=2;resolution++)foreach(bool wide in new[]{false,true})foreach(bool debug in new[]{false,true}){
            var expected=new UserOptions{Resolution=resolution,Widescreen=wide,ShowDebugMenus=debug};byte[] bytes=expected.Encode();
            var actual=UserOptions.Decode(bytes);check(actual.Resolution==resolution&&actual.Widescreen==wide&&actual.ShowDebugMenus==debug,"video and debug visibility preferences round trip independently");
            for(int i=0;i<bytes.Length;i++){
                var damaged=(byte[])bytes.Clone();damaged[i]^=1;bool rejected=false;
                try{UserOptions.Decode(damaged);}catch(IOException){rejected=true;}
                check(rejected,"corruption cannot silently select another video mode");
            }
        }
        foreach(int length in new[]{0,4,15,17,8192}){
            bool rejected=false;try{UserOptions.Decode(new byte[length]);}catch(IOException){rejected=true;}check(rejected,"bad/truncated option records are rejected");
        }
        bool invalid=false;try{new UserOptions{Resolution=3}.Encode();}catch(InvalidOperationException){invalid=true;}
        check(invalid,"unsupported output cannot be persisted");
        byte[] legacy=Convert.FromHexString("41544f01020100000000000009c9faa2");
        var migrated=UserOptions.Decode(legacy);
        check(migrated.Resolution==2&&migrated.Widescreen&&!migrated.ShowDebugMenus,"v1 console preferences retain video settings and default to hidden debug menus");
        check(migrated.Encode()[3]==2,"the next explicit settings save upgrades a legacy preference record");
        EraseChecks(check);
    }
    private sealed class Files : ITownDataFiles,ISaveStorage
    {
        internal readonly Dictionary<string,byte[]> Data=new();
        internal int Mutations,FailAt;
        internal bool FailAfter,DamageMarker;
        public string[] Names()=>Data.Keys.ToArray();
        public byte[] Read(string name)=>Data.TryGetValue(name,out var bytes)?(byte[])bytes.Clone():throw new IOException("Missing file");
        private void Before(){Mutations++;if(Mutations==FailAt&&!FailAfter)throw new IOException("Card removed");}
        private void After(){if(Mutations==FailAt&&FailAfter)throw new IOException("Card removed after mutation");}
        public void Write(string name,byte[] bytes){Before();Data[name]=(byte[])bytes.Clone();if(DamageMarker&&name==TownDataDeletion.Marker)Data[name][0]^=1;After();}
        public void Delete(string name){Before();if(!Data.Remove(name))throw new IOException("Missing file");After();}
    }
    private static Files TownFiles()
    {
        var files=new Files();var town=new GameState(9281){Name="Erase me"};
        town.SelectProfile(1);town.Name="Erase me too";town.Town.Residents[0].Friendship=99;
        new SaveRepository(files).Save(town);
        files.Data["AnimalTown0"]=SaveCodec.Encode(town);files.Data["AnimalTown1"]=SaveCodec.Encode(town);
        for(int bank=0;bank<2;bank++)for(int kind=0;kind<9;kind++)files.Data.TryAdd(SplitSaveCodec.FileName(kind,bank),new byte[]{1,2,3});
        files.Data["options"]=new UserOptions{Resolution=2,Widescreen=true,ShowDebugMenus=true}.Encode();
        files.Data["dev_config"]=new DevConfig().Encode();files.Data["OtherGame"]=new byte[]{7,8,9};files.Mutations=0;return files;
    }
    private static void EraseChecks(Action<bool,string> check)
    {
        var files=TownFiles();var options=(byte[])files.Data["options"].Clone();var debug=(byte[])files.Data["dev_config"].Clone();
        TownDataDeletion.Erase(files);int writes=files.Mutations;
        check(!files.Names().Any(SplitSaveCodec.IsFile)&&!TownDataDeletion.Pending(files.Names()),"whole-town deletion removes both banks, all four players, NPC and legacy records");
        check(new SaveRepository(files).Load()==null,"no legacy/fallback source can restore deleted villagers");
        check(files.Data["options"].SequenceEqual(options)&&files.Data["dev_config"].SequenceEqual(debug)&&files.Data["OtherGame"].SequenceEqual(new byte[]{7,8,9}),"deletion preserves options, debug settings and unrelated data byte for byte");
        files.Mutations=0;TownDataDeletion.Erase(files);check(files.Mutations==0,"deleting an already empty town does not write a marker");
        foreach(bool after in new[]{false,true})for(int fail=1;fail<=writes;fail++){
            var interrupted=TownFiles();interrupted.FailAt=fail;interrupted.FailAfter=after;bool failed=false;
            try{TownDataDeletion.Erase(interrupted);}catch(IOException){failed=true;}
            check(failed,"injected deletion I/O failure is reported");
            bool remaining=interrupted.Names().Any(SplitSaveCodec.IsFile);
            check(!remaining||TownDataDeletion.Pending(interrupted.Names())||fail==1&&!after,"a partly deleted town is hidden by durable intent until completion");
            check(interrupted.Data["options"].SequenceEqual(options)&&interrupted.Data["dev_config"].SequenceEqual(debug),"interruption never targets console preferences");
            interrupted.FailAt=0;TownDataDeletion.Erase(interrupted);
            check(!TownDataDeletion.Pending(interrupted.Names())&&new SaveRepository(interrupted).Load()==null,"retry completes every interrupted deletion without resurrecting a villager");
        }
        var bad=TownFiles();int original=bad.Data.Count;bad.DamageMarker=true;bool refused=false;
        try{TownDataDeletion.Erase(bad);}catch(IOException){refused=true;}
        check(refused&&bad.Data.Count==original+1,"bad marker readback stops before any town file is erased");
        bad.DamageMarker=false;TownDataDeletion.Erase(bad);check(new SaveRepository(bad).Load()==null,"a confirmed retry can recover a damaged deletion marker");
    }
}
