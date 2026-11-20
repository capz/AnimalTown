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
        bool winter=mode=="snowman";long now=JobCalendar.At(2027,winter?1:7,15,12);var s=new GameState(7319){Name="Ari",Tutorial=7,HouseTier=1};s.Town.DaylightLocation=1;
        s.Rollover((int)(now/86400),2027*12+(winter?1:7));s.Tutorial=7;s.ResidentJobs.Enable(0x41544a4f425331UL,now);
        bool placed=false;for(int z=20;z<Town.Height-13&&!placed;z++)for(int x=4;x<20&&!placed;x++)placed=s.PlaceHouse(x,z,false);if(!placed)throw new Exception("Job fixture requires a home plot");
        for(int z=28;z<52;z++)for(int x=20;x<54;x++){s.Town.Tiles[z*Town.Width+x]=(byte)Terrain.Grass;s.Town.Elevation[z*Town.Width+x]=0;}
        for(int i=s.Town.GroundCount-1;i>=0;i--){var g=s.Town.Ground[i];if(g.X>=20&&g.X<54&&g.Z>=28&&g.Z<52)s.Town.RemoveGround(i);}
        s.Space=0;s.X=30;s.Z=42.3f;s.Facing=(float)Math.PI;
        Array.Clear(s.Pockets);s.Pockets[0]=Catalog.WritingPaper;s.Pockets[1]=2;s.Pockets[2]=2;s.Pockets[3]=2;s.Pockets[4]=2;s.Pockets[5]=Catalog.Pear;s.Pockets[6]=14;s.Pockets[7]=15;s.Pockets[8]=Catalog.Shovel;
        for(int n=0;n<6;n++){var v=s.Town.Residents[n];v.Space=0;v.PosX=n==0?30:30+(n-2.5f)*2.2f;v.PosZ=n==0?40.8f:35;v.X=(int)v.PosX;v.Z=(int)v.PosZ;v.Dwell=36000;v.JourneyPhase=0;v.JourneyBuilding=-1;v.JourneyNextX=v.JourneyNextZ=-1;v.Yaw=0;s.Town.Jobs.SetOutfit(n,mode=="outfits-on"?Catalog.BerryShirt:0);}
        s.Shirt=mode=="outfits-on"?Catalog.StripedShirt:0;
        var l=s.Town.Jobs;JobTemplate template=mode=="retrieval"?JobTemplate.RetrievalBorrowed:mode=="fruit"?JobTemplate.FavorFruit:mode=="fish"?JobTemplate.FavorFish:mode=="insect"?JobTemplate.FavorInsect:mode=="flowers"?JobTemplate.FavorFlowers:mode=="ball"?JobTemplate.FavorBall:mode=="snowman"?JobTemplate.FavorSnowman:mode=="letter"?JobTemplate.FavorLetter:JobTemplate.DeliveryClothing;
        if(!mode.StartsWith("outfits")&&mode!="mail"&&mode!="maximum-mail"){
            l.Offers[0]=new VillagerJobOffer{Id=l.Allocate(),Owner=l.Player(0),Requester=l.Npc(0),Target=template<=JobTemplate.DeliveryProperty||template==JobTemplate.RetrievalBorrowed?l.Npc(1):default,Template=template,Item=template==JobTemplate.FavorFruit?Catalog.Pear:template==JobTemplate.DeliveryClothing?Catalog.BerryShirt:template==JobTemplate.RetrievalBorrowed?Catalog.Notebook:0,Revision=1,CreatedAt=now};
            if(mode=="ball"){l.ObjectDay=(int)(now/86400);l.World[0]=new JobWorldObject{Id=l.Allocate(),Kind=JobWorldKind.Ball,X=30,Z=41.6f,Radius=.32f,Acre=2*Town.ScreenColumns+1,CreatedAt=now};}
            if(mode=="snowman"){l.ObjectDay=(int)(now/86400);for(int i=0;i<2;i++)l.World[i]=new JobWorldObject{Id=l.Allocate(),Kind=JobWorldKind.Snowball,X=30,Z=42.5f+i*1.8f,Radius=.7f,CreatedAt=now,Acre=2*Town.ScreenColumns+1};}
        }
        if(mode=="mail"||mode=="maximum-mail"){
            string body=mode=="mail"?"Dear Ari,\nThe town feels brighter with you here. Thank you for helping your neighbours!\nYour friend":string.Concat(Enumerable.Repeat("\U0001f642",1000));
            int count=mode=="maximum-mail"?128:1;for(int i=0;i<count;i++){l.EnsureMail();var m=new JobMailRecord{Id=l.Allocate(),Owner=l.Player(0),Sender=l.Npc(0),Recipient=l.Player(0),State=JobMailState.Inbox,Gift=i==0?Catalog.LeafShirt:0,SentAt=now-86400,DueAt=now};l.StoreBody(ref m,JobTextBody.Pack(body,mode=="mail"?body.Length:1000));l.Mail[i]=m;s.Mail[i]=JobContent.Letter;}s.MailCount=count;
        }
        var disk=new Disk();var repository=new SaveRepository(disk);repository.Save(s);repository.Save(s);Directory.CreateDirectory(output);foreach(var p in disk.Files)File.WriteAllBytes(Path.Combine(output,p.Key),p.Value);
        File.WriteAllText(Path.Combine(output,"fixture.json"),JsonSerializer.Serialize(new{mode,now,seed=s.Town.Seed,sequence=s.Sequence,files=disk.Files.Keys},new JsonSerializerOptions{WriteIndented=true}));Console.WriteLine("Disposable job fixture: "+Path.GetFullPath(output));
    }
}
