using System.Buffers.Binary;
using System.Text.Json;
using AnimalTown;

internal static class ModelMemoryCooker
{
    internal const string DiscName="villager-memory.bin";
    private static void ValidateCaps(ModelMemoryPlan plan)
    {
        if(plan.SharedBytes>ModelMemoryPlan.MaximumSharedBytes||plan.InstanceBytes>ModelMemoryPlan.MaximumInstanceBytes)
            throw new InvalidDataException("Villager native allocation plan exceeds its model/instance loader cap");
    }
    private static byte[] Create(string root,out object[] records)
    {
        var bytes=new byte[16+ModelMemoryCatalog.Count*ModelMemoryCatalog.Stride];
        void Put(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at,4),value);
        Put(0,ModelMemoryCatalog.Magic);Put(4,ModelMemoryCatalog.Version);Put(8,ModelMemoryCatalog.Count);Put(12,ModelMemoryCatalog.Stride);
        records=new object[ModelMemoryCatalog.Count];
        for(int species=0;species<ModelMemoryCatalog.Count;species++){
            string name=ModelLodCooker.Species[species];byte[] package=File.ReadAllBytes(Path.Combine(root,"out/disc",name+"-tex128.dndmodel"));ModelMemoryPlan plan=ModelMemoryPlan.Estimate(package);ValidateCaps(plan);int at=16+species*ModelMemoryCatalog.Stride;
            Put(at,plan.PackageBytes);Put(at+4,unchecked((int)plan.PackageHash));Put(at+8,plan.SharedBytes);Put(at+12,plan.InstanceBytes);Put(at+16,plan.OneInstancePeak);Put(at+20,plan.TwoInstancePeak);
            records[species]=new{species=name,packageBytes=plan.PackageBytes,packageFnv32=plan.PackageHash,sharedNativeUpperBytes=plan.SharedBytes,instanceNativeUpperBytes=plan.InstanceBytes,startupOneInstancePeakBytes=plan.OneInstancePeak,startupTwoInstancePeakBytes=plan.TwoInstancePeak};
        }
        var catalog=new ModelMemoryCatalog(bytes);for(int species=0;species<ModelMemoryCatalog.Count;species++)catalog.ValidatePackage(species,BinaryPrimitives.ReadInt32LittleEndian(bytes.AsSpan(16+species*ModelMemoryCatalog.Stride,4)));
        return bytes;
    }
    internal static void Cook(string root)
    {
        byte[] bytes=Create(root,out object[] records);File.WriteAllBytes(Path.Combine(root,"out/disc",DiscName),bytes);
        File.WriteAllText(Path.Combine(root,"out/model-memory-audit.json"),JsonSerializer.Serialize(new{schema=1,loadGuardBytes=ModelMemoryPlan.LoadGuardBytes,model=records},new JsonSerializerOptions{WriteIndented=true})+"\n");
        Console.WriteLine("Villager memory admission: six package-bound native allocation plans, "+bytes.Length+" runtime bytes");
        File.WriteAllBytes(Path.Combine(root,"out/disc/villager-appearance-memory.bin"),Appearances(root));
    }
    internal static void Validate(string root)
    {
        byte[] expected=Create(root,out _),actual=File.ReadAllBytes(Path.Combine(root,"out/disc",DiscName));
        if(!expected.SequenceEqual(actual))throw new InvalidDataException("Villager memory plan differs from the actual packaged 128px models");
        if(!Appearances(root).SequenceEqual(File.ReadAllBytes(Path.Combine(root,"out/disc/villager-appearance-memory.bin"))))throw new InvalidDataException("Appearance memory plan differs from packaged models");
    }
    private static byte[] Appearances(string root)
    {
        var bytes=new byte[8+VillagerAppearance.Count*8];
        BinaryPrimitives.WriteInt32LittleEndian(bytes,0x41564d31);
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4),VillagerAppearance.Count);
        for(int id=0;id<VillagerAppearance.Count;id++){
            var plan=ModelMemoryPlan.Estimate(File.ReadAllBytes(Path.Combine(root,"out/disc",VillagerAppearance.Models[id]+"-tex128.dndmodel")));
            ValidateCaps(plan);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(8+id*8),plan.PackageBytes);
            BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(12+id*8),plan.OneInstancePeak);
        }
        return bytes;
    }
    internal static void Check()
    {
        byte[] source=ModelLodChecks.Grid();ModelMemoryPlan plan=ModelMemoryPlan.Estimate(source);
        if(plan.PackageBytes!=source.Length||plan.PackageHash!=ModelMemoryPlan.Hash(source)||plan.SharedBytes<source.Length||plan.InstanceBytes<512||plan.TwoInstancePeak<=plan.OneInstancePeak)throw new InvalidDataException("Invalid model memory estimate");
        var bytes=new byte[16+ModelMemoryCatalog.Count*ModelMemoryCatalog.Stride];
        void Put(int at,int value)=>BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(at,4),value);
        Put(0,ModelMemoryCatalog.Magic);Put(4,ModelMemoryCatalog.Version);Put(8,ModelMemoryCatalog.Count);Put(12,ModelMemoryCatalog.Stride);
        for(int i=0;i<ModelMemoryCatalog.Count;i++){int at=16+i*ModelMemoryCatalog.Stride;Put(at,plan.PackageBytes);Put(at+4,unchecked((int)plan.PackageHash));Put(at+8,plan.SharedBytes);Put(at+12,plan.InstanceBytes);Put(at+16,plan.OneInstancePeak);Put(at+20,plan.TwoInstancePeak);}
        var catalog=new ModelMemoryCatalog(bytes);if(catalog.StartupPeak(0,false)!=plan.OneInstancePeak||catalog.StartupPeak(0,true)!=plan.TwoInstancePeak)throw new InvalidDataException("Model memory catalog roundtrip failed");
        bool rejected=false;try{catalog.ValidatePackage(0,source.Length+32);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new InvalidDataException("Mismatched model package accepted");
        Put(16+16,plan.OneInstancePeak-1);rejected=false;try{_ = new ModelMemoryCatalog(bytes);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new InvalidDataException("Understated model allocation plan accepted");
        source[170]^=1;rejected=false;try{_ = ModelMemoryPlan.Estimate(source);}catch(InvalidOperationException){rejected=true;}if(!rejected)throw new InvalidDataException("Corrupt model memory source accepted");
        foreach(int vertices in new[]{26000,40000}){
            byte[] fixture=ModelLodChecks.Grid(splitSkin:true);var sections=new byte[6][];
            for(int i=0;i<6;i++){int directory=16+i*16,start=BinaryPrimitives.ReadInt32BigEndian(fixture.AsSpan(directory+4,4)),size=BinaryPrimitives.ReadInt32BigEndian(fixture.AsSpan(directory+8,4));sections[i]=fixture.AsSpan(start,size).ToArray();}
            byte[] original=sections[1];int oldVertices=BinaryPrimitives.ReadInt32BigEndian(original.AsSpan(16,4)),indices=BinaryPrimitives.ReadInt32BigEndian(original.AsSpan(20,4));var expanded=new byte[48+vertices*80+indices*4];original.AsSpan(0,48).CopyTo(expanded);BinaryPrimitives.WriteInt32BigEndian(expanded.AsSpan(16,4),vertices);
            for(int i=0;i<vertices;i++)original.AsSpan(48+(i%oldVertices)*80,80).CopyTo(expanded.AsSpan(48+i*80));original.AsSpan(48+oldVertices*80).CopyTo(expanded.AsSpan(48+vertices*80));sections[1]=expanded;
            rejected=false;try{ValidateCaps(ModelMemoryPlan.Estimate(ModelLodCooker.Package(sections,2)));}catch(InvalidDataException){rejected=true;}if(!rejected)throw new InvalidDataException("A model/instance loader cap could be exceeded by an emitted memory plan");
        }
        Console.WriteLine("Model memory cooking checks passed: bounded plans, schema roundtrip, tampering and mismatched packages");
    }
}
