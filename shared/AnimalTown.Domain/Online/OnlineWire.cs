namespace AnimalTown.Online;

public enum OnlineFrameType : byte
{
    MovementInput = 1,
    ActorPosition = 2,
    SnapshotPage = 3,
    Heartbeat = 4,
    Pong = 5,
    GameplayTicket = 6,
    ReliableEvent = 7,
    Command = 8,
    BulkPage = 9,
    SessionState = 10
}

public enum OnlineFrameRead
{
    Incomplete,
    Invalid,
    Complete
}

public readonly struct OnlineFrameHeader
{
    public readonly OnlineFrameType Type;
    public readonly int PayloadLength;
    public readonly uint Epoch;
    public readonly uint Sequence;

    public OnlineFrameHeader(OnlineFrameType type, int payloadLength, uint epoch, uint sequence)
    {
        Type = type;
        PayloadLength = payloadLength;
        Epoch = epoch;
        Sequence = sequence;
    }
}

public readonly struct OnlineMovementInput
{
    public readonly sbyte X;
    public readonly sbyte Z;
    public readonly bool Run;

    public OnlineMovementInput(sbyte x, sbyte z, bool run)
    {
        X = x;
        Z = z;
        Run = run;
    }
}

public readonly struct OnlineActorPosition
{
    public readonly byte Slot;
    public readonly byte Room;
    public readonly int X;
    public readonly int Z;
    public readonly ushort Facing;
    public readonly uint AcknowledgedInput;

    // Coordinates are Q16.16. Facing spans a full turn from 0 through 65535.
    public OnlineActorPosition(byte slot, byte room, int x, int z, ushort facing, uint acknowledgedInput)
    {
        Slot = slot;
        Room = room;
        X = x;
        Z = z;
        Facing = facing;
        AcknowledgedInput = acknowledgedInput;
    }
}

public static class OnlineWire
{
    public const byte Version = 1;
    public const int HeaderSize = 12, MaxPayloadSize = 4096;
    public const int InputSize = 3, PositionSize = 16, GameplayTicketSize = 32;
    public const int PositionScale = 65536, ActorCount = 3;

    // Header layout: version, type, u16 payload length, u32 epoch, u32 sequence.
    // All integer fields use little-endian byte order on both host and console.
    public static bool TryWriteHeader(byte[] buffer, int offset, int available, OnlineFrameHeader header)
    {
        if (!ValidRange(buffer, offset, available) || available < HeaderSize ||
            !ValidLength(header.Type, header.PayloadLength))
            return false;

        buffer[offset] = Version;
        buffer[offset + 1] = (byte)header.Type;
        Write16(buffer, offset + 2, (ushort)header.PayloadLength);
        Write32(buffer, offset + 4, header.Epoch);
        Write32(buffer, offset + 8, header.Sequence);
        return true;
    }

    // Accepts a complete header even when its payload has yet to arrive.
    public static bool TryReadHeader(byte[] buffer, int offset, int available, out OnlineFrameHeader header)
    {
        header = default;
        if (!ValidRange(buffer, offset, available) || available < HeaderSize || buffer[offset] != Version)
            return false;

        var type = (OnlineFrameType)buffer[offset + 1];
        int length = Read16(buffer, offset + 2);
        if (!ValidLength(type, length)) return false;
        header = new OnlineFrameHeader(type, length, Read32(buffer, offset + 4), Read32(buffer, offset + 8));
        return true;
    }

    // Validates a header before returning Incomplete for a partial payload. Reads
    // one frame so a caller can retain and process any following stream bytes.
    public static OnlineFrameRead ReadFrame(byte[] buffer, int offset, int available,
        out OnlineFrameHeader header, out int frameBytes)
    {
        header = default;
        frameBytes = 0;
        if (!ValidRange(buffer, offset, available)) return OnlineFrameRead.Invalid;
        if (available < HeaderSize) return OnlineFrameRead.Incomplete;
        if (!TryReadHeader(buffer, offset, available, out var decoded)) return OnlineFrameRead.Invalid;

        int total = HeaderSize + decoded.PayloadLength;
        if (available < total) return OnlineFrameRead.Incomplete;
        header = decoded;
        frameBytes = total;
        return OnlineFrameRead.Complete;
    }

    public static bool TryWriteInput(byte[] buffer, int offset, int available,
        uint epoch, uint sequence, OnlineMovementInput input)
    {
        if (input.X == sbyte.MinValue || input.Z == sbyte.MinValue || available < HeaderSize + InputSize ||
            !TryWriteHeader(buffer, offset, available, new OnlineFrameHeader(OnlineFrameType.MovementInput, InputSize, epoch, sequence)))
            return false;

        int body = offset + HeaderSize;
        buffer[body] = unchecked((byte)input.X);
        buffer[body + 1] = unchecked((byte)input.Z);
        buffer[body + 2] = input.Run ? (byte)1 : (byte)0;
        return true;
    }

    public static bool TryReadInput(byte[] buffer, int offset, int available,
        out OnlineFrameHeader header, out OnlineMovementInput input)
    {
        input = default;
        header = default;
        if (ReadFrame(buffer, offset, available, out var decoded, out _) != OnlineFrameRead.Complete ||
            decoded.Type != OnlineFrameType.MovementInput)
            return false;

        int body = offset + HeaderSize;
        sbyte x = unchecked((sbyte)buffer[body]);
        sbyte z = unchecked((sbyte)buffer[body + 1]);
        byte flags = buffer[body + 2];
        if (x == sbyte.MinValue || z == sbyte.MinValue || flags > 1) return false;
        input = new OnlineMovementInput(x, z, flags == 1);
        header = decoded;
        return true;
    }

    public static bool TryWritePosition(byte[] buffer, int offset, int available,
        uint epoch, uint sequence, OnlineActorPosition position)
    {
        if (!ValidPosition(position) || available < HeaderSize + PositionSize ||
            !TryWriteHeader(buffer, offset, available, new OnlineFrameHeader(OnlineFrameType.ActorPosition, PositionSize, epoch, sequence)))
            return false;

        int body = offset + HeaderSize;
        buffer[body] = position.Slot;
        buffer[body + 1] = position.Room;
        Write32(buffer, body + 2, (uint)position.X);
        Write32(buffer, body + 6, (uint)position.Z);
        Write16(buffer, body + 10, position.Facing);
        Write32(buffer, body + 12, position.AcknowledgedInput);
        return true;
    }

    public static bool TryReadPosition(byte[] buffer, int offset, int available,
        out OnlineFrameHeader header, out OnlineActorPosition position)
    {
        header = default;
        position = default;
        if (ReadFrame(buffer, offset, available, out var decoded, out _) != OnlineFrameRead.Complete ||
            decoded.Type != OnlineFrameType.ActorPosition)
            return false;

        int body = offset + HeaderSize;
        var candidate = new OnlineActorPosition(buffer[body], buffer[body + 1],
            unchecked((int)Read32(buffer, body + 2)), unchecked((int)Read32(buffer, body + 6)),
            Read16(buffer, body + 10), Read32(buffer, body + 12));
        if (!ValidPosition(candidate)) return false;
        position = candidate;
        header = decoded;
        return true;
    }

    private static bool ValidRange(byte[] buffer, int offset, int count)
        => offset >= 0 && offset <= buffer.Length && count >= 0 && count <= buffer.Length - offset;

    private static bool ValidLength(OnlineFrameType type, int length)
    {
        switch (type)
        {
            case OnlineFrameType.MovementInput: return length == InputSize;
            case OnlineFrameType.ActorPosition: return length == PositionSize;
            case OnlineFrameType.Heartbeat:
            case OnlineFrameType.Pong: return length == 0;
            case OnlineFrameType.GameplayTicket: return length == GameplayTicketSize;
            case OnlineFrameType.SessionState: return length == 1;
            case OnlineFrameType.ReliableEvent:
            case OnlineFrameType.Command: return length >= 1 && length <= 512;
            case OnlineFrameType.SnapshotPage:
            case OnlineFrameType.BulkPage: return length >= 1 && length <= MaxPayloadSize;
            default: return false;
        }
    }

    // Scene-specific collision, room size and visibility are enforced by the caller.
    private static bool ValidPosition(OnlineActorPosition position)
        => position.Slot < ActorCount && BuildingWorld.ValidSpace(position.Room) &&
            position.X >= 0 && position.X < Town.Width * PositionScale &&
            position.Z >= 0 && position.Z < Town.Height * PositionScale;

    private static ushort Read16(byte[] buffer, int at)
        => (ushort)(buffer[at] | buffer[at + 1] << 8);

    private static uint Read32(byte[] buffer, int at)
        => (uint)buffer[at] | (uint)buffer[at + 1] << 8 | (uint)buffer[at + 2] << 16 | (uint)buffer[at + 3] << 24;

    private static void Write16(byte[] buffer, int at, ushort value)
    {
        buffer[at] = unchecked((byte)value);
        buffer[at + 1] = (byte)(value >> 8);
    }

    private static void Write32(byte[] buffer, int at, uint value)
    {
        buffer[at] = unchecked((byte)value);
        buffer[at + 1] = unchecked((byte)(value >> 8));
        buffer[at + 2] = unchecked((byte)(value >> 16));
        buffer[at + 3] = (byte)(value >> 24);
    }
}
