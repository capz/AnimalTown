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
var card=new MemoryCard();var adapter=new CardSaveStorage(card,banner,icon);var repository=new SaveRepository(adapter);
using(var draft=NewVillager()){
    draft.Commit(repository,false,new string[0]);
    var loaded=repository.Load()!;
    Check(loaded.Name=="Emi"&&loaded.Species==0&&loaded.HousePlaced&&loaded.Profiles[0].Created,"a generated first villager saves and reloads with 24 free blocks");
    Check(card.Files.Count==1&&card.Files.ContainsKey("AnimalTown")&&card.Used==18&&card.Peak==18,"one browser item contains both save banks and internal recovery space");
    foreach(var record in card.Metadata){
        Check(record.Key=="AnimalTown"&&record.Value.Banner!.SequenceEqual(banner)&&record.Value.Icon!.SequenceEqual(icon),"one shared banner/icon pair");
        Check(record.Value.Title=="AnimalTown","container retains browser identity");
    }
    int writes=card.Writes;repository.Save(loaded);Check(card.Writes==writes,"an unchanged save does not rewrite records to change their presentation");
    Console.WriteLine("Generated first town: "+card.Used+" resident blocks / "+card.Peak+" peak including internal recovery space.");
}
card=new MemoryCard{Capacity=10};adapter=new CardSaveStorage(card,banner,icon);repository=new SaveRepository(adapter);
using(var draft=NewVillager()){
    bool failed=false;try{draft.Commit(repository,false,new string[0]);}catch(IOException){failed=true;}
    Check(failed&&draft.State.Sequence==0&&draft.State.Name=="Emi"&&draft.State.HousePlaced,"out-of-space leaves the onboarding draft intact");
    card.Capacity=24;var retryNames=adapter.Names();draft.Commit(repository,retryNames.Any(SplitSaveCodec.IsFile),retryNames);
    Check(repository.Load()!.Name=="Emi"&&card.Peak<=24,"retry publishes only this draft's interrupted first save after freeing space");
}
// A complete old layout is copied and verified before old directory entries go.
card=new MemoryCard{Capacity=80};
using(var draft=NewVillager()){
    draft.Commit(new SaveRepository(new OldFiles(card)),false,new string[0]);
}
card.WriteSave("options",new byte[]{4,5},new MemoryCardSaveOptions());
card.WriteSave("dev_config",new byte[]{6,7},new MemoryCardSaveOptions());
card.WriteSave("Unrelated",new byte[]{8},new MemoryCardSaveOptions());
int oldCount=card.Files.Count;
var migrating=new CardArchive(card,banner,icon);
Check(card.Files.Count==oldCount,"opening an old layout does not migrate or save");
card.FailDelete=1;bool interrupted=false;
try{migrating.Write("options",new byte[]{9});}catch(IOException){interrupted=true;}
Check(interrupted&&card.Files.ContainsKey("AnimalTown"),"interrupted cleanup keeps the verified destination");
Check(new SaveRepository(new CardSaveStorage(card,banner,icon)).Load()!.Name=="Emi","published migration is loadable before cleanup finishes");
var recovered=new CardArchive(card,banner,icon);recovered.Write("options",new byte[]{9});
Check(card.Files.Count==2&&card.Files.ContainsKey("Unrelated"),"cleanup resumes and preserves unrelated files");
Check(recovered.Read("dev_config").SequenceEqual(new byte[]{6,7})&&recovered.Read("options")[0]==9,"debug and options remain independent records");
TownDataDeletion.Erase(new ArchiveFiles(recovered));
Check(new SaveRepository(new CardSaveStorage(card,banner,icon)).Load()==null,"logical town deletion removes every fallback bank");
Check(recovered.Read("options")[0]==9&&recovered.Read("dev_config")[0]==6,"town deletion retains preferences and debug data");
// Exhausted physical space must preserve every old directory item.
card=new MemoryCard{Capacity=80};using(var draft=NewVillager())draft.Commit(new SaveRepository(new OldFiles(card)),false,new string[0]);
oldCount=card.Files.Count;card.Capacity=card.Used;bool noSpace=false;
try{new CardArchive(card,banner,icon).Write("options",new byte[]{1});}catch(IOException){noSpace=true;}
Check(noSpace&&card.Files.Count==oldCount&&new SaveRepository(new OldFiles(card)).Load()!.Name=="Emi","migration without workspace leaves all source files intact");
card=new MemoryCard{Capacity=80};using(var draft=NewVillager())draft.Commit(new SaveRepository(new OldFiles(card)),false,new string[0]);
oldCount=card.Files.Count;card.FailBlockWrite=2;bool copyCut=false;
try{new CardArchive(card,banner,icon).Write("options",new byte[]{3});}catch(IOException){copyCut=true;}
Check(copyCut&&card.Files.Count==oldCount+1&&new SaveRepository(new OldFiles(card)).Load()!.Name=="Emi","a cut before migration publication retains every old file");
var resumed=new CardArchive(card,banner,icon);resumed.Write("options",new byte[]{3});
Check(card.Files.Count==1&&new SaveRepository(new CardSaveStorage(card,banner,icon)).Load()!.Name=="Emi","partial copy resumes and consolidates only after verification");
card=new MemoryCard();var preferences=new CardArchive(card,banner,icon);preferences.Write("options",new byte[]{5});preferences.Write("dev_config",new byte[]{7});
using(var draft=NewVillager())draft.Commit(new SaveRepository(new CardSaveStorage(card,banner,icon)),false,new string[0]);
var all=new CardArchive(card,banner,icon);
Check(card.Files.Count==1&&all.Read("options")[0]==5&&all.Read("dev_config")[0]==7,"a preferences-first card adds the town to the same item without losing settings");
Console.WriteLine(checks+" card adapter checks passed: consolidated save/reload, no-op save, capacity, migration interruption/retry, independent records and deletion; host model, not hardware.");
sealed class OldFiles(MemoryCard card) : ISaveStorage
{
    public byte[] Read(string name)=>card.ReadAllBytes(name);
    public void Write(string name,byte[] bytes)=>card.WriteSave(name,bytes,new MemoryCardSaveOptions{Title="AnimalTown"});
}
sealed class ArchiveFiles(CardArchive archive) : ITownDataFiles
{
    public string[] Names()=>archive.Names();public byte[] Read(string name)=>archive.Read(name);
    public void Write(string name,byte[] bytes)=>archive.Write(name,bytes);public void Delete(string name)=>archive.Delete(name);
}
