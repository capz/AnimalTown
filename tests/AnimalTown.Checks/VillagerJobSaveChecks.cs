using AnimalTown;
using AnimalTown.Jobs;

internal static class VillagerJobSaveChecks
{
    private static void Put(byte[] data,int offset,int value){for(int i=0;i<4;i++)data[offset+i]=(byte)((uint)value>>(8*i));}
    private static bool Fails(Action operation){try{operation();return false;}catch(InvalidOperationException){return true;}catch(ArgumentException){return true;}}
    internal static void Run(Action<bool,string> check)
    {
        VillagerJobIdentityChecks.Run(check);
        SnowCompatibility(check);
        var state=new GameState(-99);var old=SaveCodec.Encode(state);
        check(old[4]==11&&SaveCodec.Decode(old).Town.JobState.IsPristine,"old combined saves migrate to fresh job state without changing RNG");
        var oldParts=SplitSaveCodec.Encode(state);
        check(oldParts[0][4]==7&&SplitSaveCodec.Decode(oldParts).Town.JobState.IsPristine,"old shared town records migrate without consuming RNG");
        var zero=new GameState(unchecked((int)JobRulesV1.OfferSeedSalt));zero.Town.JobState.Rewards.NextInt(2);
        check(SaveCodec.Decode(SaveCodec.Encode(zero)).Town.JobState.Offers.State==0,"saved zero RNG state is never treated as an uninitialized seed");
        var jobs=state.Town.JobState;
        for(int i=0;i<6;i++){jobs.Resident(i).StartNoJobCooldown();jobs.Resident(i).ConversationCompleted();}
        jobs.ObserveOutdoorAcre(0,3);jobs.ObserveOutdoorAcre(1,9);jobs.Advance(347);
        jobs.Offers.NextInt(37);jobs.Rewards.NextInt(100);jobs.HouseCache.NextInt(13);
        var bytes=SaveCodec.Encode(state);check(bytes[4]==13,"job-bearing combined save uses explicit schema 13");
        var combined=SaveCodec.Decode(bytes);check(SaveCodec.Encode(combined).SequenceEqual(bytes),"all cooldown, patience, acre and RNG fields round-trip without quantization");
        var parts=SplitSaveCodec.Encode(state);check(parts[0][4]==9,"job-bearing town record uses explicit schema 9");
        check(SaveCodec.Encode(SplitSaveCodec.Decode(parts)).SequenceEqual(bytes),"job state survives the split codec's shared semantic validator");
        var loaded=SaveCodec.Decode(bytes);check(loaded.Town.JobState.Resident(0).RemainingTicks==3653,"loading alone cannot count offline time");
        for(int i=0;i<20;i++)check(loaded.Town.JobState.Offers.NextInt(997)==jobs.Offers.NextInt(997)&&loaded.Town.JobState.Rewards.NextInt(113)==jobs.Rewards.NextInt(113)&&loaded.Town.JobState.HouseCache.NextInt(71)==jobs.HouseCache.NextInt(71),"save/load resumes all three streams at the exact next draw");
        check(Fails(()=>SaveCodec.EncodeLegacy(state))&&Fails(()=>SplitSaveCodec.EncodeLegacy(state)),"old single-profile export explicitly rejects loss of durable job state");
        state.Town.DaylightLocation=1;check(SaveCodec.Decode(SaveCodec.Encode(state)).Town.DaylightLocation==1&&SplitSaveCodec.Decode(SplitSaveCodec.Encode(state)).Town.JobState.Resident(0).RemainingTicks==3653,"job extensions preserve modern daylight data");
        // Recompute the outer CRC to exercise semantic validation rather than
        // merely the checksum. The fixed-size tail includes its length word.
        foreach(var mutation in new[]{(0,39),(2,999),(3,7),(7,999),(11,6),(12,-1),(13,1001),(14,99),(15,2)}){
            var broken=(byte[])bytes.Clone();int offset=broken.Length-4*(TownJobState.EncodedWordCount+1)+mutation.Item1*4;
            Put(broken,offset,mutation.Item2);Put(broken,20,unchecked((int)SaveCodec.Crc(broken,24,broken.Length-24)));
            check(Fails(()=>SaveCodec.Decode(broken)),"invalid job length, rules, roster, acre, personality, timer, budget, patience or flag fails bounded decoding");
        }
        for(int cut=1;cut<=4*(TownJobState.EncodedWordCount+1);cut++){
            var truncated=bytes[..^cut];Put(truncated,16,truncated.Length);Put(truncated,20,unchecked((int)SaveCodec.Crc(truncated,24,truncated.Length-24)));
            check(Fails(()=>SaveCodec.Decode(truncated)),"every truncated job-state tail is rejected safely");
        }
        var profileState=new GameState(17);profileState.SelectProfile(1);profileState.Town.JobState.ObserveOutdoorAcre(1,8);profileState.DeleteProfile(1);
        check(profileState.Town.JobState.LastOutdoorAcre(1)==-1,"deleting a profile clears its old acre cursor");
        Console.WriteLine("Job-state legacy migration, bounded schemas, corruption, profile cleanup and exact RNG continuation checks passed");
    }
    private static void SnowCompatibility(Action<bool,string> check)
    {
        var snow=SaveCodec.Decode(File.ReadAllBytes("tests/fixtures/jobs/legacy-snow-v14-catalog1.bin"));
        check(snow.Tutorial==7&&snow.Town.Traffic.Snow.Winter&&snow.Town.Traffic.Snow.Packed.Any(b=>b!=0),"the actual catalog-1 schema-14 snowfall save retains dents and migrates completed employment");
        var jobs=SaveCodec.Decode(File.ReadAllBytes("tests/fixtures/jobs/legacy-jobs-v14-catalog2.bin"));
        var letter=jobs.Town.Jobs.Letter(0,0);
        check(jobs.Town.Jobs.TownId==23495&&jobs.MailCount==1&&JobTextBody.Unpack(letter.Body!,letter.BodyOffset,letter.BodyWords)=="Legacy café! 🙂","the actual catalog-2 schema-14 job save retains its namespace and Unicode letter");
        long now=JobCalendar.At(2027,1,15,12);snow.ResidentJobs.Enable(23496,now);
        var combined=SaveCodec.Encode(snow);var restored=SaveCodec.Decode(combined);
        check(combined[4]==15&&restored.Town.Jobs.TownId==23496&&restored.Town.Traffic.Snow.Packed.SequenceEqual(snow.Town.Traffic.Snow.Packed),"schema 15 retains the snow checkpoint and economic job ledger together");
        var parts=SplitSaveCodec.Encode(snow);restored=SplitSaveCodec.Decode(parts);
        check(parts[0][4]==10&&restored.Town.Jobs.TownId==23496&&restored.Town.Traffic.Snow.Packed.SequenceEqual(snow.Town.Traffic.Snow.Packed),"schema-10 split towns retain snowfall alongside jobs in the independent economy record");
    }
}
