using AnimalTown;
using Dolphin.Storage;

int checks=0;
void Check(bool pass,string why){checks++;if(!pass)throw new Exception(why);}
Check(CardSaveError.Describe("mount",2,-3,0,"Memory card unavailable.").Contains("CARD -3\nNo memory card detected"),"a stale transaction marker cannot hide the driver's no-card result");
Check(CardSaveError.Describe("mount",2,-2,0,"").Contains("did not identify a memory card"),"raw wrong-device -2 differs from the old positive stage 2");
Check(CardSaveError.Describe("mount",2,-6,0,"").Contains("directory could not be verified. Data kept."),"broken directory is reported without prompting a format");
Check(CardSaveError.Describe("mount",2,0,-28,"").Contains("recovery / error 28\nMore free blocks"),"mount-time journal recovery reports space failure separately");
Check(CardSaveError.Describe("mount",2,0,-5,"").Contains("Pending save could not be recovered. Data kept."),"unreadable recovery retains the journal");
Check(CardSaveError.Describe("mount",0,-2001,0,"").Contains("Not enough memory"),"workarea allocation failure differs from missing card");
Check(CardSaveError.Describe("save set",2028,-3,0,"write failed")=="Save failed / save set / card 2028\nwrite failed","old mount diagnostics do not override a later write failure");
OnboardingDraft NewVillager()
{
    var source=new GameState(7319);source.Profiles[0].Created=false;
    var draft=new OnboardingDraft(source,0,false);draft.SetName("Emi");draft.SetSpecies(0);
    bool placed=false;
    for(int z=1;z<Town.Height-13&&!placed;z++)for(int x=1;x<Town.Width-15&&!placed;x++)placed=draft.PlaceHome(x,z);
    if(!placed)throw new Exception("Generated town needs a home");return draft;
}
var banner=new byte[6144];var icon=new byte[2048];
var card=new MemoryCard();var repository=new SaveRepository(new CardSaveStorage(card,banner,icon));
using(var draft=NewVillager()){
    draft.Commit(repository,false,new string[0]);
    var loaded=repository.Load()!;
    Check(loaded.Name=="Emi"&&loaded.Species==0&&loaded.HousePlaced&&loaded.Profiles[0].Created,"a generated first villager saves and reloads with 24 free blocks");
    Check(card.Files.Count==18&&card.Used<=22&&card.Peak<=24,"both complete banks and durable journal fit the bounded card space");
    foreach(var record in card.Metadata){
        bool town=record.Key=="AT-Town0"||record.Key=="AT-Town1";
        Check(town?ReferenceEquals(record.Value.Banner,banner)&&ReferenceEquals(record.Value.Icon,icon):record.Value.Banner==null&&record.Value.Icon==null,"only town records retain the save banner/icon: "+record.Key);
        Check(record.Value.Title=="AnimalTown"&&record.Value.Comment==BuildInfo.Version,"every component retains browser identity and version: "+record.Key);
    }
    int writes=card.Writes;repository.Save(loaded);Check(card.Writes==writes,"an unchanged save does not rewrite records to change their presentation");
    Console.WriteLine("Generated first town: "+card.Used+" resident blocks / "+card.Peak+" peak including journal.");
}
card=new MemoryCard{Capacity=10};repository=new SaveRepository(new CardSaveStorage(card,banner,icon));
using(var draft=NewVillager()){
    bool failed=false;try{draft.Commit(repository,false,new string[0]);}catch(IOException){failed=true;}
    Check(failed&&draft.State.Sequence==0&&draft.State.Name=="Emi"&&draft.State.HousePlaced,"out-of-space leaves the onboarding draft intact");
    card.Capacity=24;draft.Commit(repository,true,card.Files.Keys.ToArray());
    Check(repository.Load()!.Name=="Emi"&&card.Peak<=24,"retry publishes only this draft's interrupted first save after freeing space");
}
Console.WriteLine(checks+" card adapter checks passed: bounded first save, artwork, journals, readback and retry; host model, not hardware.");
