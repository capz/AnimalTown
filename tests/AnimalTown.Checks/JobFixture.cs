using AnimalTown;
using AnimalTown.Jobs;
using System.Text.Json;

// Disposable emulator inputs. Offers are deterministic snapshots; acceptance,
// handoffs, physics, mail and card transfers still use the production game UI.
internal static class JobFixture
{
    private sealed class Disk:ISaveStorage
    {internal readonly Dictionary<string,byte[]> Files=new();public byte[] Read(string name)=>Files.TryGetValue(name,out var b)?(byte[])b.Clone():throw new IOException();public void Write(string name,byte[] bytes)=>Files[name]=(byte[])bytes.Clone();}
    internal static void Write(string output,string mode)
    {
        bool baseline=mode=="baseline",winter=mode=="snowman";long now=JobCalendar.At(2027,winter?1:7,15,12);var s=new GameState(7319){Name="Ari",Tutorial=baseline?6:7,HouseTier=1};s.Town.DaylightLocation=1;
        s.Rollover((int)(now/86400),2027*12+(winter?1:7));s.Tutorial=baseline?6:7;if(!baseline)s.ResidentJobs.Enable(0x41544a4f425331UL,now);
        bool placed=false;for(int z=20;z<Town.Height-13&&!placed;z++)for(int x=4;x<20&&!placed;x++)placed=s.PlaceHouse(x,z,false);if(!placed)throw new Exception("Job fixture requires a home plot");
        for(int z=28;z<52;z++)for(int x=20;x<54;x++){s.Town.Tiles[z*Town.Width+x]=(byte)Terrain.Grass;s.Town.Elevation[z*Town.Width+x]=0;}
        for(int i=s.Town.GroundCount-1;i>=0;i--){var g=s.Town.Ground[i];if(g.X>=20&&g.X<54&&g.Z>=28&&g.Z<52)s.Town.RemoveGround(i);}
        s.Space=0;s.X=30;s.Z=42.3f;s.Facing=(float)Math.PI;
        Array.Clear(s.Pockets);s.Pockets[0]=Catalog.WritingPaper;s.Pockets[1]=2;s.Pockets[2]=2;s.Pockets[3]=2;s.Pockets[4]=2;s.Pockets[5]=Catalog.Pear;s.Pockets[6]=14;s.Pockets[7]=15;s.Pockets[8]=Catalog.Shovel;
        if(baseline){s.Pockets[0]=Catalog.WorkShirt;s.Pockets[5]=1;}
        for(int n=0;n<6;n++){var v=s.Town.Residents[n];v.Space=0;v.PosX=n==0?30:n==1?35.5f:30+(n-2.5f)*2.2f;v.PosZ=n==0?40.8f:n==1?42.3f:35;v.X=(int)v.PosX;v.Z=(int)v.PosZ;v.Dwell=36000;v.JourneyPhase=0;v.JourneyBuilding=-1;v.JourneyNextX=v.JourneyNextZ=-1;v.Yaw=0;s.Town.Jobs.SetOutfit(n,mode=="outfits-on"?Catalog.BerryShirt:0);}
        if(mode=="mail"){s.Space=25;s.X=5;s.Z=5;}
        s.Shirt=mode=="outfits-on"?Catalog.StripedShirt:0;
        if(mode=="retrieval")s.Town.JobState.Offers.Restore(1); // First referral draw stops the chain.
        if(mode=="flowers"||mode=="snowman"){s.Town.Residents[0].HomeX=21;s.Town.Residents[0].HomeZ=41;}
        var l=s.Town.Jobs;JobTemplate template=mode=="property"?JobTemplate.DeliveryProperty:mode=="retrieval"?JobTemplate.RetrievalBorrowed:mode=="fruit"?JobTemplate.FavorFruit:mode=="fish"?JobTemplate.FavorFish:mode=="insect"?JobTemplate.FavorInsect:mode=="flowers"?JobTemplate.FavorFlowers:mode=="ball"?JobTemplate.FavorBall:mode=="snowman"?JobTemplate.FavorSnowman:mode=="letter"?JobTemplate.FavorLetter:JobTemplate.DeliveryClothing;
        if(!baseline&&!mode.StartsWith("outfits")&&mode!="mail"&&mode!="maximum-mail"){
            l.Offers[0]=new VillagerJobOffer{Id=l.Allocate(),Owner=l.Player(0),Requester=l.Npc(0),Target=template<=JobTemplate.DeliveryProperty||template==JobTemplate.RetrievalBorrowed?l.Npc(1):default,Template=template,Item=template==JobTemplate.FavorFruit?Catalog.Pear:template==JobTemplate.DeliveryClothing?Catalog.BerryShirt:template==JobTemplate.RetrievalBorrowed||template==JobTemplate.DeliveryProperty?Catalog.Notebook:0,Revision=1,CreatedAt=now};
            if(mode=="ball"){l.ObjectDay=(int)(now/86400);l.World[0]=new JobWorldObject{Id=l.Allocate(),Kind=JobWorldKind.Ball,X=30,Z=41.6f,Radius=.32f,Acre=2*Town.ScreenColumns+1,CreatedAt=now};}
            if(mode=="snowman"){l.ObjectDay=(int)(now/86400);for(int i=0;i<2;i++)l.World[i]=new JobWorldObject{Id=l.Allocate(),Builder=l.Player(0),Kind=JobWorldKind.Snowball,X=30,Z=42.5f+i*1.8f,Radius=.7f,CreatedAt=now,Acre=2*Town.ScreenColumns+1};}
        }
        if(mode=="mail"||mode=="maximum-mail"){
            string body=mode=="mail"?"Dear Ari,\nThe town feels brighter with you here. Thank you for helping your neighbours!\nYour friend":string.Concat(Enumerable.Repeat("\U0001f642",1000));
            int count=mode=="maximum-mail"?128:1;for(int i=0;i<count;i++){l.EnsureMail();var m=new JobMailRecord{Id=l.Allocate(),Owner=l.Player(0),Sender=l.Npc(0),Recipient=l.Player(0),State=JobMailState.Inbox,Gift=i==0?Catalog.LeafShirt:0,SentAt=now-86400,DueAt=now};l.StoreBody(ref m,JobTextBody.Pack(body,mode=="mail"?body.Length:1000));l.Mail[i]=m;s.Mail[i]=JobContent.Letter;}s.MailCount=count;
        }
        var disk=new Disk();
        if(baseline){
            s.Sequence=1;var records=SplitSaveCodec.Encode(s);for(int i=0;i<records.Length;i++){CatalogOne(records[i]);disk.Files[SplitSaveCodec.FileName(SplitSaveCodec.RecordKind(i),1)]=records[i];}var commit=SplitSaveCodec.EncodeCommit(s,records);CatalogOne(commit);disk.Files[SplitSaveCodec.FileName(SplitSaveCodec.Commit,1)]=commit;
        }else{var repository=new SaveRepository(disk);repository.Save(s);repository.Save(s);}
        Directory.CreateDirectory(output);foreach(var p in disk.Files)File.WriteAllBytes(Path.Combine(output,p.Key),p.Value);
        File.WriteAllText(Path.Combine(output,"fixture.json"),JsonSerializer.Serialize(new{mode,now,seed=s.Town.Seed,sequence=s.Sequence,files=disk.Files.Keys},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Disposable job fixture: "+Path.GetFullPath(output));
    }
    private static void CatalogOne(byte[] record)
    {record[8]=1;record[9]=record[10]=record[11]=0;uint crc=SaveCodec.Crc(record,0,record.Length-4);for(int i=0;i<4;i++)record[record.Length-4+i]=(byte)(crc>>(8*i));}
    internal static void Report(string directory,string mode,bool verify)
    {
        var disk=new Disk();foreach(string file in Directory.GetFiles(directory))disk.Files[Path.GetFileName(file)]=File.ReadAllBytes(file);
        var s=new SaveRepository(disk).Load()??throw new Exception("Controller run produced no committed town");var l=s.Town.Jobs;
        var jobs=Enumerable.Range(0,OfflineJobLedger.JobCapacity).Select(l.Job).Where(j=>j.Occupying).ToArray();
        bool completed=Enumerable.Range(0,OfflineJobLedger.TerminalCapacity).Any(i=>l.Terminal(i).Reason==JobEndReason.Success)||jobs.Any(j=>j.Status==JobStatus.CompletedRetained);
        if(verify&&mode!="mail"&&!mode.StartsWith("maximum")&&!mode.StartsWith("outfits")&&mode!="baseline"&&!completed)throw new Exception("Controller run has not completed its errand: "+mode);
        if(verify&&mode=="delivery"&&l.Outfit(1)!=Catalog.BerryShirt)throw new Exception("Delivery did not change the actual recipient's clothing");
        if(verify&&mode=="mail"&&s.Find(Catalog.LeafShirt)<0)throw new Exception("The real mail attachment was not collected");
        var result=new{mode,passed=verify,sequence=s.Sequence,activeProfile=s.ActiveProfile,visiting=s.Visiting,position=new{s.X,s.Z,s.Space},wallet=s.Wallet,completed,mailCount=s.MailCount,pockets=s.Pockets,bindings=Enumerable.Range(0,s.Capacity).Where(s.IsJobItem),outfits=Enumerable.Range(0,6).Select(l.Outfit),jobs=jobs.Select(j=>new{template=j.Template.ToString(),status=j.Status.ToString(),target=l.FindNpc(j.Target),j.Item,j.FlowerBaseline,j.HomeAcre}),terminals=Enumerable.Range(0,16).Select(l.Terminal).Where(t=>!t.Job.IsEmpty).Select(t=>new{template=t.Template.ToString(),reason=t.Reason.ToString(),t.At})};
        string json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);File.WriteAllText(Path.Combine(directory,"..","jobs-state.json"),json+"\n");
    }
}
