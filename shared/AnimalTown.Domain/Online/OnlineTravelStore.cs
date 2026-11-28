using System.IO;

namespace AnimalTown.Online;

public enum OnlineTravelPhase { Ready = 0, Reserved = 1, Active = 2, Publishing = 3, Published = 4 }

// A reservation is persisted and read back before the server may debit the
// selected home profile. The destination world is never a save candidate.
public sealed class OnlineTravelRecord
{
    public readonly string Origin, Town, Account, Reservation, Fingerprint;
    public readonly long Revision;
    public readonly int Profile;
    public readonly OnlineTravelPhase Phase;
    public OnlineTravelRecord(string origin, string town, string account, string reservation,
        long revision, int profile, OnlineTravelPhase phase, string fingerprint = "")
    {
        if (!OnlineCardCredential.ValidOrigin(origin) || !OnlineCardCredential.Hex(town, 0, 16) || town == "0000000000000000" ||
            !OnlineCardCredential.Identifier(account) || !OnlineCardCredential.Identifier(reservation) ||
            revision < 1 || (uint)profile >= GameState.ProfileCount || phase < OnlineTravelPhase.Ready || phase > OnlineTravelPhase.Published ||
            fingerprint.Length != 0 && !OnlineCardCredential.Hex(fingerprint, 0, 64) ||
            phase >= OnlineTravelPhase.Publishing && fingerprint.Length != 64)
            throw new FormatException("Invalid online travel record.");
        Origin = origin; Town = town; Account = account; Reservation = reservation;
        Revision = revision; Profile = profile; Phase = phase; Fingerprint = fingerprint;
    }
}

public sealed class OnlineTravelStore
{
    public const string Bank0 = "AnimalTown-Online0", Bank1 = "AnimalTown-Online1";
    public const int RecordBytes = 512;
    const int Magic = 0x31564e4f;
    readonly ISaveStorage storage;
    public OnlineTravelStore(ISaveStorage storage) { this.storage = storage; }
    public sealed class Snapshot
    {
        internal readonly int Bank, Sequence;
        internal readonly uint Checksum;
        public readonly OnlineTravelRecord Record;
        internal Snapshot(int bank, int sequence, uint checksum, OnlineTravelRecord record)
        { Bank = bank; Sequence = sequence; Checksum = checksum; Record = record; }
    }
    public Snapshot? Load()
    {
        bool damaged = false;
        var first = ReadBank(0, ref damaged); var second = ReadBank(1, ref damaged);
        if (first == null && second == null)
        {
            if (damaged) throw new IOException("Online recovery records are unreadable. Keep this card for recovery.");
            return null;
        }
        if (first == null) return second; if (second == null) return first;
        if (first.Sequence == second.Sequence && first.Checksum != second.Checksum)
            throw new IOException("Online recovery records disagree.");
        return first.Sequence >= second.Sequence ? first : second;
    }
    public Snapshot Save(Snapshot? expected, OnlineTravelRecord record)=>SaveCore(expected,record,false);
    // Call only after explicit service selection, authentication to the same
    // account/town and verification of the immutable reservation fingerprint.
    // Rebinding can change no travel phase, identity, revision or contents.
    public Snapshot RebindOrigin(Snapshot expected,string origin)
    {
        var old=expected.Record;
        return SaveCore(expected,new OnlineTravelRecord(origin,old.Town,old.Account,old.Reservation,old.Revision,old.Profile,old.Phase,old.Fingerprint),true);
    }
    Snapshot SaveCore(Snapshot? expected,OnlineTravelRecord record,bool rebindOrigin)
    {
        var current = Load();
        if (current == null != (expected == null) || current != null && expected != null &&
            (current.Sequence != expected.Sequence || current.Checksum != expected.Checksum))
            throw new IOException("Online recovery record changed. Reload this card.");
        if (expected != null && (expected.Record.Town != record.Town || expected.Record.Account != record.Account ||
            expected.Record.Origin != record.Origin && expected.Record.Phase != OnlineTravelPhase.Ready&&!rebindOrigin))
            throw new IOException("Finish online recovery before changing this card's link.");
        if(expected!=null)
        {
            var before=expected.Record;
            bool ready=before.Phase==OnlineTravelPhase.Ready&&record.Phase==OnlineTravelPhase.Ready&&record.Revision>=before.Revision;
            bool reserve=before.Phase==OnlineTravelPhase.Ready&&record.Phase==OnlineTravelPhase.Reserved&&record.Revision==before.Revision;
            bool same=before.Reservation==record.Reservation&&before.Profile==record.Profile&&before.Origin==record.Origin;
            bool rebind=rebindOrigin&&before.Reservation==record.Reservation&&before.Profile==record.Profile&&before.Phase==record.Phase&&before.Revision==record.Revision&&before.Fingerprint==record.Fingerprint;
            bool advance=same&&(
                before.Phase==OnlineTravelPhase.Reserved&&record.Phase==OnlineTravelPhase.Active&&record.Revision==before.Revision&&record.Fingerprint==before.Fingerprint||
                before.Phase==OnlineTravelPhase.Active&&record.Phase==OnlineTravelPhase.Publishing&&record.Revision==before.Revision+1||
                before.Phase==OnlineTravelPhase.Reserved&&record.Phase==OnlineTravelPhase.Publishing&&record.Revision==before.Revision+1||
                before.Phase==OnlineTravelPhase.Publishing&&record.Phase==OnlineTravelPhase.Published&&record.Revision==before.Revision&&record.Fingerprint==before.Fingerprint||
                before.Phase==OnlineTravelPhase.Published&&record.Phase==OnlineTravelPhase.Ready&&record.Revision==before.Revision&&record.Fingerprint==before.Fingerprint);
            if(!ready&&!reserve&&!advance&&!rebind)throw new IOException("Invalid online recovery transition. Reload this card.");
        }
        int previous=current?.Sequence??0;if(previous==int.MaxValue)throw new IOException("Online recovery sequence is exhausted. Keep this card.");
        int bank = current == null ? 0 : current.Bank ^ 1, sequence = previous+1;
        var bytes = new byte[RecordBytes]; var w = new OnlinePacketWriter(bytes);
        w.Int(Magic); w.Int(sequence); w.Int((int)record.Phase); w.Long(record.Revision); w.Int(record.Profile);
        w.Text(record.Origin, 71); w.Text(record.Town, 16); w.Text(record.Account, 36);
        w.Text(record.Reservation, 36); w.Text(record.Fingerprint, 64);
        uint checksum = SaveCodec.Crc(bytes, 0, RecordBytes - 4);
        for (int i = 0; i < 4; i++) bytes[RecordBytes - 4 + i] = (byte)(checksum >> (i * 8));
        byte[]? actual = null;
        try
        {
            storage.Write(Name(bank), bytes); actual = storage.Read(Name(bank));
            if (actual.Length != bytes.Length) throw new IOException("Online recovery readback failed.");
            for (int i = 0; i < bytes.Length; i++) if (actual[i] != bytes[i]) throw new IOException("Online recovery readback failed.");
            return Decode(bank, actual);
        }
        finally { Array.Clear(bytes, 0, bytes.Length); if (actual != null) Array.Clear(actual, 0, actual.Length); }
    }
    Snapshot? ReadBank(int bank, ref bool damaged)
    {
        byte[]? bytes = null;
        try { bytes = storage.Read(Name(bank)); return Decode(bank, bytes); }
        catch (FileNotFoundException) { return null; }
        catch (IOException) { damaged = true; return null; }
        catch (InvalidOperationException) { damaged = true; return null; }
        catch (FormatException) { damaged = true; return null; }
        finally { if (bytes != null) Array.Clear(bytes, 0, bytes.Length); }
    }
    static string Name(int bank) => bank == 0 ? Bank0 : Bank1;
    static Snapshot Decode(int bank, byte[] bytes)
    {
        if (bytes.Length != RecordBytes) throw new FormatException("Invalid online recovery length.");
        uint checksum = 0; for (int i = 0; i < 4; i++) checksum |= (uint)bytes[RecordBytes - 4 + i] << (i * 8);
        if (checksum != SaveCodec.Crc(bytes, 0, RecordBytes - 4)) throw new FormatException("Invalid online recovery checksum.");
        var r = new OnlinePacketReader(bytes, 0, RecordBytes - 4);
        if (r.Int() != Magic) throw new FormatException("Unsupported online recovery record.");
        int sequence = r.Int(); if (sequence < 1) throw new FormatException("Invalid online recovery sequence.");
        var phase = (OnlineTravelPhase)r.Int(); long revision = r.Long(); int profile = r.Int();
        var record = new OnlineTravelRecord(r.Text(71), r.Text(16), r.Text(36), r.Text(36), revision, profile, phase, r.Text(64));
        while (r.Remaining > 0) if (r.Byte() != 0) throw new FormatException("Noncanonical online recovery padding.");
        return new(bank, sequence, checksum, record);
    }
}
