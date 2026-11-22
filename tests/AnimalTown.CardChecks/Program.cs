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
var card=new MemoryCard();var adapter=new CardSaveStorage(card,banner,icon,4);var repository=new SaveRepository(adapter);
using(var draft=NewVillager()){
    draft.Commit(repository,false,new string[0]);
    var loaded=repository.Load()!;
    Check(loaded.Name=="Emi"&&loaded.Species==0&&loaded.HousePlaced&&loaded.Profiles[0].Created,"a generated first villager saves and reloads with 40 free blocks");
    Check(card.Files.Count==2&&card.Files.ContainsKey("AnimalTown")&&card.Files.ContainsKey("AnimalTown-Villager1")&&card.Used==36&&card.Peak==39,"the town and first villager occupy separate browser entries and blocks");
    foreach(var record in card.Metadata){
        Check(record.Value.Banner!.SequenceEqual(banner)&&record.Value.Icon!.SequenceEqual(icon),"both entries retain card artwork");
        Check(record.Value.Title=="AnimalTown","container retains browser identity");
    }
    int writes=card.Writes;repository.Save(loaded);Check(card.Writes==writes,"an unchanged save does not rewrite records to change their presentation");
    long jobNow=AnimalTown.Jobs.JobCalendar.At(2027,7,15,12);loaded.ResidentJobs.Enable(23495,jobNow);repository.Save(loaded);
    writes=card.Writes;loaded.ResidentJobs.AdvanceTo(jobNow+1);repository.Save(loaded);
    Console.WriteLine("Clock card writes: "+repository.LastWrittenRecords+" logical / "+repository.LastWrittenBytes+" encoded bytes / "+(card.Writes-writes)+" sectors");
    Check(repository.LastWrittenRecords==4&&repository.LastWrittenBytes==1008&&card.Writes-writes==8,"a pristine job clock writes four logical records and eight verified 8 KiB sectors");
    loaded.ObserveJobAcre(0);repository.Save(loaded);writes=card.Writes;loaded.ResidentJobs.AdvanceTo(jobNow+2);repository.Save(loaded);
    Check(repository.LastWrittenRecords==4&&repository.LastWrittenBytes==1336&&card.Writes-writes==8,"an active job clock writes four logical records and eight verified 8 KiB sectors, without an economic rewrite");
    var prefixMeter=new PrefixMeter(adapter);new SaveRepository(prefixMeter).VerifyRevision(loaded);
    Check(prefixMeter.PrefixReads==1&&prefixMeter.EconomicReads==0,"travel verifies the entire economic checksum while retaining only a 732-byte identity prefix");
    prefixMeter.PrefixReads=prefixMeter.EconomicReads=0;var travelPreview=new SaveRepository(prefixMeter).ReadTravelPreview();
    Check(travelPreview.TownId==loaded.Town.Jobs.TownId&&travelPreview.Sequence==loaded.Sequence&&travelPreview.Seed==loaded.Town.Seed&&Enumerable.Range(0,6).All(i=>travelPreview.NpcName(i)==loaded.Town.Residents[i].Name),"travel preview reads the committed town identity, revision and six NPC names without constructing another town");
    Check(prefixMeter.PrefixReads==1&&prefixMeter.EconomicReads==0,"travel preview validates the full referenced economy while retaining only its small identity prefix");
    var badPrefix=card.Copy();var pages=badPrefix.RecordPages["AnimalTown"];var rootBytes=BitConverter.ToInt32(pages[0],8)>BitConverter.ToInt32(pages[1],8)?pages[0]:pages[1];
    for(int i=0;i<64;i++){
        int at=32+i*48;string record=System.Text.Encoding.ASCII.GetString(rootBytes,at,32).TrimEnd('\0');if(!record.StartsWith("AT-Shops"))continue;
        int page=BitConverter.ToInt32(rootBytes,at+32),offset=BitConverter.ToInt32(rootBytes,at+36),length=BitConverter.ToInt32(rootBytes,at+40);int altered=offset+length-8;
        pages[page+altered/8192][altered%8192]^=1;
    }
    bool prefixCorrupt=false;try{new SaveRepository(adapter).VerifyRevision(loaded);}catch(InvalidOperationException){prefixCorrupt=true;}
    Check(prefixCorrupt,"a prefix read rejects corruption beyond the retained prefix in both referenced economic copies");
    bool previewCorrupt=false;try{new SaveRepository(adapter).ReadTravelPreview();}catch(InvalidOperationException){previewCorrupt=true;}
    Check(previewCorrupt,"travel preview rejects full-record corruption beyond the retained identity prefix");MemoryCard.Slots[4]=card;MemoryCard.Current=card;
    // The extra town-history pages must fit on subsequent copy-on-write saves,
    // not only the first save into an empty container.
    for(int save=1;save<=12;save++){
        loaded.Town.Traffic.Visits[100]=(byte)save;
        repository.Save(loaded);
        Check(new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()!.Town.Traffic.Visits[100]==save,"changing traffic survives repeated saves without exhausting recovery space");
    }
    Console.WriteLine("Generated first town: "+card.Used+" resident blocks / "+card.Peak+" peak including internal recovery space.");
}
card=new MemoryCard{Capacity=10};adapter=new CardSaveStorage(card,banner,icon,4);repository=new SaveRepository(adapter);
using(var draft=NewVillager()){
    bool failed=false;try{draft.Commit(repository,false,new string[0]);}catch(IOException){failed=true;}
    Check(failed&&draft.State.Sequence==0&&draft.State.Name=="Emi"&&draft.State.HousePlaced,"out-of-space leaves the onboarding draft intact");
    card.Capacity=40;var retryNames=adapter.Names();draft.Commit(repository,retryNames.Any(SplitSaveCodec.IsFile),retryNames);
    Check(repository.Load()!.Name=="Emi"&&card.Peak<=40,"retry publishes only this draft's interrupted first save after freeing space");
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
var migrating=new CardArchive(card,banner,icon,4);
Check(card.Files.Count==oldCount,"opening an old layout does not migrate or save");
card.FailDelete=1;bool interrupted=false;
try{migrating.Write("options",new byte[]{9});}catch(IOException){interrupted=true;}
Check(interrupted&&card.Files.ContainsKey("AnimalTown"),"interrupted cleanup keeps the verified destination");
Check(new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()!.Name=="Emi","published migration is loadable before cleanup finishes");
var recovered=new CardArchive(card,banner,icon,4);recovered.Write("options",new byte[]{9});
Check(card.Files.Count==2&&card.Files.ContainsKey("Unrelated"),"cleanup resumes and preserves unrelated files");
Check(recovered.Read("dev_config").SequenceEqual(new byte[]{6,7})&&recovered.Read("options")[0]==9,"debug and options remain independent records");
TownDataDeletion.Erase(new ArchiveFiles(recovered));
Check(new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()==null,"logical town deletion removes every fallback bank");
Check(recovered.Read("options")[0]==9&&recovered.Read("dev_config")[0]==6,"town deletion retains preferences and debug data");
// Exhausted physical space must preserve every old directory item.
card=new MemoryCard{Capacity=80};using(var draft=NewVillager())draft.Commit(new SaveRepository(new OldFiles(card)),false,new string[0]);
oldCount=card.Files.Count;card.Capacity=card.Used;bool noSpace=false;
try{new CardArchive(card,banner,icon,4).Write("options",new byte[]{1});}catch(IOException){noSpace=true;}
Check(noSpace&&card.Files.Count==oldCount&&new SaveRepository(new OldFiles(card)).Load()!.Name=="Emi","migration without workspace leaves all source files intact");
card=new MemoryCard{Capacity=80};using(var draft=NewVillager())draft.Commit(new SaveRepository(new OldFiles(card)),false,new string[0]);
oldCount=card.Files.Count;card.FailBlockWrite=2;bool copyCut=false;
try{new CardArchive(card,banner,icon,4).Write("options",new byte[]{3});}catch(IOException){copyCut=true;}
Check(copyCut&&card.Files.Count==oldCount+1&&new SaveRepository(new OldFiles(card)).Load()!.Name=="Emi","a cut before migration publication retains every old file");
var resumed=new CardArchive(card,banner,icon,4);resumed.Write("options",new byte[]{3});
Check(card.Files.Count==1&&new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()!.Name=="Emi","partial copy resumes and consolidates only after verification");
card=new MemoryCard();var preferences=new CardArchive(card,banner,icon,4);preferences.Write("options",new byte[]{5});preferences.Write("dev_config",new byte[]{7});
using(var draft=NewVillager())draft.Commit(new SaveRepository(new CardSaveStorage(card,banner,icon,4)),false,new string[0]);
var all=new CardArchive(card,banner,icon,4);
Check(card.Files.Count==2&&all.Read("options")[0]==5&&all.Read("dev_config")[0]==7,"a preferences-first card adds a separate villager without losing settings");
// Existing 16-block archives grow by verified replacement, with the source
// intact until the new archive's publication marker is durable.
MemoryCard OldArchive()
{
    var result=new MemoryCard();
    if(result.CreateRecords("AnimalTown",16,1234,new byte[16384])!=0)throw new Exception("Old container fixture");
    var records=new CardRecordStore(new NativeCardBlocks(4,false,banner,icon),true);
    using(var draft=NewVillager())draft.Commit(new SaveRepository(new RecordFiles(records)),false,new string[0]);
    records.Write("options",new byte[]{5});records.Write("dev_config",new byte[]{7});records.Write("AT-Archive",new byte[]{2});
    return result;
}
card=OldArchive();var oldArchive=new CardArchive(card,banner,icon,4);
Check(card.Used==18&&card.Files.Count==1&&new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()!.Name=="Emi","reading a small archive leaves it untouched");
oldArchive.Write("options",new byte[]{8});
Check(card.Files.Count==1&&card.Files.ContainsKey("AnimalTown2")&&card.Used==34&&card.Peak==52,"expansion publishes a replacement before removing the old browser item");
Check(oldArchive.Read("options")[0]==8&&oldArchive.Read("dev_config")[0]==7&&new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()!.Name=="Emi","expansion preserves the complete town and independent preferences");
card=OldArchive();card.FailBlockWrite=3;bool expandCut=false;
try{new CardArchive(card,banner,icon,4).Write("options",new byte[]{8});}catch(IOException){expandCut=true;}
Check(expandCut&&card.Files.ContainsKey("AnimalTown")&&new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()!.Name=="Emi","a torn expansion copy retains the old authoritative save");
new CardArchive(card,banner,icon,4).Write("options",new byte[]{8});
Check(card.Files.Count==1&&card.Files.ContainsKey("AnimalTown2")&&new CardArchive(card,banner,icon,4).Read("options")[0]==8,"an interrupted expansion resumes safely");
card=OldArchive();card.FailDelete=0;bool cleanupCut=false;
try{new CardArchive(card,banner,icon,4).Write("options",new byte[]{8});}catch(IOException){cleanupCut=true;}
Check(cleanupCut&&card.Files.Count==2&&new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()!.Name=="Emi","published expanded archive loads while source cleanup is interrupted");
new CardArchive(card,banner,icon,4).Write("options",new byte[]{8});
Check(card.Files.Count==1&&card.Used==34,"expanded cleanup resumes after reopening");
card=OldArchive();card.Capacity=51;bool growthFull=false;
try{new CardArchive(card,banner,icon,4).Write("options",new byte[]{8});}catch(IOException){growthFull=true;}
Check(growthFull&&card.Files.Count==1&&new CardArchive(card,banner,icon,4).Read("options")[0]==5,"insufficient migration workspace retains the complete original");
card.Capacity=52;new CardArchive(card,banner,icon,4).Write("options",new byte[]{8});
Check(card.Files.Count==1&&card.Used==34,"expansion retries after freeing exactly the required workspace");
// A no-op explicit save migrates existing archived profiles, including
// inactive slots. Interrupt cleanup after the external file has been written.
card=OldArchive();
new CardArchive(card,banner,icon,4).Write("options",new byte[]{8});
var legacyRecords=new CardRecordStore(new NativeCardBlocks(4,false,banner,icon,"AnimalTown2"),true);
var legacyRepo=new SaveRepository(new RecordFiles(legacyRecords));
var existing=legacyRepo.Load()!;
for(int slot=1;slot<GameState.ProfileCount;slot++){
    existing.SelectProfile(slot);existing.Name="Resident "+(slot+1);existing.Wallet=100+slot;
}
legacyRepo.Save(existing);
var upgrade=new CardSaveStorage(card,banner,icon,4);
var upgradeRepo=new SaveRepository(upgrade);
existing=upgradeRepo.Load()!;
Check(card.Files.Count==1&&existing.ProfileName(3)=="Resident 4","reading all archived villagers causes no writes");
upgradeRepo.Save(existing);
card.Capacity=38;
bool splitFull=false;
try{upgrade.UpdateVillagerFiles(existing);}catch(IOException){splitFull=true;}
Check(splitFull&&card.Files.Count==1&&upgradeRepo.Load()!.ProfileName(3)=="Resident 4","a full card keeps archived profiles readable");
card.Capacity=80;
card.FailBlockWrite=0;
bool splitCut=false;
try{upgrade.UpdateVillagerFiles(existing);}catch(IOException){splitCut=true;}
Check(splitCut&&card.Files.ContainsKey("AnimalTown-Villager1")&&new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()!.ProfileName(3)=="Resident 4","interrupted separation retains a loadable town");
upgrade=new CardSaveStorage(card,banner,icon,4);
upgrade.UpdateVillagerFiles(existing);
Check(card.Files.Count==5&&Enumerable.Range(0,4).All(i=>card.Files.ContainsKey(VillagerCardFiles.FileName(i))),"each occupied villager has exactly one physical entry");
var sharedRecords=new CardRecordStore(new NativeCardBlocks(4,false,banner,icon,"AnimalTown2"),false);
Check(!sharedRecords.Names().Any(n=>VillagerCardFiles.SlotForBank(n)>=0),"occupied villager banks have moved out of the shared archive");
var other=(byte[])card.Files["AnimalTown-Villager1"].Clone();
upgradeRepo=new SaveRepository(upgrade);
existing=upgradeRepo.Load()!;
existing.Wallet++;
upgradeRepo.Save(existing);
Check(card.Files["AnimalTown-Villager1"].SequenceEqual(other),"saving an active villager leaves another villager file byte-identical");
card.Capacity=card.Used;
existing=upgradeRepo.DeleteProfile(existing,3);
card.FailDelete=0;
bool releaseCut=false;
try{upgrade.UpdateVillagerFiles(existing);}catch(IOException){releaseCut=true;}
upgrade=new CardSaveStorage(card,banner,icon,4);
Check(releaseCut&&card.Files.ContainsKey("AnimalTown-Villager4")&&!new SaveRepository(upgrade).Load()!.Profiles[3].Created,"an interrupted release cannot resurrect a committed deletion");
upgrade.UpdateVillagerFiles(existing);
Check(!card.Files.ContainsKey("AnimalTown-Villager4")&&!new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()!.Profiles[3].Created,"deletion frees only the removed villager's entry on a full card and survives reload");
TownDataDeletion.Erase(new ArchiveFiles(new CardArchive(card,banner,icon,4)));
Check(card.Files.Count==1&&new SaveRepository(new CardSaveStorage(card,banner,icon,4)).Load()==null,"town deletion also removes the dedicated entries");
// Existing settings remain readable until an explicit save can move them.
card=OldArchive();
var expectedVideo=new UserOptions{Resolution=2,Widescreen=true,ShowDebugMenus=true};
var expectedDebug=new DevConfig();expectedDebug.Values[DevConfig.FullResolutionAaIndex]=1;
new CardArchive(card,banner,icon,4).Write(UserOptionsStorage.FileName,expectedVideo.Encode());
new CardArchive(card,banner,icon,4).Write(DevConfigStorage.FileName,expectedDebug.Encode());
SaveCards.Initialize();
Check(UserOptionsStorage.Load().Resolution==2&&DevConfigStorage.Load().Values[DevConfig.FullResolutionAaIndex]==1&&card.Files.Count==1,"loading archived settings is read-only");
card.Capacity=card.Used+1;
new CardSaveStorage(card,banner,icon,4).MigrateSettings();
Check(card.Files.Count==1&&UserOptionsStorage.Load().Widescreen,"insufficient journal space preserves archived settings");
card.Capacity=80;
card.FailBlockWrite=0;
new CardSaveStorage(card,banner,icon,4).MigrateSettings();
Check(card.Files.ContainsKey(CardSettingsStorage.VideoFile)&&UserOptionsStorage.Load().ShowDebugMenus,"video copy survives interrupted archive cleanup");
new CardSaveStorage(card,banner,icon,4).MigrateSettings();
var settingsArchive=new CardRecordStore(new NativeCardBlocks(4,false,banner,icon,"AnimalTown2"),false);
Check(!settingsArchive.Names().Contains(UserOptionsStorage.FileName)&&!settingsArchive.Names().Contains(DevConfigStorage.FileName),"the verified settings files supersede both archived copies");
Check(card.Files.Count==3&&card.Used==36&&DevConfigStorage.Load().Values[DevConfig.FullResolutionAaIndex]==1,"both migrated files use one physical block each");
// A loose legacy setting migrates on its own save, without creating a town.
card=new MemoryCard();
card.WriteSave(UserOptionsStorage.FileName,expectedVideo.Encode(),new MemoryCardSaveOptions());
SaveCards.Initialize();
Check(UserOptionsStorage.Load().Widescreen&&card.Files.ContainsKey(UserOptionsStorage.FileName),"loose video settings still load");
Check(UserOptionsStorage.Save(expectedVideo)&&card.Files.Count==1&&card.Files.ContainsKey(CardSettingsStorage.VideoFile),"explicit settings save replaces the loose legacy entry");
Check(card.CreateRecords("AnimalTown",32,1234,new byte[16384])==0,"damaged archive fixture creates an independent town entry");
card.RecordPages["AnimalTown"][0][0]=255;
Check(UserOptionsStorage.Load().Widescreen&&DevConfigStorage.Save(expectedDebug)&&DevConfigStorage.Load().Values[DevConfig.FullResolutionAaIndex]==1,"dedicated settings can load and save with a damaged town archive");
CardSelectionChecks.Run(Check);
// Retire only the older record-store checkpoint, after publishing a second
// verified copy of the unchanged current directory. No larger backing is needed.
card=new MemoryCard();var compactRecords=new TownCardRecords(new NativeCardBlocks(4,true,banner,icon,"AnimalTown",6),true);
var oldBody=Enumerable.Repeat((byte)2,8193).ToArray();var newBody=Enumerable.Repeat((byte)3,8193).ToArray();
compactRecords.Write("large",oldBody);compactRecords.Write("large",oldBody);var compactBase=card.Copy();int compactWrites=card.Writes;
MemoryCard.Slots[4]=card;MemoryCard.Current=card;
compactRecords.Write("large",newBody);int retireWrites=card.Writes-compactWrites;
Check(retireWrites==4&&compactRecords.Read("large").SequenceEqual(newBody)&&card.Files.Count==1,"obsolete-checkpoint pages are reused only after verified retirement, without growing the backing");
for(int cut=0;cut<retireWrites;cut++){
    card=compactBase.Copy();card.FailBlockWrite=cut;bool stopped=false;
    try{new TownCardRecords(new NativeCardBlocks(4,false,banner,icon),true).Write("large",newBody);}catch(IOException){stopped=true;}
    Check(stopped,"checkpoint retirement interruption reaches every sector write");card.FailBlockWrite=-1;
    var reopened=new TownCardRecords(new NativeCardBlocks(4,false,banner,icon),true);var retiredBody=reopened.Read("large");
    Check(retiredBody.SequenceEqual(oldBody)||retiredBody.SequenceEqual(newBody),"every retirement interruption retains one complete old or new payload");
    reopened.Write("large",newBody);Check(reopened.Read("large").SequenceEqual(newBody),"retirement resumes idempotently after every interrupted sector");
}
card=new MemoryCard();var fragmented=new TownCardRecords(new NativeCardBlocks(4,true,banner,icon,"AnimalTown",10),true);
fragmented.Write("gap",oldBody);fragmented.Write("a",oldBody);fragmented.Write("b",oldBody);fragmented.Write("b",oldBody);fragmented.Delete("gap");
var fragmentedBase=card.Copy();MemoryCard.Slots[4]=card;MemoryCard.Current=card;
var threePages=Enumerable.Repeat((byte)4,16385).ToArray();int relocationBefore=card.Writes;fragmented.Write("c",threePages);int relocationWrites=card.Writes-relocationBefore;
Check(relocationWrites==9&&fragmented.Read("a").SequenceEqual(oldBody)&&fragmented.Read("b").SequenceEqual(oldBody)&&fragmented.Read("c").SequenceEqual(threePages),"streamed relocation joins fragmented free space without retaining a second large body");
for(int cut=0;cut<relocationWrites;cut++){
    card=fragmentedBase.Copy();card.FailBlockWrite=cut;bool stopped=false;
    try{new TownCardRecords(new NativeCardBlocks(4,false,banner,icon),true).Write("c",threePages);}catch(IOException){stopped=true;}
    Check(stopped,"streamed relocation interruption reaches every data and root publication sector");card.FailBlockWrite=-1;
    var reopened=new TownCardRecords(new NativeCardBlocks(4,false,banner,icon),true);
    Check(reopened.Read("a").SequenceEqual(oldBody)&&reopened.Read("b").SequenceEqual(oldBody),"both complete source records survive every interrupted relocation");
    bool published=reopened.Names().Contains("c");if(!published)reopened.Write("c",threePages);
    Check(reopened.Read("c").SequenceEqual(threePages)&&card.Files.Count==1,"relocation retries within its original backing after every interrupted sector");
}
// Text mail grows the same browser item beyond the old 256-block ceiling.
card=new MemoryCard{Capacity=2043};var mailArchive=new CardArchive(card,banner,icon,4);mailArchive.Write("options",new byte[]{7});
var longMail=new byte[2200000];for(int i=0;i<longMail.Length;i++)longMail[i]=(byte)(i*13);
mailArchive.Write("AT-Shops0",longMail);
Check(card.Files.Count==1&&card.Used>258&&new CardArchive(card,banner,icon,4).Read("AT-Shops0").SequenceEqual(longMail),"large mail archives grow beyond 256 blocks while retaining one browser item");
Check(new CardArchive(card,banner,icon,4).Read("options")[0]==7,"mail growth preserves preferences");
// A maximum real legacy mailbox must consolidate without a 32-block
// intermediate or two full encoded copies during readback.
var largeState=new GameState(7461){Tutorial=7};
for(int p=1;p<4;p++){largeState.SelectProfile(p);largeState.Tutorial=7;}largeState.SelectProfile(0);
long mailNow=AnimalTown.Jobs.JobCalendar.At(2027,7,15,12);largeState.ResidentJobs.Enable(0x41544d41494cUL,mailNow);
string fullBody=string.Concat(Enumerable.Repeat("\U0001f642",1000));
for(int p=0;p<4;p++)for(int batch=0;batch<2;batch++){
    for(int n=0;n<64;n++){largeState.Receive(Catalog.WritingPaper);Check(largeState.ResidentJobs.SendLetter(largeState.Town.Jobs.Player(p),fullBody,largeState.Find(Catalog.WritingPaper),-1,largeState.ResidentJobs.NextCommandSequence).Result==AnimalTown.Jobs.JobResult.Ok,"maximum legacy letter send");}
    largeState.ResidentJobs.AdvanceTo(mailNow+=86400);
}
card=new MemoryCard{Capacity=2043};new SaveRepository(new OldFiles(card)).Save(largeState);
var legacyMaximum=card.Copy();MemoryCard.Slots[4]=card;MemoryCard.Current=card;int legacyWrites=card.Writes;
var largeMigration=new CardArchive(card,banner,icon,4,new GameState(1));largeMigration.ReuseTextFrom(largeState);
int conversionCollections=0;var previousReclaim=AnimalTown.Jobs.JobMemory.ReclaimTemporaryText;
try{AnimalTown.Jobs.JobMemory.ReclaimTemporaryText=()=>conversionCollections++;largeMigration.Write("options",new byte[]{3});}
finally{AnimalTown.Jobs.JobMemory.ReclaimTemporaryText=previousReclaim;}
Check(conversionCollections<64,"conversion reuses its own validated town bodies instead of retaining another 512-letter mailbox");
Check(card.Files.Count==1&&card.Used>258,"maximum legacy town chooses adequate initial backing space");
int migrationWrites=card.Writes-legacyWrites;
void VerifyMaximum(MemoryCard source){
    MemoryCard.Slots[4]=source;MemoryCard.Current=source;
    var loaded=new SaveRepository(new CardSaveStorage(source,banner,icon,4,largeState)).Load(largeState)!;
    Check(loaded!=null&&loaded.MailCount==128,"maximum migrated mailbox remains loadable");
    for(int p=0;p<4;p++){loaded!.SelectProfile(p);loaded.ResidentJobs.Enable(loaded.Town.Jobs.TownId,mailNow);Check(loaded.MailCount==128&&loaded.ResidentJobs.ReadLetter(127)==fullBody,"all four full Unicode mailboxes survive legacy conversion");}
}
VerifyMaximum(card);
card=legacyMaximum.Copy();var interruptedCreation=new NativeCardBlocks(4,true,banner,icon,"AnimalTown",1024);
for(int page=600;page<1024;page++)Array.Fill(card.RecordPages["AnimalTown"][page],(byte)255);
int creationWrites=card.Writes;
VerifyMaximum(card);
Check(card.Writes==creationWrites&&card.Files.Count==legacyMaximum.Files.Count+1,"opening a target interrupted during zero filling reads the intact legacy town without writing");
new CardArchive(card,banner,icon,4,largeState).Write("options",new byte[]{3});VerifyMaximum(card);
Check(card.Files.Count==1,"initial creation interrupted before either root is replaced only after the complete legacy town is validated");
card=legacyMaximum.Copy();_ =new NativeCardBlocks(4,true,banner,icon,"AnimalTown",1024);
foreach(string name in card.Files.Keys.Where(n=>n.StartsWith("AT-Commit")).ToArray())card.Delete(name);
int incompleteFiles=card.Files.Count;bool refusedIncomplete=false;
try{new CardArchive(card,banner,icon,4,largeState).Write("options",new byte[]{3});}catch(IOException){refusedIncomplete=true;}
Check(refusedIncomplete&&card.Files.Count==incompleteFiles&&card.Files.ContainsKey("AnimalTown"),"an unverified archive is never replaced when the remaining legacy town is incomplete");
card=legacyMaximum.Copy();_ =new TownCardRecords(new NativeCardBlocks(4,true,banner,icon),true);
new CardArchive(card,banner,icon,4,largeState).Write("options",new byte[]{3});VerifyMaximum(card);
Check(card.Files.Count==1&&card.Used>258,"an unpublished undersized legacy copy is replaced while all original records remain authoritative");
foreach(int cut in new[]{0,migrationWrites/2,migrationWrites-1}){
    card=legacyMaximum.Copy();card.FailBlockWrite=cut;bool stopped=false;
    try{new CardArchive(card,banner,icon,4,largeState).Write("options",new byte[]{3});}catch(IOException){stopped=true;}
    Check(stopped,"maximum legacy conversion interruption reached a physical write");
    VerifyMaximum(card);card.FailBlockWrite=-1;new CardArchive(card,banner,icon,4,largeState).Write("options",new byte[]{3});VerifyMaximum(card);
    Check(card.Files.Count==1,"maximum legacy conversion retries into one complete browser item");
}
Console.WriteLine("Maximum legacy mail: "+migrationWrites+" block writes; initial backing, streaming readback and interrupted retries passed.");
// Metadata intent uses checkpoint space, so growth also works with no free
// data page in the existing backing file.
card=new MemoryCard{Capacity=2043};var packed=new TownCardRecords(new NativeCardBlocks(4,true,banner,icon),true);packed.SetArchiveMarker(2,0,0);
packed.Write("full",new byte[28*8192]);packed.Write("tail",new byte[2*8192]);
Check(packed.AvailableBlocks==0,"growth fixture fills every data page");
new CardArchive(card,banner,icon,4).Write("options",new byte[]{9});
Check(new CardArchive(card,banner,icon,4).Read("full").Length==28*8192,"a completely full container can publish growth without a free data page");
var growthBase=card.Copy();var baselineArchive=new CardArchive(growthBase,banner,icon,4);baselineArchive.Write("larger",new byte[50*8192]);
int growthWrites=growthBase.Writes-card.Writes;
for(int cut=0;cut<growthWrites;cut++){
    var trial=card.Copy();trial.FailBlockWrite=cut;try{new CardArchive(trial,banner,icon,4).Write("larger",new byte[50*8192]);}catch(IOException){}
    trial.FailBlockWrite=-1;var recoveredGrowth=new CardArchive(trial,banner,icon,4);
    Check(recoveredGrowth.Read("full").Length==28*8192,"every torn dynamic-growth block retains the original complete record");
    recoveredGrowth.Write("larger",new byte[50*8192]);Check(recoveredGrowth.Read("larger").Length==50*8192&&trial.Files.Count==1,"dynamic growth recovers every physical write without duplicate backing items");
}
Console.WriteLine("Dynamic growth: "+growthWrites+" physical block writes, each torn and retried.");
// Streaming readback checks full bytes and the record-store checksum without
// retaining a second megabyte-sized array.
card=growthBase;MemoryCard.Slots[4]=card;var streamRecords=new TownCardRecords(new NativeCardBlocks(4,false,banner,icon,card.RecordPages.Single().Key,card.RecordPages.Single().Value.Length),true);var large=streamRecords.Read("larger");
Check(streamRecords.Matches("larger",large,-1,large.Length,0,false),"streamed readback accepts the complete saved record");large[100]=1;
Check(!streamRecords.Matches("larger",large,-1,large.Length,0,false),"streamed readback detects a payload mismatch beyond its header");
Console.WriteLine(checks+" card adapter checks passed: consolidated save/reload, no-op save, capacity, migration interruption/retry, independent records and deletion; host model, not hardware.");
sealed class PrefixMeter(CardSaveStorage inner) : ISaveStorage,ISaveSnapshotStorage,ISaveVerifiedPrefixStorage
{
    internal int PrefixReads,EconomicReads;
    public byte[] Read(string name){if(name.StartsWith("AT-Shops"))EconomicReads++;return inner.Read(name);}
    public void Write(string name,byte[] bytes)=>inner.Write(name,bytes);
    public byte[] ReadVerifiedPrefix(string name,int count,int sequence,int length,uint checksum){PrefixReads++;return inner.ReadVerifiedPrefix(name,count,sequence,length,checksum);}
    public bool Matches(string name,byte[]? expected,int sequence,int length,uint checksum,bool ignoreSequence)=>inner.Matches(name,expected,sequence,length,checksum,ignoreSequence);
}

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
sealed class RecordFiles(CardRecordStore records) : ISaveStorage
{
    public byte[] Read(string name)=>records.Read(name);
    public void Write(string name,byte[] bytes)=>records.Write(name,bytes);
}
