namespace AnimalTown.Online;

// Explicit gameplay allowlist. There is deliberately no generic world-action
// command: tools, harvesting, attachments and cross-town mail have no opcode.
public enum OnlineCommandKind : byte
{
    Snapshot = 1, Replay = 2, EnterRoom = 3, Place = 4, MovePlacement = 5,
    PickupPlacement = 6, ShopView = 7, Buy = 8, List = 9, CancelListing = 10,
    Reprice = 11, Jobs = 12, AskJob = 13, AcceptJob = 14, CompleteJob = 15,
    AbandonJob = 16, CollectReward = 17, Chat = 18, Mute = 19, Report = 20,
    SendMail = 21, ReviewBegin = 22, ReviewDecision = 23, ReviewEnd = 24,
    Inventory = 25, Return = 26
}

// Opaque 128-bit IDs have no CLR Guid dependency on the console. Their bytes
// round-trip unchanged through the host's Guid representation.
public readonly struct OnlineId
{
    public readonly ulong First, Second;
    public OnlineId(ulong first, ulong second) { First = first; Second = second; }
    public bool IsEmpty => First == 0 && Second == 0;
    public bool Same(OnlineId other) => First == other.First && Second == other.Second;
}

public sealed class OnlineCommand
{
    public OnlineCommandKind Kind;
    public OnlineId Id, Entity;
    public long Revision;
    public int A, B, C, D, E, F;
    public string Text = "";
}

public static class OnlineGameplayCodec
{
    public const byte Version = 1, Result = 1, Event = 2;
    public const int MaximumTextUnits = 2000;
    public static int WriteCommand(byte[] bytes, OnlineCommand command)
    {
        var w = new OnlinePacketWriter(bytes);
        w.Byte(Version); w.Byte((byte)command.Kind); w.Id(command.Id); w.Id(command.Entity);
        w.Long(command.Revision); w.Int(command.A); w.Int(command.B); w.Int(command.C);
        w.Int(command.D); w.Int(command.E); w.Int(command.F); w.Text(command.Text, MaximumTextUnits);
        return w.Count;
    }
    public static OnlineCommand ReadCommand(byte[] bytes, int offset, int count)
    {
        var r = new OnlinePacketReader(bytes, offset, count);
        if (r.Byte() != Version) throw new InvalidOperationException("Unsupported gameplay command.");
        var command = new OnlineCommand { Kind = (OnlineCommandKind)r.Byte(), Id = r.Id(), Entity = r.Id(),
            Revision = r.Long(), A = r.Int(), B = r.Int(), C = r.Int(), D = r.Int(), E = r.Int(), F = r.Int(),
            Text = r.Text(MaximumTextUnits) };
        r.End();
        if (command.Kind < OnlineCommandKind.Snapshot || command.Kind > OnlineCommandKind.Return)
            throw new InvalidOperationException("Unsupported gameplay command.");
        return command;
    }
}

// Fixed-capacity writers are also used by the host projections. No reflection,
// runtime JSON, unsafe buffers or implicit object serialization on the wire.
public sealed class OnlinePacketWriter
{
    readonly byte[] bytes;
    public int Count { get; private set; }
    public OnlinePacketWriter(byte[] bytes) { this.bytes = bytes; }
    void Need(int count) { if (count < 0 || count > bytes.Length - Count) throw new InvalidOperationException("Gameplay packet exceeds its bound."); }
    public void Byte(byte value) { Need(1); bytes[Count++] = value; }
    public void Int(int value) { Need(4); for (int i = 0; i < 4; i++) bytes[Count++] = (byte)((uint)value >> (i * 8)); }
    public void Long(long value) { Need(8); for (int i = 0; i < 8; i++) bytes[Count++] = (byte)((ulong)value >> (i * 8)); }
    public void Id(OnlineId value) { Long(unchecked((long)value.First)); Long(unchecked((long)value.Second)); }
    public void Text(string text, int maximum)
    {
        if (text == null || text.Length > maximum || text.Length > ushort.MaxValue || !ValidText(text))
            throw new InvalidOperationException("Invalid gameplay text.");
        Need(2 + text.Length * 2); Byte((byte)text.Length); Byte((byte)(text.Length >> 8));
        for (int i = 0; i < text.Length; i++) { Byte((byte)text[i]); Byte((byte)(text[i] >> 8)); }
    }
    public void Bytes(byte[] source, int offset, int count)
    { Need(count); if (offset < 0 || offset > source.Length - count) throw new InvalidOperationException("Invalid gameplay range."); Array.Copy(source, offset, bytes, Count, count); Count += count; }
    internal static bool ValidText(string text)
    {
        for (int i = 0; i < text.Length; i++)
        {
            int c = text[i];
            if (c >= 0xdc00 && c <= 0xdfff) return false;
            if (c >= 0xd800 && c <= 0xdbff && (++i == text.Length || text[i] < 0xdc00 || text[i] > 0xdfff)) return false;
        }
        return true;
    }
}

public sealed class OnlinePacketReader
{
    readonly byte[] bytes;
    readonly int end;
    int at;
    public int Remaining => end - at;
    public OnlinePacketReader(byte[] bytes, int offset, int count)
    {
        if (bytes == null || offset < 0 || count < 0 || offset > bytes.Length - count)
            throw new InvalidOperationException("Invalid gameplay packet.");
        this.bytes = bytes; at = offset; end = offset + count;
    }
    void Need(int count) { if (count < 0 || count > end - at) throw new InvalidOperationException("Truncated gameplay packet."); }
    public byte Byte() { Need(1); return bytes[at++]; }
    public int Int() { Need(4); int value = 0; for (int i = 0; i < 4; i++) value |= bytes[at++] << (i * 8); return value; }
    public long Long() { Need(8); ulong value = 0; for (int i = 0; i < 8; i++) value |= (ulong)bytes[at++] << (i * 8); return unchecked((long)value); }
    public OnlineId Id() => new(unchecked((ulong)Long()), unchecked((ulong)Long()));
    public string Text(int maximum)
    {
        int count = Byte() | Byte() << 8;
        if (count > maximum) throw new InvalidOperationException("Gameplay text exceeds its bound.");
        Need(count * 2); var chars = new char[count];
        for (int i = 0; i < count; i++) chars[i] = (char)(Byte() | Byte() << 8);
        string value = new string(chars);
        if (!OnlinePacketWriter.ValidText(value)) throw new InvalidOperationException("Invalid gameplay text.");
        return value;
    }
    public void Bytes(byte[] target, int offset, int count)
    { Need(count); if (offset < 0 || offset > target.Length - count) throw new InvalidOperationException("Invalid gameplay range."); Array.Copy(bytes, at, target, offset, count); at += count; }
    public void End() { if (at != end) throw new InvalidOperationException("Trailing gameplay data."); }
}
