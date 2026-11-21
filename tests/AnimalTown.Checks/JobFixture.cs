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
        if(mode is "maximum-economy" or "maximum-economy-destination"){
            bool otherTown=mode.EndsWith("-destination");Write(output,otherTown?"destination":"base-travel");
            var fullDisk=ReadFixture(output);var fullRepository=new SaveRepository(fullDisk);var fullState=fullRepository.Load()!;var fullLedger=fullState.Town.Jobs;long fullNow=fullLedger.Watermark;
            for(int p=1;p<4;p++){fullState.SelectProfile(p);fullState.Tutorial=7;}fullState.SelectProfile(0);fullState.EnsureJobIdentities(fullLedger.TownId);fullLedger.EnsureMail();
            string fullMailBody=string.Concat(Enumerable.Repeat("\U0001f642",1000));
            for(int p=0;p<4;p++)for(int n=0;n<128;n++){
                var message=new JobMailRecord{Id=fullLedger.Allocate(),Owner=fullLedger.Player(p),Sender=fullLedger.Npc(0),Recipient=fullLedger.Player(p),State=JobMailState.Inbox,SentAt=fullNow-86400,DueAt=fullNow};
                fullLedger.StoreBody(ref message,JobTextBody.Pack(fullMailBody,1000));fullLedger.Mail.Set(p*128+n,message);fullState.Profiles[p].Mail[n]=JobContent.Letter;fullState.Profiles[p].MailCount=128;
            }
            fullState.MailCount=128;Array.Fill(fullState.Mail,JobContent.Letter);JobMaximumFixture.Fill(fullState,fullMailBody);
            if(otherTown){int free=26;for(int i=OfflineJobLedger.InboxCount;i<fullLedger.Mail.Length&&free>0;i++){var message=fullLedger.Mail[i];int reward=fullLedger.FindReward(message.Reservation);if(reward<0)continue;fullLedger.Rewards[reward]=default;message.Reservation=default;message.Recipient=fullLedger.Player(1);fullLedger.Mail.Set(i,message);free--;}if(free!=0)throw new Exception("Maximum destination needs visitor reward space");}
            for(int z=9;z<24;z++)for(int x=28;x<43;x++){fullState.Town.Tiles[z*Town.Width+x]=(byte)Terrain.Grass;fullState.Town.Elevation[z*Town.Width+x]=0;}
            for(int i=fullState.Town.GroundCount-1;i>=0;i--){var ground=fullState.Town.Ground[i];if(ground.X>=28&&ground.X<43&&ground.Z>=9&&ground.Z<24)fullState.Town.RemoveGround(i);}
            fullState.Space=0;fullState.X=36.5f;fullState.Z=14.5f;fullState.Shirt=Catalog.StripedShirt;
            for(int n=0;n<6;n++)fullLedger.SetOutfit(n,Catalog.BerryShirt);
            var neighbour=fullState.Town.Residents[0];neighbour.Space=0;neighbour.PosX=36.5f;neighbour.PosZ=16;neighbour.X=36;neighbour.Z=16;neighbour.JourneyPhase=0;neighbour.JourneyBuilding=-1;neighbour.JourneyNextX=neighbour.JourneyNextZ=-1;neighbour.Dwell=36000;
            fullRepository.Save(fullState);foreach(var file in fullDisk.Files)File.WriteAllBytes(Path.Combine(output,file.Key),file.Value);return;
        }
        if(mode is "retrieval-multi" or "letter-full" or "owed"){
            Write(output,mode=="retrieval-multi"?"retrieval":mode=="letter-full"?"letter":"delivery");
            var retryDisk=ReadFixture(output);var retryRepository=new SaveRepository(retryDisk);var state=retryRepository.Load()!;var ledger=state.Town.Jobs;
            if(mode=="retrieval-multi"){
                state.Town.JobState.Offers.Restore(0);for(int i=0;i<state.Capacity;i++)state.Pockets[i]=1;
            }else if(mode=="letter-full"){
                ledger.EnsureMail();
                for(int i=0;i<128;i++){
                    var letter=new JobMailRecord{Id=ledger.Allocate(),Owner=ledger.Player(0),Sender=ledger.Npc(1),Recipient=ledger.Player(0),State=JobMailState.Inbox,SentAt=ledger.Watermark-86400,DueAt=ledger.Watermark};
                    ledger.StoreBody(ref letter,JobTextBody.Pack("An earlier letter.",18));ledger.Mail.Set(i,letter);state.Mail[i]=JobContent.Letter;
                }
                state.MailCount=128;
            }else{
                var service=state.ResidentJobs;service.Enable(ledger.TownId,ledger.Watermark);uint seed=0;
                while(JobSelection.RollReward(new JobRandom(seed),JobTemplate.DeliveryClothing)!=JobRewardCategory.Currency)seed++;
                state.Town.JobState.Rewards.Restore(seed);state.Wallet=GameState.WalletLimit;
                state.X=state.Town.Residents[0].PosX;state.Z=state.Town.Residents[0].PosZ;service.BeginConversation(0,out _);
                var offer=ledger.Offer(0,0);var accepted=service.Execute(service.Command(JobCommandKind.Accept,offer.Id,offer.Revision));
                if(accepted.Result!=JobResult.Ok)throw new Exception("Owed fixture acceptance");service.EndConversation();
                var job=ledger.Job(ledger.FindJob(accepted.Job));int target=ledger.FindNpc(job.Target);state.X=state.Town.Residents[target].PosX+1;state.Z=state.Town.Residents[target].PosZ;
                service.BeginConversation(target,out _);service.Execute(service.Command(JobCommandKind.Handoff,job.Id,job.Revision));service.EndConversation();
                var reward=ledger.Rewards[ledger.FindReward(job.Obligation)];
                if(reward.State!=JobRewardState.Owed||reward.Currency!=197)throw new Exception("Expected the deterministic earned currency obligation");
            }
            retryRepository.Save(state);foreach(var file in retryDisk.Files)File.WriteAllBytes(Path.Combine(output,file.Key),file.Value);return;
        }
        if(mode=="room"){
            Write(output,"mail");var roomDisk=ReadFixture(output);var repo=new SaveRepository(roomDisk);var room=repo.Load()!;
            room.Space=1;room.X=3;room.Z=4;room.BackpackTier=1;room.Shirt=Catalog.StripedShirt;Array.Clear(room.Pockets);room.Pockets[0]=Catalog.OakFloor;room.Pockets[1]=Catalog.LeafWallpaper;Array.Clear(room.Town.Jobs.Offers);repo.Save(room);
            foreach(var file in roomDisk.Files)File.WriteAllBytes(Path.Combine(output,file.Key),file.Value);return;
        }
        if(mode=="moving"||mode=="recovery"){
            CrossTown(output,"foreign");var home=ReadFixture(output);var destinationDisk=ReadFixture(output+"-destination");
            if(mode=="recovery"){
                var repository=new SaveRepository(home);var state=repository.Load()!;
                try{new OfflineTownTravel(repository,new SaveRepository(new FailWrites(destinationDisk))).Depart(0,state.Town.Jobs.Watermark);}catch(IOException){}
                state=repository.Load()!;if(!state.ProfileLocked(0)||!state.Town.Jobs.Trip.Pending)throw new Exception("Recovery fixture requires a genuinely committed source lock");
            }
            foreach(var file in home.Files)File.WriteAllBytes(Path.Combine(output,file.Key),file.Value);foreach(var file in destinationDisk.Files)File.WriteAllBytes(Path.Combine(output+"-destination",file.Key),file.Value);return;
        }
        if(mode=="foreign"||mode=="moved"){CrossTown(output,mode);return;}
        bool baseline=mode=="baseline",winter=mode is "snowman" or "outfits-winter",dressed=mode is "outfits-on" or "outfits-winter",destination=mode=="destination";long now=JobCalendar.At(2027,winter?1:7,15,mode=="letter"?7:12,mode=="letter"?58:0);var s=new GameState(destination?7320:7319){Name="Ari",Tutorial=baseline?6:7,HouseTier=1};s.Town.DaylightLocation=1;
        s.Rollover((int)(now/86400),2027*12+(winter?0:6));s.Tutorial=baseline?6:7;if(!baseline)s.ResidentJobs.Enable(destination?0x41544a4f425332UL:0x41544a4f425331UL,now);
        bool placed=false;for(int z=20;z<Town.Height-13&&!placed;z++)for(int x=4;x<20&&!placed;x++)placed=s.PlaceHouse(x,z,false);if(!placed)throw new Exception("Job fixture requires a home plot");
        for(int z=28;z<52;z++)for(int x=20;x<54;x++){s.Town.Tiles[z*Town.Width+x]=(byte)Terrain.Grass;s.Town.Elevation[z*Town.Width+x]=0;}
        for(int i=s.Town.GroundCount-1;i>=0;i--){var g=s.Town.Ground[i];if(g.X>=20&&g.X<54&&g.Z>=28&&g.Z<52)s.Town.RemoveGround(i);}
        s.Space=0;s.X=30;s.Z=42.3f;s.Facing=(float)Math.PI;
        Array.Clear(s.Pockets);s.Pockets[0]=Catalog.WritingPaper;s.Pockets[1]=2;s.Pockets[2]=2;s.Pockets[3]=2;s.Pockets[4]=2;s.Pockets[5]=Catalog.Pear;s.Pockets[6]=14;s.Pockets[7]=15;s.Pockets[8]=Catalog.Shovel;
        if(baseline){s.Pockets[0]=Catalog.WorkShirt;s.Pockets[5]=1;}
        for(int n=0;n<6;n++){var v=s.Town.Residents[n];v.Space=0;v.PosX=n==0?30:n==1?35.5f:30+(n-2.5f)*2.2f;v.PosZ=n==0?40.8f:n==1?42.3f:35;v.X=(int)v.PosX;v.Z=(int)v.PosZ;v.Dwell=36000;v.JourneyPhase=0;v.JourneyBuilding=-1;v.JourneyNextX=v.JourneyNextZ=-1;v.Yaw=0;s.Town.Jobs.SetOutfit(n,dressed?Catalog.BerryShirt:0);}
        if(mode=="mail"){s.Space=25;s.X=2;s.Z=2;s.Pockets[9]=Catalog.PlainWritingPaper;}
        if(mode=="letter"){s.Space=25;s.X=2;s.Z=2;var n=s.Town.Residents[0];n.Space=25;n.PosX=2;n.PosZ=.8f;n.X=2;n.Z=0;n.JourneyPhase=3;n.JourneyBuilding=8;n.Dwell=36000;}
        s.Shirt=dressed?Catalog.StripedShirt:0;
        if(mode=="retrieval")s.Town.JobState.Offers.Restore(1); // First referral draw stops the chain.
        if(mode=="flowers"||mode=="snowman"){s.Town.Residents[0].HomeX=21;s.Town.Residents[0].HomeZ=41;}
        var l=s.Town.Jobs;JobTemplate template=mode=="property"?JobTemplate.DeliveryProperty:mode=="retrieval"?JobTemplate.RetrievalBorrowed:mode=="fruit"?JobTemplate.FavorFruit:mode=="fish"?JobTemplate.FavorFish:mode=="insect"?JobTemplate.FavorInsect:mode=="flowers"?JobTemplate.FavorFlowers:mode=="ball"?JobTemplate.FavorBall:mode=="snowman"?JobTemplate.FavorSnowman:mode=="letter"?JobTemplate.FavorLetter:JobTemplate.DeliveryClothing;
        if(!baseline&&!mode.StartsWith("outfits")&&mode!="mail"&&mode!="maximum-mail"&&mode!="base-travel"&&!destination){
            l.Offers[0]=new VillagerJobOffer{Id=l.Allocate(),Owner=l.Player(0),Requester=l.Npc(0),Target=template<=JobTemplate.DeliveryProperty||template==JobTemplate.RetrievalBorrowed?l.Npc(1):default,Template=template,Item=template==JobTemplate.FavorFruit?Catalog.Pear:template==JobTemplate.DeliveryClothing?Catalog.BerryShirt:template==JobTemplate.RetrievalBorrowed||template==JobTemplate.DeliveryProperty?Catalog.Notebook:0,Revision=1,CreatedAt=now};
            if(mode=="ball"){l.ObjectDay=(int)(now/86400);l.World[0]=new JobWorldObject{Id=l.Allocate(),Kind=JobWorldKind.Ball,X=30,Z=41.6f,Radius=.32f,Acre=2*Town.ScreenColumns+1,CreatedAt=now};}
            if(mode=="snowman"){l.ObjectDay=(int)(now/86400);for(int i=0;i<2;i++)l.World[i]=new JobWorldObject{Id=l.Allocate(),Builder=l.Player(0),Kind=JobWorldKind.Snowball,X=30,Z=42.5f+i*1.8f,Radius=.7f,CreatedAt=now,Acre=2*Town.ScreenColumns+1};}
        }
        if(mode=="mail"||mode=="maximum-mail"){
            string body=mode=="mail"?"Dear Ari,\nThe town feels brighter with you here. Thank you for helping your neighbours!\nYour friend":string.Concat(Enumerable.Repeat("\U0001f642",1000));
            int profiles=mode=="maximum-mail"?4:1,count=mode=="maximum-mail"?128:1;
            for(int p=1;p<profiles;p++){s.SelectProfile(p);s.Tutorial=7;}s.SelectProfile(0);s.EnsureJobIdentities(l.TownId);
            for(int p=0;p<profiles;p++){
                for(int i=0;i<count;i++){l.EnsureMail();var m=new JobMailRecord{Id=l.Allocate(),Owner=l.Player(p),Sender=l.Npc(0),Recipient=l.Player(p),State=JobMailState.Inbox,Gift=i==0?Catalog.LeafShirt:0,SentAt=now-86400,DueAt=now};l.StoreBody(ref m,JobTextBody.Pack(body,mode=="mail"?body.Length:1000));l.Mail.Set(p*128+i,m);s.Profiles[p].Mail[i]=JobContent.Letter;}
                s.Profiles[p].MailCount=count;
            }
            s.MailCount=count;for(int i=0;i<count;i++)s.Mail[i]=JobContent.Letter;
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
    private static Disk ReadFixture(string directory)
    {var d=new Disk();foreach(string f in Directory.GetFiles(directory))if(SplitSaveCodec.IsFile(Path.GetFileName(f)))d.Files[Path.GetFileName(f)]=File.ReadAllBytes(f);return d;}
    private sealed class FailWrites(ISaveStorage storage):ISaveStorage
    {public byte[] Read(string name)=>storage.Read(name);public void Write(string name,byte[] bytes)=>throw new IOException("Intentional controller-qualification interruption before destination publication");}
    private static void CrossTown(string output,string mode)
    {
        Write(output,"base-travel");Write(output+"-destination","destination");var a=ReadFixture(output);var b=ReadFixture(output+"-destination");var ar=new SaveRepository(a);var br=new SaveRepository(b);long now=JobCalendar.At(2027,7,15,12);
        void Gate(GameState s){for(int z=9;z<24;z++)for(int x=28;x<43;x++){s.Town.Tiles[z*Town.Width+x]=(byte)Terrain.Grass;s.Town.Elevation[z*Town.Width+x]=0;}for(int i=s.Town.GroundCount-1;i>=0;i--){var g=s.Town.Ground[i];if(g.X>=28&&g.X<43&&g.Z>=9&&g.Z<24)s.Town.RemoveGround(i);}s.Space=0;s.X=36.5f;s.Z=14.5f;var n=s.Town.Residents[0];n.Space=0;n.PosX=36.5f;n.PosZ=16;n.X=36;n.Z=16;n.JourneyPhase=0;n.JourneyBuilding=-1;n.JourneyNextX=n.JourneyNextZ=-1;n.Dwell=36000;if(!s.CanWalk(s.X,s.Z,0))throw new Exception("Travel fixture gate must be reachable");}
        var h=ar.Load()!;var d=br.Load()!;Gate(h);Gate(d);ar.Save(h);br.Save(d);var trip=new OfflineTownTravel(ar,br);trip.Depart(0,now);h=trip.Return(now);JobIdentity target=br.Load()!.Town.Jobs.Npc(0);
        if(mode=="moved"){target=h.Town.Jobs.Npc(1);h=trip.ExchangeResidents(1,0,now);}
        d=br.Load()!;Gate(h);Gate(d);var l=h.Town.Jobs;l.Offers[0]=new VillagerJobOffer{Id=l.Allocate(),Owner=l.Player(0),Requester=l.Npc(0),Target=target,Template=mode=="foreign"?JobTemplate.DeliveryForeign:JobTemplate.DeliveryMoved,Item=Catalog.Notebook,CreatedAt=now,Revision=1};ar.Save(h);br.Save(d);
        foreach(var p in a.Files)File.WriteAllBytes(Path.Combine(output,p.Key),p.Value);foreach(var p in b.Files)File.WriteAllBytes(Path.Combine(output+"-destination",p.Key),p.Value);
        Console.WriteLine("Actual previous visit"+(mode=="moved"?" and NPC move":"")+" prepared for two-card controller capture");
    }
    internal static void Report(string directory,string mode,bool verify)
    {
        var disk=new Disk();foreach(string file in Directory.GetFiles(directory))disk.Files[Path.GetFileName(file)]=File.ReadAllBytes(file);
        var s=new SaveRepository(disk).Load()??throw new Exception("Controller run produced no committed town");var l=s.Town.Jobs;
        var jobs=Enumerable.Range(0,OfflineJobLedger.JobCapacity).Select(l.Job).Where(j=>j.Occupying).ToArray();
        JobTemplate? expected=mode switch{"delivery" or "owed"=>JobTemplate.DeliveryClothing,"property"=>JobTemplate.DeliveryProperty,"retrieval" or "retrieval-multi"=>JobTemplate.RetrievalBorrowed,"foreign"=>JobTemplate.DeliveryForeign,"moved"=>JobTemplate.DeliveryMoved,"fruit"=>JobTemplate.FavorFruit,"fish"=>JobTemplate.FavorFish,"insect"=>JobTemplate.FavorInsect,"flowers"=>JobTemplate.FavorFlowers,"ball"=>JobTemplate.FavorBall,"snowman" or "destroy-snowman"=>JobTemplate.FavorSnowman,"letter" or "letter-full"=>JobTemplate.FavorLetter,_=>null};
        bool completed=Enumerable.Range(0,OfflineJobLedger.TerminalCapacity).Any(i=>l.Terminal(i).Reason==JobEndReason.Success&&(!expected.HasValue||l.Terminal(i).Template==expected.Value))||jobs.Any(j=>j.Status==JobStatus.CompletedRetained&&(!expected.HasValue||j.Template==expected.Value));
        GameState? other=null;
        if(mode=="foreign"||mode=="moved"||mode=="moving"||mode=="recovery"||mode=="maximum-economy"||mode=="maximum-recovery"||mode=="maximum-post-travel"){
            other=new SaveRepository(ReadFixture(Path.Combine(directory,"..","records-b"))).Load()??throw new Exception("Controller run must export both actual town cards");
            if(expected.HasValue)completed|=Enumerable.Range(0,OfflineJobLedger.TerminalCapacity).Any(i=>other.Town.Jobs.Terminal(i).Owner==l.Player(0)&&other.Town.Jobs.Terminal(i).Template==expected.Value&&other.Town.Jobs.Terminal(i).Reason==JobEndReason.Success);
            if(verify&&(s.Visiting||other.Visiting||s.ProfileLocked(0)||l.Trip.Pending||other.Town.Jobs.Trip.Pending))throw new Exception("Both town cards must finish without an active or duplicated traveller");
        }
        if(verify&&expected.HasValue&&!completed)throw new Exception("Controller run has not completed its expected errand: "+mode);
        if(verify&&(mode=="maximum-economy"||mode=="maximum-recovery"||mode=="maximum-post-travel")){
            var guest=new SaveRepository(ReadFixture(Path.Combine(directory,"..","records-guest"))).Load()??throw new Exception("Maximum transfer needs its actual committed visitor checkpoint");
            if(!guest.Visiting||!guest.Town.Jobs.Job(99).Occupying||s.Town.Jobs.Trip.Phase!=OfflineTripPhase.Finished||!l.Job(19).Occupying||other==null||other.Town.Jobs.GuestProfile!=null)throw new Exception("The full visitor partition did not travel and return safely");
        }
        if(verify&&(mode=="maximum-mail"||mode=="maximum-economy"||mode=="maximum-recovery"||mode=="maximum-post-travel")){
            string maximumBody=string.Concat(Enumerable.Repeat("\U0001f642",1000));
            bool FullMail(GameState town)=>town.Sequence>=2&&Enumerable.Range(0,4).All(p=>town.Profiles[p].MailCount==128)&&Enumerable.Range(0,512).All(i=>{var m=town.Town.Jobs.Mail[i];return m.State==JobMailState.Inbox&&JobTextBody.Unpack(m.Body!,m.BodyOffset,m.BodyWords)==maximumBody;});
            if(!FullMail(s)||other!=null&&!FullMail(other))throw new Exception("Maximum Unicode mail did not survive the actual card checkpoint in both towns");
        }
        if(verify&&mode=="retrieval-multi"){
            var mid=new SaveRepository(ReadFixture(Path.Combine(directory,"..","records-mid"))).Load()??throw new Exception("No saved mid-chain checkpoint");
            if(!Enumerable.Range(0,OfflineJobLedger.JobCapacity).Any(i=>mid.Town.Jobs.Job(i).Template==JobTemplate.RetrievalBorrowed&&mid.Town.Jobs.Job(i).Borrowers>1))throw new Exception("No actual borrower referral was saved and reloaded");
        }
        if(verify&&mode=="destroy-snowman"&&l.World.Any(o=>o.Kind==JobWorldKind.Snowman))throw new Exception("Shovel destruction did not survive the actual card checkpoint");
        if(verify&&mode=="owed"&&(s.Wallet!=99196||l.Rewards.Any(r=>r.State==JobRewardState.Owed)||Enumerable.Range(0,s.Capacity).Any(s.IsJobItem)))throw new Exception("Reloaded earned payment was not collected exactly once");
        if(verify&&mode=="letter-full"&&(s.MailCount!=128||!Enumerable.Range(0,l.Mail.Length).Any(i=>l.Mail[i].State==JobMailState.Inbox&&l.Mail[i].Sender==l.Npc(0)&&l.Mail[i].Read&&l.Mail[i].Gift==0)))throw new Exception("The full-mailbox reply was not retried, read and collected");
        if(verify&&mode=="room"&&(l.HomeFloor(0)!=Catalog.OakFloor||l.HomeWall(0)!=Catalog.LeafWallpaper))throw new Exception("Room coverings were not applied and saved");
        if(verify&&mode=="recovery"&&(s.Visiting||s.ProfileLocked(0)||l.Trip.Pending))throw new Exception("Recovered traveller did not return home safely");
        if(verify&&mode=="moving"&&(other==null||l.NpcMove.Pending||other.Town.Jobs.NpcMove.Pending||l.Npc(1).Town!=other.Town.Jobs.TownId||other.Town.Jobs.Npc(0).Town!=l.TownId||l.Species(1)!=0||other.Town.Jobs.Species(0)!=1))throw new Exception("The confirmed NPC pair did not exchange identities and species on both cards");
        if(verify&&mode=="delivery"&&l.Outfit(1)!=Catalog.BerryShirt)throw new Exception("Delivery did not change the actual recipient's clothing");
        if(verify&&(mode=="outfits-on"||mode=="outfits-winter")&&(s.Shirt!=Catalog.StripedShirt||Enumerable.Range(0,6).Any(n=>l.Outfit(n)!=Catalog.BerryShirt)))throw new Exception("The player and all six species must retain their actual outfits");
        if(verify&&mode=="mail"&&s.Find(Catalog.LeafShirt)<0)throw new Exception("The real mail attachment was not collected");
        if(verify&&mode=="mail"&&(s.Pockets[0]!=Catalog.LeafShirt||s.Pockets[5]!=0||s.Pockets[9]!=Catalog.PlainWritingPaper||
            !Enumerable.Range(OfflineJobLedger.InboxCount,OfflineJobLedger.OutboxCapacity).Any(i=>l.Mail[i].State==JobMailState.Queued&&l.Mail[i].Gift==Catalog.Pear&&l.Mail[i].Body!=null&&JobTextBody.Unpack(l.Mail[i].Body!,l.Mail[i].BodyOffset,l.Mail[i].BodyWords)=="Hello!")))
            throw new Exception("Backpack mail controls did not preserve the spare stationery and queue the chosen gift");
        var result=new{mode,passed=verify,sequence=s.Sequence,tripPhase=l.Trip.Phase.ToString(),tripPending=l.Trip.Pending,profileLocked=s.ProfileLocked(0),activeProfile=s.ActiveProfile,visiting=s.Visiting,position=new{s.X,s.Z,s.Space},wallet=s.Wallet,completed,mailCount=s.MailCount,pockets=s.Pockets,bindings=Enumerable.Range(0,s.Capacity).Where(s.IsJobItem),outfits=Enumerable.Range(0,6).Select(l.Outfit),jobs=jobs.Select(j=>new{template=j.Template.ToString(),status=j.Status.ToString(),target=l.FindNpc(j.Target),j.Item,j.FlowerBaseline,j.HomeAcre}),terminals=Enumerable.Range(0,16).Select(l.Terminal).Where(t=>!t.Job.IsEmpty).Select(t=>new{template=t.Template.ToString(),reason=t.Reason.ToString(),t.At})};
        string json=JsonSerializer.Serialize(result,new JsonSerializerOptions{WriteIndented=true});Console.WriteLine(json);File.WriteAllText(Path.Combine(directory,"..","jobs-state.json"),json+"\n");
    }
}
