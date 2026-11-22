using AnimalTown;
using AnimalTown.Jobs;

internal static class VillagerJobIdentityChecks
{
    private static bool Fails(Action action){try{action();return false;}catch(InvalidOperationException){return true;}catch(ArgumentException){return true;}}
    internal static void Run(Action<bool,string> check)
    {
        var s=new GameState(17);uint offers=s.Town.JobState.Offers.State;
        s.EnsureJobIdentities(0xf0123456789abcdeUL);var ids=new List<JobIdentity>();
        for(int i=0;i<6;i++){var id=s.Town.Jobs.Npc(i);check(id.IsValid&&!ids.Contains(id),"NPC identity is nonempty and unique");ids.Add(id);}
        for(int i=0;i<4;i++){s.SelectProfile(i);s.EnsureJobIdentities(s.Town.Jobs.TownId);var id=s.Town.Jobs.Player(i);check(id.IsValid&&!ids.Contains(id),"each player profile gets a unique identity");ids.Add(id);}
        check(s.Town.JobState.Offers.State==offers,"identity migration never consumes job RNG");
        var other=new GameState(17);other.EnsureJobIdentities(123);check(other.Town.Jobs.Npc(0)!=s.Town.Jobs.Npc(0),"identical terrain seeds do not identify the same town or villagers");
        var raw=SaveCodec.Encode(s);check(raw[4]==16,"identity-bearing combined saves use schema 16 alongside independent snow and koban extensions");
        var copy=SaveCodec.Decode(raw);check(raw.SequenceEqual(SaveCodec.Encode(copy)),"all 64-bit identity bits and serial allocation state round-trip");
        var split=SplitSaveCodec.Encode(s);check(raw.SequenceEqual(SaveCodec.Encode(SplitSaveCodec.Decode(split))),"economic split record preserves identities through semantic readback");
        var commit=SplitSaveCodec.EncodeCommit(s,split);check(SplitSaveCodec.DecodeCommitted(commit,split).Town.Jobs.Player(3)==s.Town.Jobs.Player(3),"commit includes the identity extension");
        copy.Town.Residents[0].Name="Another name";copy.Town.Residents[0].HomeX++;
        check(copy.Town.Jobs.Npc(0)==s.Town.Jobs.Npc(0),"names and home coordinates are not identities");
        var deleted=copy.Town.Jobs.Player(2);copy.DeleteProfile(2);check(copy.Town.Jobs.Player(2).IsEmpty,"deletion clears old profile ownership");
        copy.SelectProfile(2);copy.EnsureJobIdentities(copy.Town.Jobs.TownId);
        check(copy.Town.Jobs.Player(2)!=deleted&&copy.Town.Jobs.Player(2).Serial>deleted.Serial,"reusing a player slot allocates a new never-reused identity");
        check(Fails(()=>copy.EnsureJobIdentities(0))&&Fails(()=>copy.EnsureJobIdentities(99)),"an established town identity cannot be reset");
        check(Fails(()=>SaveCodec.EncodeLegacy(s))&&Fails(()=>SplitSaveCodec.EncodeLegacy(s)),"legacy export cannot silently discard durable identities");
        var frozen=SaveCodec.Decode(SaveCodec.Encode(s));for(int i=0;i<4;i++)check(frozen.Town.Jobs.Player(i)==s.Town.Jobs.Player(i),"profile switching does not transfer another player's identity");
        Console.WriteLine("Offline villager identities: nonce namespaces, no RNG draws, profile reuse, complete save formats and migration passed");
    }
}
