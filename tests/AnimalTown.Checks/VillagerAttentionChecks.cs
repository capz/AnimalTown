using AnimalTown;

internal static class VillagerAttentionChecks
{
    internal static void Run(Action<bool,string> check)
    {
        GameState Fresh(){var s=new GameState(841);Array.Fill(s.Town.Tiles,(byte)Terrain.Grass);Array.Clear(s.Town.Elevation);s.Town.Day=50;s.X=50;s.Z=50;for(int i=0;i<6;i++){var v=s.Town.Residents[i];v.PosX=49+i*.3f;v.PosZ=47;v.Met=1;v.LastSeenDay=48;}return s;}
        int Count(VillagerAttention a){int count=0;for(int i=0;i<6;i++)if(a.Kind(i)!=0)count++;return count;}
        var s=Fresh();var a=new VillagerAttention();a.Update(s,0,720,true,1);
        check(a.Kind(0)==VillagerAttention.Greeting,"reunion greeting follows two days without a nearby encounter");
        check(s.Town.Residents[0].LastSeenDay==50&&s.Town.Residents[0].LastGreetingDay==int.MinValue,"seeing records the day, but hidden notices do not consume the greeting");
        a.Shown(s,0,0);check(s.Town.Residents[0].LastGreetingDay==50,"an actually drawn greeting starts the daily cooldown");
        a.Update(s,3999999,720,true,1);check(a.Kind(0)!=0,"friendly notice lasts four seconds");a.Update(s,4000000,720,true,1);check(a.Kind(0)==0,"friendly notice expires at four seconds");
        a.Update(s,5000000,720,true,1);check(a.Kind(0)==0,"remaining nearby does not repeat the greeting");
        s.Town.Day=51;a.Update(s,6000000,720,true,1);check(a.Kind(0)==0,"daily encounters prevent an absence greeting");
        s=Fresh();a=new VillagerAttention();s.Town.Residents[1].Request=14;s.Town.Residents[1].RequestDay=50;s.Receive(14);
        a.Update(s,0,720,true,3);check(a.Kind(1)==VillagerAttention.RequestReady&&a.Kind(0)==0,"carried request item takes priority over a greeting");
        a.Shown(s,1,0);a.Update(s,800000,720,true,3);check(a.Kind(0)==VillagerAttention.Greeting&&Count(a)==2,"a second named notice can coexist after the stagger");
        a.Shown(s,0,800000);a.Update(s,1000000,720,true,63);check(Count(a)<=2,"six eligible residents cannot exceed the two-notice budget");
        int friendship=s.Town.Residents[1].Friendship,wallet=s.Wallet;check(s.Find(14)>=0&&s.Town.Residents[1].Request==14&&s.Wallet==wallet&&s.Town.Residents[1].Friendship==friendship,"notices do not consume an item, complete errands or grant rewards");
        a.Spoke(1,1000000);s.Talk(1);a.Update(s,1100000,720,true,63);check(a.Kind(1)==0&&s.Town.Residents[1].Request==0&&s.Find(14)<0,"only ordinary conversation resolves the pending request");
        s=Fresh();a=new VillagerAttention();a.Update(s,0,720,true,3);for(int i=0;i<2;i++)if(a.Kind(i)!=0)a.Shown(s,i,0);a.Update(s,800000,720,true,3);for(int i=0;i<2;i++)if(a.Kind(i)!=0)a.Shown(s,i,800000);
        s.Town.Residents[2].Request=14;s.Town.Residents[2].RequestDay=50;s.Receive(14);a.Update(s,1600000,720,true,7);
        check(a.Kind(2)==VillagerAttention.RequestReady&&Count(a)==2,"a new urgent request preempts a friendly bubble when both slots are occupied");
        s=Fresh();a=new VillagerAttention();s.Town.Residents[0].LastSeenDay=50;s.Town.Residents[0].Request=14;s.Town.Residents[0].RequestDay=50;
        a.Update(s,0,299,true,1);a.Spoke(0,0);a.Update(s,14999999,300,true,1);check(a.Kind(0)==0,"conversation quiet time prevents an immediate follow-up notice");
        a.Update(s,15000000,300,true,1);check(a.Kind(0)==VillagerAttention.Deadline,"urgent notices resume after the fifteen-second quiet period");
        s=Fresh();a=new VillagerAttention();var v=s.Town.Residents[0];v.LastSeenDay=50;v.Request=14;v.RequestDay=50;
        a.Update(s,0,299,true,1);check(a.Kind(0)==0,"ordinary pending errands are not urgent all day");
        a.Update(s,1000000,300,true,1);check(a.Kind(0)==VillagerAttention.Deadline,"05:00 starts the final-hour warning before the 06:00 rollover");
        a.Shown(s,0,1000000);a.Update(s,6999999,359,true,1);check(a.Kind(0)!=0,"urgent notice lasts six seconds");
        a.Update(s,7000000,359,true,1);check(a.Kind(0)==0,"urgent notices also expire and have a daily cooldown");
        s.Rollover(51,0);a.Update(s,8000000,360,true,1);check(a.Kind(0)==0&&v.Request==0,"rollover removes an obsolete urgent request");
        s=Fresh();a=new VillagerAttention();a.Update(s,0,720,false,63);check(Count(a)==0&&s.Town.Residents[0].LastSeenDay==48,"menus and interiors neither display nor record outdoor encounters");
        a.Update(s,1000000,720,true,1);a.Update(s,1100000,720,false,1);check(Count(a)==0,"opening dialogue hides ambient notices independently");
        s=Fresh();a=new VillagerAttention();s.Town.Residents[0].Space=16;a.Update(s,0,720,true,1);check(Count(a)==0,"residents indoors cannot call through the exterior world");
        s=Fresh();a=new VillagerAttention();s.Town.Tiles[48*Town.Width+49]=(byte)Terrain.Building;a.Update(s,0,720,true,1);check(Count(a)==0,"buildings block nearby attention sight lines");
        s=Fresh();a=new VillagerAttention();s.Town.Residents[0].PosX=70;a.Update(s,0,720,true,1);check(Count(a)==0,"far and offscreen residents remain quiet");
        s=Fresh();a=new VillagerAttention();s.Town.Residents[0].LastSeenDay=int.MinValue;a.Update(s,0,720,true,1);check(Count(a)==0&&s.Town.Residents[0].LastSeenDay==50,"unknown history establishes a baseline without inventing an absence");
        s=Fresh();a=new VillagerAttention();a.Update(s,5000000,720,true,1);a.Update(s,1,720,false,0);check(Count(a)==0,"clock rollback resets transient durations safely");
        s=Fresh();a=new VillagerAttention();for(long t=0;t<12000000;t+=100000){a.Update(s,t,720,true,63);for(int i=0;i<6;i++)if(a.Kind(i)!=0)a.Shown(s,i,t);check(Count(a)<=2,"the bounded notice budget holds through repeated expiration");}
        check(AttentionBubbleLayout.Project(s.Town,50,48,50,50,0,out float hx,out float hy)&&hx==320&&hy>56&&hy<350,"head anchors project into the actual cylinder camera");
        check(!AttentionBubbleLayout.Project(s.Town,100,48,50,50,0,out _,out _)&&!AttentionBubbleLayout.Project(s.Town,50,70,50,50,0,out _,out _),"offscreen and behind-camera bubbles are rejected");
        check(AttentionBubbleLayout.Place(180,250,false,0,0,out float x,out float y)&&x>=18&&y>=22&&x+AttentionBubbleLayout.Width<=622,"bubble remains inside safe UI margins");
        if(AttentionBubbleLayout.Place(300,250,true,x,y,out float nx,out float ny))check(nx>=x+AttentionBubbleLayout.Width+10||nx+AttentionBubbleLayout.Width+10<=x||ny>=y+AttentionBubbleLayout.Height+10||ny+AttentionBubbleLayout.Height+10<=y,"two fitted bubbles cannot overlap");
        var journey=new VillagerJourney{AttentionMask=1};s=Fresh();float px=s.Town.Residents[0].PosX,pz=s.Town.Residents[0].PosZ;
        for(int n=0;n<30;n++)journey.Tick(s);
        check(s.Town.Residents[0].PosX==px&&s.Town.Residents[0].PosZ==pz&&!journey.Moving[0],"an ambient caller pauses briefly without moving its saved route");
        check(PlayerMovement.Move(s,0,1,0,0,false,out _,out _),"villager attention never locks player movement");
        journey.AttentionMask=0;for(int n=0;n<500;n++)journey.Tick(s);
        check(s.Town.Residents[0].PosX!=px||s.Town.Residents[0].PosZ!=pz,"the existing villager journey resumes after the callout");
        check(AttentionBubbleLayout.Place(180,80,false,0,0,out float sx,out float sy)&&(180<sx||180>sx+AttentionBubbleLayout.Width),"near-horizon bubbles sit beside their speaker instead of covering a face");
        var navigation=new VillagerNavigation();s=Fresh();navigation.Tick(s);navigation.Target(0,100,100);navigation.Tick(s);navigation.Paused[0]=true;navigation.Tick(s);navigation.Paused[0]=false;navigation.Tick(s);
        check(navigation.Routes[0].GoalX==100&&navigation.Routes[0].GoalZ==100,"pausing an in-progress search retains the villager's original destination");
        // Append-only migrations from the actual preceding formats.
        s=Fresh();s.Town.Residents[0].LastSeenDay=42;s.Town.Residents[0].LastGreetingDay=49;s.Town.Residents[0].LastUrgentDay=48;s.Wallet=1234;s.Pockets[2]=18;s.Town.SetTerrain(40,40,Terrain.Beach);
        void Preserved(GameState restored){check(restored.Town.Residents[0].LastSeenDay==42&&restored.Town.Residents[0].LastGreetingDay==49&&restored.Town.Residents[0].LastUrgentDay==48,"encounter and notice history survives saves");check(restored.Wallet==1234&&restored.Pockets[2]==18&&restored.Town.At(40,40)==(int)Terrain.Beach,"new history preserves possessions, balances and edited terrain");}
        Preserved(SaveCodec.Decode(SaveCodec.Encode(s)));Preserved(SplitSaveCodec.Decode(SplitSaveCodec.Encode(s)));
        void Int(byte[] b,int at,int value){for(int i=0;i<4;i++)b[at+i]=(byte)((uint)value>>(8*i));}
        var legacy=SaveCodec.Encode(s)[..^72];Int(legacy,4,7);Int(legacy,16,legacy.Length);Int(legacy,20,(int)SaveCodec.Crc(legacy,24,legacy.Length-24));
        var old=SaveCodec.Decode(legacy);check(old.Town.Residents[0].LastSeenDay==s.Town.Day&&old.Town.Residents[0].LastGreetingDay==int.MinValue&&old.Wallet==1234,"v7 combined save initializes conservative encounter history without regenerating terrain");
        var parts=SplitSaveCodec.Encode(s);for(int i=0;i<4;i++){var b=parts[i];if(i==3)b=b[..^(72+4)].Concat(new byte[4]).ToArray();Int(b,4,3);Int(b,24,b.Length);Int(b,b.Length-4,(int)SaveCodec.Crc(b,0,b.Length-4));parts[i]=b;}
        old=SplitSaveCodec.Decode(parts);check(old.Town.Residents[0].LastSeenDay==s.Town.Day&&old.Town.Residents[0].LastUrgentDay==int.MinValue&&old.Town.Tiles.SequenceEqual(s.Town.Tiles),"v3 split save retains existing terrain and gains conservative history");
        var saved=SaveCodec.Decode(SaveCodec.Encode(s));a=new VillagerAttention();a.Update(saved,0,720,true,1);a.Shown(saved,0,0);saved=SplitSaveCodec.Decode(SplitSaveCodec.Encode(saved));a=new VillagerAttention();a.Update(saved,0,720,true,1);check(a.Kind(0)==0,"reloading does not repeat an already displayed greeting");
        Console.WriteLine("Villager attention, projected bubble layout and v7/v3 migration checks passed");
    }
}
