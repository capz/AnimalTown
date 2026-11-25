using AnimalTown;

internal static class OnlineCredentialChecks
{
    internal static void Run(Action<bool,string> check)
    {
        const string journalId="6ea3bf96-b788-4611-b4fa-d6828cc1ca76";
        var first=new OnlineCardCredential("https://town.example","0000000000000010",
            "f8bad4f5-7079-433a-b8ad-83ef12afe015","89baed36-3474-4dd5-a915-8fe8b989a1b0",
            "r2_"+new string('A',64),1);
        var next=new OnlineCardCredential(first.Origin,first.Town,first.AccountId,first.DeviceId,
            "r2_"+new string('B',64),2);
        var files=new CardFiles();
        var vault=new OnlineCredentialStore(files);
        check(vault.Load()==null,"Online link should start empty");
        var ready=vault.Save(null,first);
        check(files.Records[OnlineCredentialStore.Bank0].Length==OnlineCredentialStore.RecordBytes,"Credential bank should stay fixed size");
        var pending=vault.Save(ready,first.WithRotation(journalId));
        check(vault.Load()?.Credential.RotationId==journalId,"Pending rotation proof should survive restarting");
        check(vault.Load()?.Credential.RefreshToken==first.RefreshToken,"Pending rotation must retain proof until server response is committed");
        var torn=files.Records[OnlineCredentialStore.Bank1];
        files.Records[OnlineCredentialStore.Bank1]=(byte[])torn.Clone();
        files.Records[OnlineCredentialStore.Bank1][0]^=1;
        check(vault.Load()?.Credential.RotationId=="","Corrupt candidate bank must preserve earlier credentials");
        files.Records[OnlineCredentialStore.Bank1]=torn;
        bool refused=false;
        try{vault.Save(ready,next);}catch(IOException){refused=true;}
        check(refused,"A changed card record must block stale publication");
        files.DamageWrite=true;
        refused=false;
        try{vault.Save(pending,next);}catch(IOException){refused=true;}
        check(refused&&vault.Load()?.Credential.RotationId==journalId,"Damaged output must preserve the pending rotation for recovery");
        files.DamageWrite=false;
        vault.Save(pending,next);
        var recovered=vault.Load()?.Credential;
        check(recovered?.Generation==2&&recovered.RotationId==""&&recovered.RefreshToken==next.RefreshToken,
            "Confirmed rotation should publish the replacement and clear the pending marker");
        check(!OnlineCardCredential.ValidOrigin("http://town.example")&&!OnlineCardCredential.ValidOrigin("https://town.example/path"),
            "Card scope should accept only canonical HTTPS origins without paths");
        TownDataDeletion.Erase(files);
        check(vault.Load()==null&&!TownDataDeletion.Pending(files.Names()),"Confirmed town deletion should remove both credential banks");
    }

    private sealed class CardFiles : ISaveStorage,ITownDataFiles
    {
        internal readonly Dictionary<string,byte[]> Records=new();
        internal bool DamageWrite;
        public string[] Names()=>Records.Keys.ToArray();
        public void Delete(string name)=>Records.Remove(name);
        public byte[] Read(string name)=>Records.TryGetValue(name,out byte[]? bytes)?(byte[])bytes.Clone():throw new FileNotFoundException();
        public void Write(string name,byte[] bytes)
        {
            var copy=(byte[])bytes.Clone();
            if(DamageWrite)copy[^1]^=1;
            Records[name]=copy;
        }
    }
}
